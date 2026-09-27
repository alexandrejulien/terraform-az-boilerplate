// Plan review: renders plan.tfgraph (terraform show -json) as summary tiles, a filterable
// resource list with attribute diffs, outputs and drift — then applies the saved plan.

import { api } from '../api.js';
import { fill, h, icon, isProdLike, relativeTime } from '../dom.js';
import { store } from '../store.js';
import { actionBadge, dialog, notice } from '../ui.js';

const TILES = [
  ['create', 'to create'],
  ['update', 'to update in place'],
  ['replace', 'to replace'],
  ['delete', 'to destroy'],
  ['read', 'data sources to read'],
  ['no-op', 'unchanged'],
];

const ACTION_ORDER = ['replace', 'delete', 'create', 'update', 'forget', 'read', 'no-op'];

const REASONS = {
  replace_because_cannot_update: 'Replaced: an attribute that forces replacement changed.',
  replace_because_tainted: 'Replaced: the resource is tainted.',
  replace_by_request: 'Replaced: requested with -replace.',
  replace_by_triggers: 'Replaced: a replace_triggered_by reference changed.',
  delete_because_no_resource_config: 'Destroyed: the resource was removed from the configuration.',
  delete_because_no_module: 'Destroyed: its module was removed from the configuration.',
  delete_because_wrong_repetition: 'Destroyed: count/for_each was added or removed.',
  delete_because_count_index: 'Destroyed: its count index is out of range.',
  delete_because_each_key: 'Destroyed: its for_each key no longer exists.',
  delete_because_no_move_target: 'Destroyed: the moved block target does not exist.',
  read_because_config_unknown: 'Read during apply: its configuration depends on values not known yet.',
  read_because_dependency_pending: 'Read during apply: it depends on resources with pending changes.',
  read_because_check_nested: 'Read during apply: referenced by a check block.',
};

const SINGLE_SIDED = { create: 'after', read: 'after', delete: 'before' };

export function mount(root, ctx) {
  const page = h('div', { class: 'page' });
  root.append(page);

  const ui = { hidden: new Set(['no-op']), query: '', showUnchanged: false, open: new Set() };
  let report = null;
  let error = null;
  let loadedKey = null;
  let listHost = null;

  // Reload when the plan files, the workspace or the run history (e.g. an apply) change.
  const planKey = () => {
    const { project, runs } = store.state;
    return project ? JSON.stringify([project.planFiles, project.workspace, runs.find((r) => r.state !== 'running')?.id]) : null;
  };

  async function load() {
    loadedKey = planKey();
    try {
      report = await api.plan();
      error = null;
    } catch (e) {
      report = null;
      error = e;
    }
    render();
  }

  function render() {
    if (!report) {
      fill(page, error?.status === 404 || !error ? emptyState(ctx) : h('div', { class: 'stack' },
        notice('danger', error.message),
        h('div', null, h('button', { class: 'btn', onClick: load }, icon('refresh'), 'Retry'))));
      return;
    }

    const workspace = store.state.project?.workspace;
    const s = report.summary;
    listHost = h('div');

    fill(page, 
      h('div', { class: 'page-header' },
        h('div', null,
          h('h1', null, 'Plan review'),
          h('div', { class: 'subtitle' }, metaLine(report))),
        h('div', { class: 'page-actions' },
          h('button', { class: 'btn btn-ghost', onClick: load, title: 'Reload plan.tfgraph' }, icon('refresh')),
          h('button', { class: 'btn', onClick: () => ctx.startRun(['tf:plan']) }, icon('play'), 'Re-plan'),
          h('button', {
            class: `btn ${s.destroy > 0 || isProdLike(workspace) ? 'btn-danger' : 'btn-primary'}`,
            disabled: !report.canApply,
            title: report.canApply ? null : 'Nothing applicable: see the notes above',
            onClick: () => confirmApply(report, workspace, ctx),
          }, icon('check'), `Apply to ${workspace ?? '?'}`))),

      report.notices.map((n) => notice(n.level, n.message)),

      h('div', { class: 'plan-sentence' }, sentence(report)),

      h('div', { class: 'tiles' }, TILES.map(([action, label]) => h('button', {
        class: `tile act-${action}`,
        'aria-pressed': String(!ui.hidden.has(action)),
        title: 'Show / hide in the list below',
        onClick: () => { toggle(ui.hidden, action); render(); },
      }, h('span', { class: 'num' }, String(countFor(s, action))), h('span', { class: 'lbl' }, label)))),

      h('section', { class: 'card' },
        h('div', { class: 'card-head' },
          h('h2', null, 'Resources'),
          h('div', { class: 'toolbar' },
            h('div', { class: 'search' }, icon('search'), h('input', {
              class: 'input', type: 'search', placeholder: 'Filter by address or type', value: ui.query,
              onInput: (event) => { ui.query = event.target.value; renderList(); },
            })),
            h('label', { class: 'toggle' }, h('input', {
              type: 'checkbox', checked: ui.showUnchanged,
              onChange: (event) => { ui.showUnchanged = event.target.checked; renderList(); },
            }), 'Unchanged attributes'),
            h('button', { class: 'btn btn-sm btn-ghost', onClick: () => { visibleResources().forEach((r) => ui.open.add(r.address)); renderList(); } }, 'Expand all'),
            h('button', { class: 'btn btn-sm btn-ghost', onClick: () => { ui.open.clear(); renderList(); } }, 'Collapse all'))),
        h('div', { class: 'card-body flush' }, listHost)),

      report.outputs.length > 0 && outputsCard(report.outputs),
      report.drift.length > 0 && driftCard(report.drift, ui),
    );
    renderList();
  }

  function visibleResources() {
    const query = ui.query.trim().toLowerCase();
    return report.resources
      // Imports and moves are real changes even when their action is no-op: keep them visible.
      .filter((r) => !ui.hidden.has(r.action) || r.importId != null || r.previousAddress != null)
      .filter((r) => !query || r.address.toLowerCase().includes(query) || (r.type ?? '').toLowerCase().includes(query))
      .sort((a, b) => ACTION_ORDER.indexOf(a.action) - ACTION_ORDER.indexOf(b.action) || a.address.localeCompare(b.address));
  }

  function renderList() {
    const resources = visibleResources();
    fill(listHost, resources.length === 0
      ? h('div', { class: 'empty' }, report.resources.length === 0 ? 'This plan has no resource changes.' : 'No resource matches the current filters.')
      : resources.map((resource) => resourceEl(resource, ui, renderList)));
  }

  const unsubscribe = store.subscribe(() => {
    if (store.state.project && planKey() !== loadedKey) load();
  });
  if (store.state.project) load();
  else fill(page, h('div', { class: 'empty' }, h('span', { class: 'spinner' }), 'Loading…'));
  return unsubscribe;
}

