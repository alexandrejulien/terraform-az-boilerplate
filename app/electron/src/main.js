'use strict';

// Electron shell for TF Studio: starts the .NET server for a Terraform project folder and shows its
// web UI. All Terraform logic lives in the server; this file only manages windows and the process.
//
// CLI: electron . [--project=<folder>] [--smoke-test[=<screenshot folder>]]

const { app, BrowserWindow, Menu, dialog, ipcMain, nativeTheme, session, shell } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const { Backend } = require('./backend');
const settings = require('./settings');

const argValue = (name) => {
  const arg = process.argv.find((a) => a === `--${name}` || a.startsWith(`--${name}=`));
  if (!arg) return undefined;
  return arg.includes('=') ? arg.slice(arg.indexOf('=') + 1) : '';
};

const smokeTest = argValue('smoke-test');
const logFile = () => path.join(app.getPath('logs'), 'server.log');

let mainWindow = null;
let backend = null;
let quitting = false;

function isProjectRoot(folder) {
  return Boolean(folder)
    && fs.existsSync(path.join(folder, 'Taskfile.yml'))
    && fs.existsSync(path.join(folder, 'environments'));
}

// ---- Startup ------------------------------------------------------------------------------

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (!mainWindow) return;
    if (mainWindow.isMinimized()) mainWindow.restore();
    mainWindow.focus();
  });
  app.whenReady().then(start);
}

async function start() {
  hardenWebContents();
  registerIpc();
  Menu.setApplicationMenu(buildMenu());
  mainWindow = createWindow();

  const project = await resolveInitialProject();
  if (!project) {
    app.quit();
    return;
  }
  await launch(project);
}

function createWindow() {
  const window = new BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 960,
    minHeight: 620,
    show: false,
    title: 'TF Studio',
    backgroundColor: nativeTheme.shouldUseDarkColors ? '#0e1116' : '#f6f7f9',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false,
    },
  });
  // Keep "TF Studio — <project>" instead of the page's <title>.
  window.on('page-title-updated', (event) => event.preventDefault());
  if (smokeTest === undefined) window.once('ready-to-show', () => window.show());
  window.on('closed', () => { mainWindow = null; });
  return window;
}

/** --project=… wins, then the last opened project, then (dev) this repository, else ask. */
async function resolveInitialProject() {
  const candidates = [
    argValue('project'),
    settings.load().projectPath,
    app.isPackaged ? null : path.resolve(__dirname, '..', '..', '..'),
  ];
  const found = candidates.find((candidate) => candidate && isProjectRoot(path.resolve(candidate)));
  if (found) return path.resolve(found);
  if (smokeTest !== undefined) return null;
  mainWindow.show();
  return pickProjectFolder();
}

async function pickProjectFolder() {
  for (;;) {
    const result = await dialog.showOpenDialog(mainWindow, {
      title: 'Open a Terraform boilerplate project',
      buttonLabel: 'Open project',
      properties: ['openDirectory'],
    });
    if (result.canceled || result.filePaths.length === 0) return null;
    const folder = result.filePaths[0];
    if (isProjectRoot(folder)) return folder;
    await dialog.showMessageBox(mainWindow, {
      type: 'warning',
      message: 'This folder is not a Terraform boilerplate project.',
      detail: 'Pick the folder that contains Taskfile.yml and environments/.',
    });
  }
}

async function launch(projectPath) {
  if (backend) {
    const previous = backend;
    backend = null;
    await previous.stop();
  }

  await showLoading(`Starting the server for ${projectPath}`);
  const next = new Backend({ projectPath, logFile: logFile(), isPackaged: app.isPackaged });
  next.on('exit', (code) => onBackendExit(next, code));
  backend = next;

  try {
    await next.start();
  } catch (error) {
    if (backend === next) await onStartFailure(next, error);
    return;
  }

  settings.save({ ...settings.load(), projectPath });
  mainWindow.setTitle(`TF Studio — ${path.basename(projectPath)}`);
  await mainWindow.loadURL(`${next.url}/#token=${next.token}`);
  if (smokeTest !== undefined) runSmokeTest(next);
}

function showLoading(message) {
  return mainWindow.loadFile(path.join(__dirname, 'loading.html'), { query: { message } });
}

async function onStartFailure(failed, error) {
  if (smokeTest !== undefined) {
    console.error(`SMOKE_FAIL ${error.message}\n${failed.logTail()}`);
    app.exit(1);
    return;
  }
  mainWindow.show();
  const { response } = await dialog.showMessageBox(mainWindow, {
    type: 'error',
    message: 'TF Studio could not start its server.',
    detail: `${error.message}\n\n${failed.logTail()}`,
    buttons: ['Retry', 'Open another project…', 'Open log', 'Quit'],
    defaultId: 0,
    cancelId: 3,
  });
  if (response === 0) return launch(failed.projectPath);
  if (response === 1) {
    const folder = await pickProjectFolder();
    return folder ? launch(folder) : app.quit();
  }
  if (response === 2) shell.openPath(logFile());
  app.quit();
}

