// App shell: sidebar, hash router, and the run helpers shared by every view.

import { api } from './api.js';
import { fill, h, icon, isProdLike, sleep } from './dom.js';
import { activeRun, refreshProject, refreshRuns, startPolling, store } from './store.js';
import { dialog, toast } from './ui.js';
import * as overview from './views/overview.js';
import * as plan from './views/plan.js';
import * as runs from './views/runs.js';
import * as tasks from './views/tasks.js';

const ROUTES = { overview, plan, runs, tasks };
const NAV = [
  ['overview', 'Overview', 'grid'],
  ['plan', 'Plan', 'diff'],
  ['runs', 'Runs', 'terminal'],
  ['tasks', 'Tasks', 'list'],
];

// Desktop bridge exposed by the Electron preload script (absent in a plain browser).
const desktop = window.tfstudio ?? null;

const VAR_HINTS = {
  ID: { hint: 'Lock ID printed in the "Error acquiring the state lock" message.' },
  NAME: { hint: 'Must match a folder under environments/.' },
};

export const ctx = {
  navigate(path) {
    location.hash = `#/${path}`;
  },

  /** Starts a run; by default jumps to its console. Returns the run summary or null. */
  async startRun(taskNames, vars = {}, { navigate = true } = {}) {
    try {
      const run = await api.startRun(taskNames, vars);
      await refreshRuns();
      if (navigate) ctx.navigate(`runs/${run.id}`);
      return run;
    } catch (error) {
      const active = activeRun();
      toast(error.message, {
        tone: error.status === 409 ? 'warning' : 'error',
        action: error.status === 409 && active ? { label: 'Show run', onClick: () => ctx.navigate(`runs/${active.id}`) } : null,
      });
      return null;
    }
  },

  /** For quick housekeeping tasks (workspace select…): run without leaving the page, wait, refresh. */
  async runAndWait(taskNames, vars = {}) {
    const started = await ctx.startRun(taskNames, vars, { navigate: false });
    if (!started) return null;
    let run = started;
    while (run.state === 'running') {
      await sleep(400);
      run = await api.run(run.id);
    }
    await Promise.all([refreshRuns(), refreshProject()]);
    if (run.state !== 'succeeded') {
      toast(`${taskNames.join(' → ')}: ${run.state}`, {
        tone: 'error', action: { label: 'View output', onClick: () => ctx.navigate(`runs/${run.id}`) },
      });
    }
    return run;
  },

  /** Runs a task from the catalog, asking for vars / confirmation when the task needs it. */
  async runTask(task, presetVars = {}) {
    const workspace = store.state.project?.workspace;
    const needsDialog = task.requiredVars.some((v) => !presetVars[v]) || task.dangerous || task.interactive;
    let vars = presetVars;
    if (needsDialog) {
      const body = [h('p', null, task.description || task.name)];
      if (task.interactive) {
        body.push(h('p', null, 'Terraform will show its plan and ask for confirmation in the run console. Type ', h('code', null, 'yes'), ' there to proceed.'));
      }
      if (task.dangerous && workspace) {
        body.push(h('p', null, 'Active workspace: ', h('span', { class: `ws-pill${isProdLike(workspace) ? ' is-prod' : ''}` }, workspace)));
      }
      const result = await dialog({
        title: `Run ${task.name}`,
        body,
        fields: task.requiredVars.map((name) => ({ name, label: name, value: presetVars[name], ...VAR_HINTS[name] })),
        requireText: task.name === 'tf:destroy' || (task.dangerous && isProdLike(workspace)) ? workspace : null,
        confirmLabel: 'Run',
        tone: task.dangerous ? 'danger' : 'primary',
      });
      if (!result) return null;
      vars = { ...presetVars, ...result.values };
    }
    return ctx.startRun([task.name], vars);
  },

  async selectWorkspace(name) {
    if (isProdLike(name)) {
      const ok = await dialog({
        title: `Switch to ${name}?`,
        body: [h('p', null, 'Every Terraform task will target this environment until you switch back.')],
        confirmLabel: `Switch to ${name}`,
        tone: 'danger',
      });
      if (!ok) return;
    }
    const run = await ctx.runAndWait(['tf:workspace:select'], { NAME: name });
    if (run?.state === 'succeeded') {
      const backend = store.state.project?.backend;
      toast(`Active workspace: ${name}`, {
        tone: 'success',
        action: backend?.state === 'mismatch' ? { label: 'Reconfigure backend', onClick: () => ctx.startRun(['tf:reconfigure']) } : null,
      });
    }
  },
};

// ---- Sidebar ------------------------------------------------------------------------------

