// Overview: workspace, backend state, the init → validate → lint → plan workflow, quality checks, tools.

import { api } from '../api.js';
import { fill, h, icon, isProdLike, relativeTime } from '../dom.js';
import { activeRun, lastStepOf, loadTasks, loadTools, store } from '../store.js';
import { dialog, notice, toast } from '../ui.js';

const PIPELINE = [
  { task: 'tf:init', title: 'Init', sub: 'terraform init with the workspace backend.tfvars' },
  { task: 'tf:validate', title: 'Validate', sub: 'terraform validate' },
  { task: 'tf:lint', title: 'Lint', sub: 'tflint' },
  { task: 'tf:plan', title: 'Plan', sub: 'terraform plan → plan.tfplan + plan.tfgraph' },
];

const QUALITY = [
  { task: 'security:scan', title: 'Security scan', sub: 'Checkov policy checks', tool: 'checkov', icon: 'shield' },
  { task: 'costs:analysis', title: 'Cost analysis', sub: 'Infracost breakdown (needs an API key)', tool: 'infracost', icon: 'dollar' },
  { task: 'docs:generate', title: 'Generate docs', sub: 'terraform-docs → Terrraform.md', tool: 'terraform-docs', icon: 'doc' },
];

export function mount(root, ctx) {
  const page = h('div', { class: 'page' });
  root.append(page);
  let signature = null;

  const render = () => {
    const { project, runs, tools, tasks } = store.state;
    const next = JSON.stringify([project, runs, tools, tasks]);
    if (next === signature) return;
    signature = next;

    if (!project) {
      fill(page, h('div', { class: 'empty' }, h('span', { class: 'spinner' }), 'Loading project…'));
      return;
    }

    const known = Array.isArray(tasks) ? new Set(tasks.map((t) => t.name)) : null;
    const exists = (task) => !known || known.has(task);
    const busy = Boolean(activeRun());

    fill(page, 
      h('div', { class: 'page-header' },
        h('div', null,
          h('h1', null, 'Overview'),
          h('div', { class: 'subtitle' }, 'Workspace ',
            h('span', { class: `ws-pill${isProdLike(project.workspace) ? ' is-prod' : ''}` }, project.workspace ?? 'none'),
            project.workspace && h('span', { class: 'muted' }, ` · environments/${project.workspace}/`))),
        h('div', { class: 'page-actions' },
          h('button', {
            class: 'btn btn-primary', disabled: busy || !project.workspace,
            onClick: () => ctx.startRun(PIPELINE.map((s) => s.task).filter(exists)),
          }, icon('play'), 'Run workflow'))),

      project.warnings.map((message) => notice('warning', message)),
      backendNotice(project.backend, ctx),

      h('div', { class: 'grid' },
        workflowCard(project, exists, busy, ctx),
        environmentsCard(project, ctx)),
      h('div', { class: 'grid' },
        qualityCard(tools, exists, busy, ctx),
        toolsCard(tools)),
    );
  };

  const unsubscribe = store.subscribe(render);
  render();
  if (!store.state.tools) loadTools();
  if (!store.state.tasks) loadTasks();
  return unsubscribe;
}

function backendNotice(backend, ctx) {
  if (backend.state === 'ok' || backend.state === 'unknown') return null;
  const task = backend.state === 'mismatch' ? 'tf:reconfigure' : 'tf:init';
  const details = backend.mismatchedKeys.length ? ` Differs on: ${backend.mismatchedKeys.join(', ')}.` : '';
  return h('div', { class: 'notice warning' }, icon('alert'),
    h('div', { class: 'spacer' }, backend.message + details),
    h('button', { class: 'btn btn-sm', onClick: () => ctx.startRun([task]) }, `Run ${task}`));
}

function lastRunLine(task) {
  const step = lastStepOf(task);
  if (!step) return h('span', { class: 'muted small' }, 'Not run yet');
  return h('a', { class: 'small', href: `#/runs/${step.runId}` },
    `${step.state[0].toUpperCase()}${step.state.slice(1)} ${relativeTime(step.endedAt ?? step.startedAt)}`);
}

function workflowCard(project, exists, busy, ctx) {
  const steps = PIPELINE.filter((s) => exists(s.task));
  const planFiles = project.planFiles;
  return h('section', { class: 'card' },
    h('div', { class: 'card-head' }, h('h2', null, 'Workflow'), h('span', { class: 'hint' }, 'Runs sequentially, stops at the first failure')),
    h('div', { class: 'card-body flush' },
      h('div', { class: 'pipeline' }, steps.map((step, index) => {
        const last = lastStepOf(step.task);
        return h('div', { class: 'step' },
          h('div', { class: `step-index state-${last?.state ?? 'pending'}` },
            last?.state === 'succeeded' ? icon('check') : last?.state === 'failed' ? icon('x') : String(index + 1)),
          h('div', null,
            h('div', { class: 'row' }, h('span', { class: 'title' }, step.title), h('code', { class: 'muted' }, step.task)),
            h('div', { class: 'sub' }, step.sub, ' · ', lastRunLine(step.task))),
          h('button', { class: 'btn btn-sm', disabled: busy, onClick: () => ctx.startRun([step.task]) }, 'Run'));
      })),
      planFiles.jsonExists && h('div', { class: 'list-row' },
        icon('diff'),
        h('div', { class: 'grow' },
          h('div', { class: 'title' }, 'Plan ready for review'),
          h('div', { class: 'sub' }, `Generated ${relativeTime(planFiles.planModifiedAt ?? planFiles.jsonModifiedAt)}`)),
        h('a', { class: 'btn btn-primary btn-sm', href: '#/plan' }, 'Review plan', icon('arrow')))));
}

