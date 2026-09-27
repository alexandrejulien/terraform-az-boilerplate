// Shared UI pieces: toasts, modal dialogs, badges.

import { h, icon } from './dom.js';

export function toast(message, { tone = 'info', action = null, timeout = 6000 } = {}) {
  const host = document.getElementById('toasts');
  const el = h('div', { class: `toast ${tone}`, role: tone === 'error' ? 'alert' : 'status' },
    h('div', { class: 'msg' }, message),
    action && h('button', { class: 'btn btn-sm', onClick: () => { action.onClick(); el.remove(); } }, action.label),
    h('button', { class: 'btn btn-ghost btn-sm btn-icon', 'aria-label': 'Dismiss', onClick: () => el.remove() }, icon('x')));
  host.append(el);
  if (timeout) setTimeout(() => el.remove(), timeout);
}

/**
 * Modal dialog. Resolves with `{ values }` on confirm (values = named field inputs) or null.
 * `requireText` makes the user type an exact string (e.g. the workspace name) to enable confirm.
 */
export function dialog({
  title, body = [], fields = [], requireText = null, confirmLabel = 'Confirm', tone = 'primary', cancelLabel = 'Cancel', wide = false,
}) {
  return new Promise((resolve) => {
    const inputs = fields.map((field) => h('input', {
      class: 'input mono', name: field.name, placeholder: field.placeholder ?? '', value: field.value ?? '',
      autocomplete: 'off', spellcheck: 'false', required: field.required !== false,
    }));
    const guard = requireText && h('input', { class: 'input mono', autocomplete: 'off', spellcheck: 'false', placeholder: requireText });
    const confirm = confirmLabel && h('button', { class: `btn ${tone === 'danger' ? 'btn-danger' : 'btn-primary'}`, type: 'submit' }, confirmLabel);

    const updateState = () => {
      if (!confirm) return;
      const guardOk = !guard || guard.value === requireText;
      const fieldsOk = inputs.every((input) => !input.required || input.value.trim() !== '');
      confirm.disabled = !(guardOk && fieldsOk);
    };

    const form = h('form', { method: 'dialog' },
      h('div', { class: 'modal-head' }, h('h2', null, title)),
      h('div', { class: 'modal-body' },
        body,
        fields.map((field, i) => h('div', { class: 'field' },
          h('label', null, field.label ?? field.name), inputs[i], field.hint && h('div', { class: 'hint' }, field.hint))),
        guard && h('div', { class: 'field' },
          h('label', null, 'Type ', h('code', null, requireText), ' to confirm'), guard)),
      h('div', { class: 'modal-foot' },
        h('button', { class: 'btn', type: 'button', value: 'cancel', onClick: () => close(null) }, cancelLabel),
        confirm));

    const el = h('dialog', { class: `modal${wide ? ' wide' : ''}` }, form);
    const close = (result) => { el.close(); el.remove(); resolve(result); };

    form.addEventListener('input', updateState);
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      if (confirm?.disabled) return;
      close({ values: Object.fromEntries(inputs.map((input) => [input.name, input.value.trim()])) });
    });
    el.addEventListener('cancel', (event) => { event.preventDefault(); close(null); });

    document.body.append(el);
    updateState();
    el.showModal();
    (inputs[0] ?? guard ?? confirm)?.focus();
  });
}

const STATE_LABELS = {
  pending: 'Pending', running: 'Running', succeeded: 'Succeeded', failed: 'Failed', cancelled: 'Cancelled', skipped: 'Skipped',
};
const STATE_ICONS = { succeeded: 'check', failed: 'x', cancelled: 'stop' };

export function stateBadge(state) {
  return h('span', { class: `badge state-${state}` },
    state === 'running' ? h('span', { class: 'spinner' }) : STATE_ICONS[state] && icon(STATE_ICONS[state]),
    STATE_LABELS[state] ?? state);
}

const ACTION_LABELS = {
  create: '+ create', update: '~ update', delete: '− destroy', replace: '± replace', read: '≤ read', 'no-op': 'no change', forget: 'forget',
};

export function actionBadge(action) {
  return h('span', { class: `badge act-${action}` }, ACTION_LABELS[action] ?? action);
}

export function notice(level, message) {
  return h('div', { class: `notice ${level}` }, icon(level === 'info' ? 'info' : 'alert'), h('div', null, message));
}
