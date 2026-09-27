// Tasks: every task from the project's Taskfile (task --list-all --json), grouped by namespace.

import { fill, h, icon } from '../dom.js';
import { activeRun, loadTasks, store } from '../store.js';
import { notice } from '../ui.js';

const GROUPS = {
  tf: 'Terraform',
  docs: 'Documentation',
  security: 'Security',
  costs: 'Costs',
  install: 'Tooling setup',
  '': 'General',
};

export function mount(root, ctx) {
  const page = h('div', { class: 'page' });
  const listHost = h('div', { class: 'card-body flush' });
  let query = '';
  let signature = null;

  page.append(
    h('div', { class: 'page-header' },
      h('div', null,
        h('h1', null, 'Tasks'),
        h('div', { class: 'subtitle' }, 'Everything in Taskfile.yml and .tasks/*.yml. Runs use the active workspace from .env.')),
      h('div', { class: 'page-actions' },
        h('div', { class: 'search' }, icon('search'), h('input', {
          class: 'input', type: 'search', placeholder: 'Filter tasks',
          onInput: (event) => { query = event.target.value.trim().toLowerCase(); signature = null; render(); },
        })),
        h('button', { class: 'btn btn-ghost', title: 'Reload the Taskfile', onClick: () => { store.set({ tasks: null }); loadTasks(); } }, icon('refresh')))),
    h('section', { class: 'card' }, listHost));
  root.append(page);

  function render() {
    const { tasks } = store.state;
    const busy = Boolean(activeRun());
    const next = JSON.stringify([tasks, busy, query]);
    if (next === signature) return;
    signature = next;

    if (!tasks) {
      fill(listHost, h('div', { class: 'empty' }, h('span', { class: 'spinner' }), 'Reading Taskfile…'));
      return;
    }
    if (tasks.error) {
      fill(listHost, h('div', { class: 'card-body' }, notice('danger', tasks.error)));
      return;
    }

    const visible = tasks.filter((t) => t.name !== 'default'
      && (!query || t.name.toLowerCase().includes(query) || t.description.toLowerCase().includes(query)));
    const groups = Map.groupBy(visible, (t) => t.namespace);
    const order = [...Object.keys(GROUPS), ...[...groups.keys()].filter((k) => !(k in GROUPS)).sort()];

    fill(listHost, visible.length === 0
      ? h('div', { class: 'empty' }, 'No task matches this filter.')
      : order.filter((key) => groups.has(key)).map((key) => [
        h('div', { class: 'task-group-title' }, GROUPS[key] ?? key),
        h('div', { class: 'list' }, groups.get(key).map((task) => h('div', { class: 'list-row' },
          h('div', { class: 'grow' },
            h('div', { class: 'row wrap' },
              h('span', { class: 'task-name' }, task.name),
              task.dangerous && h('span', { class: 'badge tone-danger' }, 'confirmation'),
              task.interactive && h('span', { class: 'badge tone-warn' }, 'prompts for input'),
              task.requiredVars.map((v) => h('span', { class: 'badge tone-accent' }, `${v}=…`)),
              task.aliases.map((alias) => h('span', { class: 'badge' }, alias))),
            h('div', { class: 'sub' }, task.description || 'No description')),
          h('button', {
            class: `btn btn-sm ${task.dangerous ? 'btn-danger' : ''}`, disabled: busy,
            onClick: () => ctx.runTask(task),
          }, icon('play'), 'Run')))),
      ]));
  }

  const unsubscribe = store.subscribe(render);
  render();
  if (!store.state.tasks || store.state.tasks.error) loadTasks();
  return unsubscribe;
}
