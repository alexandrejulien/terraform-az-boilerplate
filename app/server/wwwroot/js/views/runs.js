// Runs: history on the left, live console on the right (replays the log, then follows it via SSE).

import { api, streamRunEvents } from '../api.js';
import { AnsiRenderer } from '../ansi.js';
import { duration, fill, h, icon, relativeTime, sleep } from '../dom.js';
import { store } from '../store.js';
import { stateBadge, toast } from '../ui.js';

export function mount(root, ctx, runId) {
  const list = h('nav', { class: 'run-list', 'aria-label': 'Runs' });
  const detail = h('div', { class: 'run-detail' });
  root.append(h('div', { class: 'runs-layout' }, list, detail));

  const selectedId = runId ?? store.state.runs[0]?.id ?? null;
  const consoleView = selectedId ? createConsole(selectedId, ctx) : null;
  detail.append(consoleView?.element ?? h('div', { class: 'empty' },
    icon('terminal'), h('h2', null, 'No runs yet'), h('p', null, 'Start a task from Overview, Plan or Tasks.')));

  let signature = null;
  const renderList = () => {
    const runs = store.state.runs;
    const next = JSON.stringify(runs);
    if (next === signature) return;
    signature = next;
    fill(list, 
      h('div', { class: 'run-list-head' }, h('h2', null, 'Runs'), h('span', { class: 'muted small' }, 'This session')),
      runs.map((run) => h('a', { class: 'run-item', href: `#/runs/${run.id}`, 'aria-current': String(run.id === selectedId) },
        h('div', { class: 'tasks' }, run.steps.map((s) => s.task).join(' → ')),
        h('div', { class: 'meta' },
          stateBadge(run.state),
          run.workspace && h('span', null, run.workspace),
          h('span', null, relativeTime(run.startedAt)),
          h('span', null, duration(run.startedAt, run.endedAt))))));
  };

  const unsubscribe = store.subscribe(renderList);
  renderList();
  return () => {
    unsubscribe();
    consoleView?.dispose();
  };
}

function createConsole(runId, ctx) {
  const head = h('div', { class: 'console-head' });
  const output = h('pre', { class: 'console', tabindex: '0', 'aria-label': 'Run output' });
  const input = h('input', {
    class: 'input', placeholder: 'Reply to the process (e.g. yes) and press Enter', autocomplete: 'off', spellcheck: 'false',
  });
  const inputBar = h('form', { class: 'console-input', hidden: true, onSubmit: sendInput },
    input, h('button', { class: 'btn btn-primary', type: 'submit' }, icon('send'), 'Send'));
  const element = h('div', { class: 'run-console' }, head, output, inputBar);
  const elapsed = h('span', { class: 'muted small' });

  const renderer = new AnsiRenderer(output);
  const controller = new AbortController();
  let summary = null;
  let cursor = 0;
  let disposed = false;
  let stickToBottom = true;
  let tail = '';

  output.addEventListener('scroll', () => {
    stickToBottom = output.scrollHeight - output.scrollTop - output.clientHeight < 40;
  });

  function onEvent(type, event) {
    cursor = Math.max(cursor, event.seq);
    if (type === 'output') {
      renderer.write(event.text, event.stream);
      tail = (tail + event.text).slice(-200);
      if (stickToBottom) output.scrollTop = output.scrollHeight;
      highlightPrompt();
    } else if (type === 'status') {
      summary = event.run;
      renderHead();
    } else if (type === 'trimmed') {
      renderer.note(event.text);
    }
  }

  // Terraform asks "Enter a value:" for approvals and missing variables.
  function highlightPrompt() {
    const waiting = summary?.acceptsInput && /Enter a value:\s*(\x1b\[[0-9;]*m)*\s*$/.test(tail);
    inputBar.classList.toggle('attention', Boolean(waiting));
    if (waiting && document.activeElement !== input) input.focus();
  }

  function renderHead() {
    if (!summary) return;
    const running = summary.state === 'running';
    const includesPlan = summary.steps.some((s) => s.task === 'tf:plan');
    fill(head, 
      h('div', { class: 'row wrap' },
        h('h1', null, 'Run'),
        stateBadge(summary.state),
        summary.workspace && h('span', { class: 'ws-pill' }, summary.workspace),
        h('span', { class: 'muted small' }, new Date(summary.startedAt).toLocaleString()),
        elapsed,
        summary.exitCode != null && summary.state === 'failed' && h('span', { class: 'muted small' }, `exit code ${summary.exitCode}`),
        h('span', { class: 'spacer' }),
        h('button', { class: 'btn btn-sm btn-ghost', onClick: copyOutput }, icon('copy'), 'Copy'),
        summary.state === 'succeeded' && includesPlan && h('a', { class: 'btn btn-sm btn-primary', href: '#/plan' }, 'Review plan', icon('arrow')),
        running && h('button', { class: 'btn btn-sm btn-danger', onClick: cancel }, icon('stop'), 'Cancel')),
      h('div', { class: 'steps' }, summary.steps.map((step, i) => [
        i > 0 && h('span', { class: 'arrow' }, '→'),
        h('span', { class: `badge state-${step.state}` },
          step.state === 'running' && h('span', { class: 'spinner' }), step.task),
      ])));
    tick();
    inputBar.hidden = !summary.acceptsInput;
    if (!summary.acceptsInput) inputBar.classList.remove('attention');
  }

  async function follow() {
    try {
      summary = await api.run(runId);
      renderHead();
    } catch (error) {
      fill(head, h('h1', null, 'Run not found'));
      output.textContent = `${error.message}\nRun history is kept in memory and is cleared when TF Studio restarts.`;
      return;
    }
    while (!disposed) {
      try {
        await streamRunEvents(runId, cursor, onEvent, controller.signal);
        if (summary?.state !== 'running') return;
      } catch (error) {
        if (disposed || error.name === 'AbortError') return;
        if (error.status === 404) return;
      }
      await sleep(1000); // connection dropped mid-run: resume from the last sequence number
    }
  }

  async function sendInput(event) {
    event.preventDefault();
    const text = input.value;
    input.value = '';
    try {
      await api.sendInput(runId, text);
    } catch (error) {
      toast(error.message, { tone: 'error' });
    }
  }

  async function cancel() {
    try {
      await api.cancelRun(runId);
    } catch (error) {
      toast(error.message, { tone: 'error' });
    }
  }

  async function copyOutput() {
    try {
      await navigator.clipboard.writeText(output.textContent);
      toast('Output copied', { tone: 'success', timeout: 2000 });
    } catch {
      toast('Clipboard is not available', { tone: 'error' });
    }
  }

  // Only the elapsed time ticks: rebuilding the header every second would swallow clicks on Cancel.
  function tick() {
    if (summary) elapsed.textContent = `· ${duration(summary.startedAt, summary.endedAt)}`;
  }
  const timer = setInterval(tick, 1000);
  follow();

  return {
    element,
    dispose() {
      disposed = true;
      controller.abort();
      clearInterval(timer);
    },
  };
}
