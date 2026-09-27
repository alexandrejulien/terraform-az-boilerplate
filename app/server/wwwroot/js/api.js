// HTTP client for the TF Studio server. Every /api call carries the per-launch token.
// The token arrives once in the URL fragment (#token=…, never sent to the server or logged),
// then lives in sessionStorage so reloads keep working.

const TOKEN_KEY = 'tfstudio.token';
const TOKEN_HEADER = 'X-TfStudio-Token';

function takeToken() {
  const match = /(?:^#|&)token=([^&]+)/.exec(location.hash);
  if (match) {
    const value = decodeURIComponent(match[1]);
    try { sessionStorage.setItem(TOKEN_KEY, value); } catch { /* storage disabled: keep it in memory */ }
    history.replaceState(null, '', `${location.pathname}#/overview`);
    return value;
  }
  try { return sessionStorage.getItem(TOKEN_KEY); } catch { return null; }
}

const token = takeToken();

export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.status = status;
  }
}

async function request(method, path, body) {
  const headers = { [TOKEN_HEADER]: token ?? '' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  let response;
  try {
    response = await fetch(`/api${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    throw new ApiError('The TF Studio server is not reachable.', 0);
  }

  const text = await response.text();
  let data = null;
  if (text) {
    try { data = JSON.parse(text); } catch { data = null; }
  }
  if (!response.ok) {
    const fallback = response.status === 401
      ? 'Not authorized. Open TF Studio from the desktop app, or use the URL printed by the server.'
      : `Request failed (HTTP ${response.status}).`;
    throw new ApiError(data?.error ?? fallback, response.status);
  }
  return data;
}

const enc = encodeURIComponent;

export const api = {
  hasToken: () => Boolean(token),
  health: () => request('GET', '/health'),
  project: () => request('GET', '/project'),
  environment: (name) => request('GET', `/environments/${enc(name)}`),
  tasks: () => request('GET', '/tasks'),
  tools: (refresh = false) => request('GET', `/tools${refresh ? '?refresh=true' : ''}`),
  runs: () => request('GET', '/runs'),
  run: (id) => request('GET', `/runs/${enc(id)}`),
  startRun: (tasks, vars = {}) => request('POST', '/runs', { tasks, vars }),
  sendInput: (id, text) => request('POST', `/runs/${enc(id)}/input`, { text }),
  cancelRun: (id) => request('POST', `/runs/${enc(id)}/cancel`),
  plan: () => request('GET', '/plan'),
};

/**
 * Follows a run's Server-Sent Events. EventSource can't send custom headers, so this reads the
 * text/event-stream body with fetch. Resolves when the server ends the stream (run finished).
 */
export async function streamRunEvents(id, afterSeq, onEvent, signal) {
  const response = await fetch(`/api/runs/${enc(id)}/events?after=${afterSeq}`, {
    headers: { [TOKEN_HEADER]: token ?? '', Accept: 'text/event-stream' },
    signal,
  });
  if (!response.ok) throw new ApiError(`Could not follow run ${id} (HTTP ${response.status}).`, response.status);

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  for (;;) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += value;
    let boundary;
    while ((boundary = buffer.indexOf('\n\n')) !== -1) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      let type = 'message';
      let data = '';
      for (const line of frame.split('\n')) {
        if (line.startsWith('event:')) type = line.slice(6).trim();
        else if (line.startsWith('data:')) data += (data ? '\n' : '') + line.slice(5).replace(/^ /, '');
      }
      if (data) onEvent(type, JSON.parse(data));
    }
  }
}
