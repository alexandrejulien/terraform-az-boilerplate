'use strict';

// Owns the TF Studio server process: spawn, wait for readiness, stop (with its whole process tree).

const { spawn, spawnSync } = require('node:child_process');
const crypto = require('node:crypto');
const EventEmitter = require('node:events');
const fs = require('node:fs');
const path = require('node:path');

const READY_LINE = /^TFSTUDIO_READY (http:\/\/127\.0\.0\.1:\d+)\s*$/m;
const ERROR_LINE = /^TFSTUDIO_ERROR (.+)$/m;
const SERVER_EXE = process.platform === 'win32' ? 'TfStudio.Server.exe' : 'TfStudio.Server';

/**
 * Where the server comes from:
 *  - TFSTUDIO_SERVER_EXE: explicit binary (e.g. a local AOT publish),
 *  - packaged app: the AOT binary shipped in resources/server,
 *  - dev: `dotnet run` on the server project (first start includes a build, hence the long timeout).
 */
function resolveServerCommand(isPackaged) {
  if (process.env.TFSTUDIO_SERVER_EXE) {
    return { command: process.env.TFSTUDIO_SERVER_EXE, args: [], timeoutMs: 30_000 };
  }
  if (isPackaged) {
    return { command: path.join(process.resourcesPath, 'server', SERVER_EXE), args: [], timeoutMs: 30_000 };
  }
  const project = path.resolve(__dirname, '..', '..', 'server', 'TfStudio.Server.csproj');
  return { command: 'dotnet', args: ['run', '--project', project, '--no-launch-profile', '--'], timeoutMs: 180_000 };
}

class Backend extends EventEmitter {
  constructor({ projectPath, logFile, isPackaged }) {
    super();
    this.projectPath = projectPath;
    this.logFile = logFile;
    this.server = resolveServerCommand(isPackaged);
    this.token = crypto.randomBytes(32).toString('hex');
    this.url = null;
    this.child = null;
    this.exited = false;
    this.stopping = false;
    this.tail = '';
  }

  start() {
    fs.mkdirSync(path.dirname(this.logFile), { recursive: true });
    this.log = fs.createWriteStream(this.logFile, { flags: 'w' });

    // The token travels by environment variable, never argv (visible to other processes).
    const env = { ...process.env, TFSTUDIO_TOKEN: this.token };
    delete env.ELECTRON_RUN_AS_NODE;

    const { command, args, timeoutMs } = this.server;
    this.log.write(`> ${command} ${args.join(' ')} --root "${this.projectPath}"\n`);
    this.child = spawn(command, [...args, '--root', this.projectPath, '--port', '0', '--parent-pid', String(process.pid)], {
      env,
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    return new Promise((resolve, reject) => {
      let settled = false;
      let startupOutput = '';

      const fail = (error) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        this.stop().finally(() => reject(error));
      };
      const succeed = () => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        resolve();
      };
      const timer = setTimeout(() => fail(new Error(`The server did not start within ${timeoutMs / 1000}s.`)), timeoutMs);

      const onData = (chunk) => {
        const text = chunk.toString();
        this.log.write(text);
        this.tail = (this.tail + text).slice(-6000);
        if (settled) return;
        startupOutput += text;
        const error = ERROR_LINE.exec(startupOutput);
        if (error) return fail(new Error(error[1]));
        const ready = READY_LINE.exec(startupOutput);
        if (ready && !this.url) {
          this.url = ready[1];
          this.healthCheck().then(succeed, fail);
        }
      };

      this.child.stdout.on('data', onData);
      this.child.stderr.on('data', onData);
      this.child.on('error', (error) => fail(new Error(`Could not start ${command}: ${error.message}`)));
      this.child.on('exit', (code) => {
        this.exited = true;
        this.log.end();
        if (!settled) fail(new Error(`The server exited (code ${code}) before it was ready.`));
        else if (!this.stopping) this.emit('exit', code);
      });
    });
  }

  async healthCheck() {
    for (let attempt = 0; attempt < 20; attempt++) {
      try {
        const response = await fetch(`${this.url}/api/health`, { headers: { 'X-TfStudio-Token': this.token } });
        if (response.ok) return;
      } catch {
        // not accepting connections yet
      }
      await new Promise((r) => setTimeout(r, 150));
    }
    throw new Error(`The server at ${this.url} did not answer its health check.`);
  }

  /** Kills the server and everything it started (task, terraform, providers). */
  async stop() {
    if (!this.child || this.exited) return;
    this.stopping = true;
    const exited = new Promise((resolve) => this.child.once('exit', resolve));
    if (process.platform === 'win32') {
      spawnSync('taskkill', ['/pid', String(this.child.pid), '/T', '/F'], { windowsHide: true });
    } else {
      this.child.kill('SIGTERM');
    }
    await Promise.race([exited, new Promise((r) => setTimeout(r, 3000))]);
  }

  logTail(lines = 15) {
    return this.tail.trimEnd().split(/\r?\n/).slice(-lines).join('\n');
  }
}

module.exports = { Backend };
