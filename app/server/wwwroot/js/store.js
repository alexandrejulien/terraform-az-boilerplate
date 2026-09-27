// Shared app state + polling. Views subscribe and re-render from `store.state`.

import { api } from './api.js';

const listeners = new Set();

export const store = {
  state: {
    project: null,
    runs: [],
    tools: null,
    tasks: null,
    connected: true,
    error: null,
  },
  set(patch) {
    Object.assign(this.state, patch);
    for (const listener of listeners) listener(this.state);
  },
  subscribe(listener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
};

export const activeRun = () => store.state.runs.find((run) => run.state === 'running') ?? null;

export async function refreshProject() {
  try {
    store.set({ project: await api.project(), connected: true, error: null });
  } catch (error) {
    store.set({ connected: error.status !== 0, error: error.message });
  }
}

export async function refreshRuns() {
  try {
    store.set({ runs: await api.runs(), connected: true });
  } catch (error) {
    store.set({ connected: error.status !== 0, error: error.message });
  }
}

export async function loadTools(refresh = false) {
  try {
    store.set({ tools: await api.tools(refresh) });
  } catch (error) {
    store.set({ tools: { error: error.message } });
  }
}

export async function loadTasks() {
  try {
    store.set({ tasks: await api.tasks() });
  } catch (error) {
    store.set({ tasks: { error: error.message } });
  }
}

/** Latest outcome of a task across all runs (newest first), e.g. to badge pipeline steps. */
export function lastStepOf(taskName) {
  for (const run of store.state.runs) {
    const step = run.steps.find((s) => s.task === taskName && s.state !== 'pending' && s.state !== 'skipped');
    if (step) return { ...step, runId: run.id };
  }
  return null;
}

// Poll fast while something runs, slowly otherwise; refresh the project (workspace, plan files,
// backend) whenever a run finishes and periodically to catch edits made outside TF Studio.
let wasRunning = false;
let ticks = 0;

export async function startPolling() {
  await Promise.all([refreshProject(), refreshRuns()]);
  const tick = async () => {
    await refreshRuns();
    const running = Boolean(activeRun());
    ticks++;
    if ((wasRunning && !running) || (!running && ticks % 3 === 0)) await refreshProject();
    wasRunning = running;
    setTimeout(tick, running ? 1000 : 4000);
  };
  setTimeout(tick, 1000);
  window.addEventListener('focus', () => { refreshProject(); refreshRuns(); });
}