function emptyState(ctx) {
  return h('div', { class: 'card' }, h('div', { class: 'empty' },
    icon('diff'),
    h('h2', null, 'No plan yet'),
    h('p', null, 'Run tf:plan to create plan.tfplan and its JSON rendering (plan.tfgraph) for review.'),
    h('div', { class: 'row' },
      h('button', { class: 'btn btn-primary', onClick: () => ctx.startRun(['tf:plan']) }, icon('play'), 'Run plan'),
      h('button', { class: 'btn', onClick: () => ctx.startRun(['tf:init', 'tf:validate', 'tf:lint', 'tf:plan']) }, 'Run full workflow'))));
}

function metaLine(report) {
  const parts = [];
  if (report.files.planModifiedAt) parts.push(`Generated ${relativeTime(report.files.planModifiedAt)}`);
  if (report.terraformVersion) parts.push(`Terraform ${report.terraformVersion}`);
  parts.push(report.originKnown ? `made for ${report.planWorkspace}` : 'workspace not verified');
  return parts.join(' · ');
}

function sentence(report) {
  const s = report.summary;
  if (!report.hasChanges) return 'No changes. Your infrastructure matches the configuration.';
  const extras = [s.import && `${s.import} to import`, s.move && `${s.move} to move`, s.forget && `${s.forget} to forget`].filter(Boolean);
  return `Plan: ${s.add} to add, ${s.change} to change, ${s.destroy} to destroy.${extras.length ? ` (${extras.join(', ')})` : ''}`;
}

function countFor(summary, action) {
  return { create: summary.create, update: summary.update, replace: summary.replace, delete: summary.delete, read: summary.read, 'no-op': summary.noOp }[action] ?? 0;
}

function toggle(set, value) {
  if (set.has(value)) set.delete(value);
  else set.add(value);
}