function environmentsCard(project, ctx) {
  const createTask = (store.state.tasks ?? []).find?.((t) => t.name === 'tf:workspace:create')
    ?? { name: 'tf:workspace:create', description: 'Create a new Terraform workspace and make it active in .env', requiredVars: ['NAME'], dangerous: false, interactive: false };

  return h('section', { class: 'card' },
    h('div', { class: 'card-head' },
      h('h2', null, 'Environments'),
      h('button', { class: 'btn btn-sm btn-ghost', onClick: () => ctx.runTask(createTask) }, icon('plus'), 'New workspace')),
    h('div', { class: 'card-body flush' },
      project.environments.length === 0
        ? h('div', { class: 'empty' }, 'No environments/ folders found.')
        : h('div', { class: 'list' }, project.environments.map((env) => h('div', { class: 'list-row' },
          h('div', { class: 'grow' },
            h('div', { class: 'row' },
              h('span', { class: 'env-name' }, env.name),
              env.isActive && h('span', { class: 'badge tone-accent' }, 'active'),
              isProdLike(env.name) && h('span', { class: 'badge tone-danger' }, 'production')),
            h('div', { class: 'checks' },
              check('backend.tfvars', env.hasBackend),
              check('variables.tfvars', env.hasVariables),
              env.hasLocalVariables && h('span', { class: 'check ok' }, icon('check'), 'local.tfvars (untracked)'))),
          h('button', { class: 'btn btn-sm btn-ghost', onClick: () => showEnvironmentFiles(env.name) }, icon('eye'), 'Files'),
          !env.isActive && h('button', { class: 'btn btn-sm', onClick: () => ctx.selectWorkspace(env.name) }, 'Switch')))),
      h('div', { class: 'list-row muted small' },
        'New environment: add environments/<name>/ with backend.tfvars and variables.tfvars, then create its workspace.')));
}

function check(label, ok) {
  return h('span', { class: `check ${ok ? 'ok' : 'missing'}` }, icon(ok ? 'check' : 'x'), label);
}

async function showEnvironmentFiles(name) {
  try {
    const files = await api.environment(name);
    await dialog({
      title: `environments/${name}`,
      wide: true,
      body: [
        h('div', { class: 'field' }, h('label', null, 'backend.tfvars'), h('pre', { class: 'code-block' }, files.backend ?? '(missing)')),
        h('div', { class: 'field' }, h('label', null, 'variables.tfvars'), h('pre', { class: 'code-block' }, files.variables ?? '(missing)')),
        h('p', { class: 'small muted' }, 'Committed files. Keep real subscription IDs and secrets in local.tfvars or CI secrets, never here.'),
      ],
      confirmLabel: null,
      cancelLabel: 'Close',
    });
  } catch (error) {
    toast(error.message, { tone: 'error' });
  }
}

function qualityCard(tools, exists, busy, ctx) {
  const toolByName = Array.isArray(tools) ? Object.fromEntries(tools.map((t) => [t.name, t])) : {};
  const items = QUALITY.filter((q) => exists(q.task));
  return h('section', { class: 'card' },
    h('div', { class: 'card-head' }, h('h2', null, 'Quality checks')),
    h('div', { class: 'card-body flush' }, h('div', { class: 'list' }, items.map((item) => {
      const tool = toolByName[item.tool];
      const missing = tool && !tool.found;
      return h('div', { class: 'list-row' },
        icon(item.icon, 'icon-lg'),
        h('div', { class: 'grow' },
          h('div', { class: 'row' }, h('span', { class: 'title' }, item.title), h('code', { class: 'muted' }, item.task)),
          h('div', { class: 'sub' }, item.sub, ' · ', missing ? h('span', { class: 'small' }, `${tool.title} not installed`) : lastRunLine(item.task))),
        h('button', {
          class: 'btn btn-sm', disabled: busy || missing, title: missing ? tool.installHint : null,
          onClick: () => ctx.startRun([item.task]),
        }, 'Run'));
    }))));
}

function toolsCard(tools) {
  const body = !tools
    ? h('div', { class: 'empty' }, h('span', { class: 'spinner' }), 'Checking tools…')
    : tools.error
      ? h('div', { class: 'card-body' }, notice('danger', tools.error))
      : h('table', { class: 'table' },
        h('thead', null, h('tr', null, h('th', null, 'Tool'), h('th', null, 'Status'), h('th', null, 'Used by'))),
        h('tbody', null, tools.map((tool) => h('tr', null,
          h('td', null, h('div', { class: 'title' }, tool.title), h('div', { class: 'small muted mono' }, tool.version ?? tool.installHint)),
          h('td', null, tool.found
            ? h('span', { class: 'badge tone-ok' }, icon('check'), 'Installed')
            : h('span', { class: `badge ${tool.required ? 'tone-danger' : 'tone-warn'}` }, icon('x'), tool.required ? 'Required' : 'Missing')),
          h('td', null, h('code', { class: 'muted' }, tool.usedBy))))));

  return h('section', { class: 'card' },
    h('div', { class: 'card-head' },
      h('h2', null, 'Tools'),
      h('button', { class: 'btn btn-sm btn-ghost', onClick: () => { store.set({ tools: null }); loadTools(true); } }, icon('refresh'), 'Re-check')),
    h('div', { class: 'card-body flush' }, body));
}