const sidebar = document.getElementById('sidebar');
let sidebarSignature = null;

function renderSidebar() {
  const { project, connected, error } = store.state;
  const current = currentRoute().name;
  const running = activeRun();

  // Polling fires every few seconds: only rebuild when something visible changed, so an open
  // <select> or a focused button isn't yanked away from the user.
  const signature = JSON.stringify([project, connected, error, current, running?.id, running?.steps, running?.acceptsInput]);
  if (signature === sidebarSignature) return;
  sidebarSignature = signature;

  const workspaceSelect = project && h('select', {
    class: 'select', 'aria-label': 'Active workspace',
    onChange: (event) => {
      const name = event.target.value;
      event.target.value = project.workspace ?? '';
      if (name && name !== project.workspace) ctx.selectWorkspace(name);
    },
  },
  !project.workspace && h('option', { value: '' }, '— none —'),
  project.environments.map((env) => h('option', { value: env.name, selected: env.isActive }, env.name)));

  fill(sidebar, 
    h('div', { class: 'brand' },
      h('div', { class: 'brand-mark' }, icon('logo')),
      h('div', null, h('div', { class: 'brand-name' }, 'TF Studio'), h('div', { class: 'brand-sub' }, 'Terraform boilerplate'))),

    project && h('div', { class: 'side-section' },
      h('div', { class: 'side-label' }, 'Project'),
      h('div', { class: 'project-name', title: project.root }, project.name),
      h('div', { class: 'project-path', title: project.root }, project.root),
      desktop && h('div', { class: 'row' },
        h('button', { class: 'btn btn-sm', onClick: () => desktop.openProject() }, icon('folder'), 'Open…'),
        h('button', { class: 'btn btn-sm btn-ghost', onClick: () => desktop.revealProject() }, 'Show in Explorer'))),

    project && h('div', { class: 'side-section' },
      h('div', { class: 'side-label' }, 'Workspace'),
      h('div', { class: 'row' },
        h('span', { class: `ws-pill${isProdLike(project.workspace) ? ' is-prod' : ''}` }, project.workspace ?? 'none'),
        h('span', { class: 'spacer' })),
      workspaceSelect),

    h('nav', { class: 'nav' },
      NAV.map(([route, label, iconName]) => h('a', {
        href: `#/${route}`, 'aria-current': route === current ? 'page' : null,
      }, icon(iconName), label,
      route === 'plan' && project?.planFiles.jsonExists && h('span', { class: 'nav-count badge tone-accent' }, 'ready')))),

    running && h('a', { class: 'side-run', href: `#/runs/${running.id}` },
      h('span', { class: 'spinner' }),
      h('div', { class: 'stack' },
        h('div', { class: 'mono' }, running.steps.find((s) => s.state === 'running')?.task ?? running.steps[0].task),
        h('div', { class: 'muted small' }, running.acceptsInput ? 'Running · may need input' : 'Running…'))),

    h('div', { class: 'side-foot' },
      h('div', { class: 'conn' }, h('span', { class: `dot${connected ? '' : ' down'}` }), connected ? 'Connected' : 'Server unreachable'),
      error && !connected && h('div', null, error)),
  );
}

// ---- Router -------------------------------------------------------------------------------

const viewHost = document.getElementById('view');
let unmountCurrent = null;
let mountedKey = null;

function currentRoute() {
  const [name, ...rest] = location.hash.replace(/^#\/?/, '').split('/');
  return { name: ROUTES[name] ? name : 'overview', arg: rest.length ? decodeURIComponent(rest.join('/')) : null };
}

function route() {
  const { name, arg } = currentRoute();
  const key = `${name}/${arg ?? ''}`;
  if (key === mountedKey) return;
  mountedKey = key;
  // A confirmation belongs to the page that opened it: cancel it on navigation (e.g. browser back).
  for (const open of document.querySelectorAll('dialog.modal[open]')) open.dispatchEvent(new Event('cancel'));
  unmountCurrent?.();
  fill(viewHost);
  viewHost.scrollTop = 0;
  unmountCurrent = ROUTES[name].mount(viewHost, ctx, arg) ?? null;
  renderSidebar();
}

// ---- Boot ---------------------------------------------------------------------------------

if (!api.hasToken()) {
  fill(viewHost, h('div', { class: 'page' }, h('div', { class: 'empty' },
    icon('alert'),
    h('h2', null, 'Not connected'),
    h('p', null, 'Open TF Studio from the desktop app, or use the URL (with #token=…) printed by the server.'))));
} else {
  store.subscribe(renderSidebar);
  window.addEventListener('hashchange', route);
  route();
  startPolling();
}