async function onBackendExit(exited, code) {
  if (quitting || backend !== exited || !mainWindow) return;
  const { response } = await dialog.showMessageBox(mainWindow, {
    type: 'error',
    message: `The TF Studio server stopped unexpectedly (exit code ${code}).`,
    detail: exited.logTail(),
    buttons: ['Restart', 'Open log', 'Quit'],
    defaultId: 0,
    cancelId: 2,
  });
  if (response === 0) return launch(exited.projectPath);
  if (response === 1) shell.openPath(logFile());
  app.quit();
}

// ---- Security -----------------------------------------------------------------------------

function isAppUrl(url) {
  return url.startsWith('file://') || Boolean(backend?.url && (url === backend.url || url.startsWith(`${backend.url}/`)));
}

function hardenWebContents() {
  session.defaultSession.setPermissionRequestHandler((_contents, permission, callback) => {
    callback(permission === 'clipboard-sanitized-write');
  });
  app.on('web-contents-created', (_event, contents) => {
    contents.on('will-navigate', (event, url) => {
      if (!isAppUrl(url)) event.preventDefault();
    });
    contents.on('will-attach-webview', (event) => event.preventDefault());
    contents.setWindowOpenHandler(({ url }) => {
      if (url.startsWith('https://')) shell.openExternal(url);
      return { action: 'deny' };
    });
  });
}

function registerIpc() {
  // Only the page served by our own backend may call these.
  const trusted = (event) => Boolean(backend?.url) && new URL(event.senderFrame.url).origin === new URL(backend.url).origin;

  ipcMain.handle('project:open', async (event) => {
    if (!trusted(event)) return { ok: false };
    const folder = await pickProjectFolder();
    if (!folder) return { ok: false };
    await launch(folder);
    return { ok: true };
  });

  ipcMain.handle('project:reveal', (event) => {
    if (trusted(event) && backend) shell.openPath(backend.projectPath);
  });
}

// ---- Menu ---------------------------------------------------------------------------------

function buildMenu() {
  return Menu.buildFromTemplate([
    {
      label: 'File',
      submenu: [
        {
          label: 'Open Terraform Project…',
          accelerator: 'CmdOrCtrl+O',
          click: async () => {
            const folder = await pickProjectFolder();
            if (folder) await launch(folder);
          },
        },
        { label: 'Show Project in Explorer', click: () => backend && shell.openPath(backend.projectPath) },
        { type: 'separator' },
        { label: 'Open Server Log', click: () => shell.openPath(logFile()) },
        { type: 'separator' },
        { role: 'quit' },
      ],
    },
    {
      label: 'View',
      submenu: [
        { role: 'reload' },
        { role: 'forceReload' },
        { role: 'toggleDevTools' },
        { type: 'separator' },
        { role: 'resetZoom' },
        { role: 'zoomIn' },
        { role: 'zoomOut' },
        { type: 'separator' },
        { role: 'togglefullscreen' },
      ],
    },
    {
      label: 'Help',
      submenu: [{
        label: 'About TF Studio',
        click: () => dialog.showMessageBox(mainWindow, {
          type: 'info',
          message: `TF Studio ${app.getVersion()}`,
          detail: `Electron ${process.versions.electron} · Chromium ${process.versions.chrome}\nProject: ${backend?.projectPath ?? '—'}\nServer: ${backend?.url ?? '—'}`,
        }),
      }],
    },
  ]);
}

// ---- Shutdown -----------------------------------------------------------------------------

app.on('window-all-closed', () => app.quit());

app.on('before-quit', (event) => {
  if (quitting || !backend) return;
  event.preventDefault();
  quitting = true;
  backend.stop().finally(() => app.quit());
});

// ---- Smoke test ---------------------------------------------------------------------------

/** Loads every view in a hidden window, saves screenshots, exits 0 (SMOKE_OK) or 1 (SMOKE_FAIL). */
async function runSmokeTest(active) {
  const outDir = smokeTest || path.join(app.getPath('userData'), 'smoke');
  const wc = mainWindow.webContents;
  const errors = [];
  wc.on('console-message', (details) => {
    if (details.level === 'error') errors.push(details.message);
  });
  const waitFor = (selector, timeoutMs = 15000) => wc.executeJavaScript(`new Promise((resolve, reject) => {
    const started = Date.now();
    const timer = setInterval(() => {
      if (document.querySelector(${JSON.stringify(selector)})) { clearInterval(timer); resolve(true); }
      else if (Date.now() - started > ${timeoutMs}) { clearInterval(timer); reject(new Error('timeout waiting for ${selector}')); }
    }, 100);
  })`);

  try {
    fs.mkdirSync(outDir, { recursive: true });
    await waitFor('.project-name');
    for (const route of ['overview', 'plan', 'runs', 'tasks']) {
      await wc.executeJavaScript(`location.hash = '#/${route}'`);
      await new Promise((r) => setTimeout(r, 2500));
      const image = await wc.capturePage();
      fs.writeFileSync(path.join(outDir, `${route}.png`), image.toPNG());
    }
    if (errors.length) throw new Error(`console errors:\n${errors.join('\n')}`);
    console.log(`SMOKE_OK ${active.url} screenshots in ${outDir}`);
    quitting = true;
    await active.stop();
    app.exit(0);
  } catch (error) {
    console.error(`SMOKE_FAIL ${error.message}`);
    quitting = true;
    await active.stop();
    app.exit(1);
  }
}