function providerShort(provider) {
  return provider ? provider.replace(/^registry\.terraform\.io\//, '') : '';
}

function resourceEl(resource, ui, rerender) {
  const open = ui.open.has(resource.address);
  const meta = [
    resource.type,
    providerShort(resource.providerName),
    resource.importId != null && `import ${resource.importId}`.trim(),
    resource.previousAddress && `moved from ${resource.previousAddress}`,
    resource.replaceOrder === 'create-before-destroy' && 'create before destroy',
  ].filter(Boolean).join(' · ');

  return h('div', { class: `resource${open ? ' open' : ''}` },
    h('button', {
      class: 'resource-head', 'aria-expanded': String(open),
      onClick: () => { toggle(ui.open, resource.address); rerender(); },
    },
    actionBadge(resource.action),
    h('div', null, h('div', { class: 'resource-address' }, resource.address), h('div', { class: 'resource-meta' }, meta)),
    h('span', { class: 'resource-meta' }, resource.changedAttributes ? `${resource.changedAttributes} attribute${resource.changedAttributes > 1 ? 's' : ''}` : ''),
    icon('chevron', 'chev')),
    open && h('div', { class: 'resource-body' },
      resource.actionReason && h('div', { class: 'reason' }, REASONS[resource.actionReason] ?? resource.actionReason),
      attributesTable(resource, ui.showUnchanged)));
}

function attributesTable(resource, showUnchanged) {
  const side = SINGLE_SIDED[resource.action];
  const rows = side || showUnchanged ? resource.attributes : resource.attributes.filter((a) => a.changed);
  if (rows.length === 0) {
    return h('div', { class: 'muted small' }, resource.action === 'no-op' ? 'No changes.' : 'No attribute changes (enable “Unchanged attributes” to see all).');
  }

  const head = side
    ? h('tr', null, h('th', null, 'Attribute'), h('th', null, 'Value'))
    : h('tr', null, h('th', null, 'Attribute'), h('th', null, 'Before'), h('th', null, 'After'));

  return h('table', { class: 'attrs' },
    h('colgroup', null, h('col', { class: 'path' }), h('col'), !side && h('col')),
    h('thead', null, head),
    h('tbody', null, rows.map((attr) => h('tr', { class: attr.changed ? 'changed' : 'unchanged' },
      h('td', null, attr.path, attr.forcesReplacement && h('span', { class: 'force-tag' }, 'forces replacement')),
      side
        ? h('td', null, valueEl(side === 'before' ? attr.before : attr.after, side))
        : [h('td', null, valueEl(attr.before, 'before')), h('td', null, valueEl(attr.after, 'after'))]))));
}

function valueEl(text, side) {
  if (text == null) return h('span', { class: 'v-null' }, 'null');
  if (text === '(known after apply)') return h('span', { class: 'v-unknown' }, text);
  if (text === '(sensitive value)') return h('span', { class: 'v-sensitive' }, text);
  return h('span', { class: side === 'before' ? 'v-before' : 'v-after' }, text);
}

function outputsCard(outputs) {
  return h('section', { class: 'card' },
    h('div', { class: 'card-head' }, h('h2', null, 'Outputs')),
    h('div', { class: 'card-body flush' }, h('table', { class: 'table' },
      h('thead', null, h('tr', null, h('th', null, 'Output'), h('th', null, 'Change'), h('th', null, 'Before'), h('th', null, 'After'))),
      h('tbody', null, outputs.map((o) => h('tr', null,
        h('td', null, h('code', null, o.name)),
        h('td', null, actionBadge(o.action)),
        h('td', { class: 'mono' }, valueEl(o.before, 'before')),
        h('td', { class: 'mono' }, valueEl(o.after, 'after'))))))));
}

function driftCard(drift, ui) {
  const host = h('div');
  const render = () => fill(host, drift.map((r) => resourceEl(r, ui, render)));
  render();
  return h('section', { class: 'card' },
    h('div', { class: 'card-head' },
      h('h2', null, 'Changed outside of Terraform'),
      h('span', { class: 'hint' }, 'Drift detected during refresh; the plan already accounts for it')),
    h('div', { class: 'card-body flush' }, host));
}

async function confirmApply(report, workspace, ctx) {
  const s = report.summary;
  const risky = s.destroy > 0 || isProdLike(workspace);
  const result = await dialog({
    title: `Apply this plan to ${workspace}?`,
    body: [
      h('p', { class: 'plan-sentence' }, sentence(report)),
      s.destroy > 0 && notice('danger', `${s.destroy} resource${s.destroy > 1 ? 's' : ''} will be destroyed${s.replace ? ` (${s.replace} as part of a replacement)` : ''}.`),
      h('p', null, 'Runs ', h('code', null, 'tf:apply:approve'), ': Terraform applies plan.tfplan exactly as reviewed here, without prompting again.'),
    ],
    requireText: risky ? workspace : null,
    confirmLabel: 'Apply',
    tone: risky ? 'danger' : 'primary',
  });
  if (result) ctx.startRun(['tf:apply:approve']);
}
