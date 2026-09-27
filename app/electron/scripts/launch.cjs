'use strict';

// `npm start` entry point: runs Electron on this app without ELECTRON_RUN_AS_NODE.
// VS Code (and tools running in its extension host) export that variable to child processes;
// when it is set, Electron behaves like plain Node and `require('electron').app` is undefined.

delete process.env.ELECTRON_RUN_AS_NODE;
process.argv.splice(2, 0, require('node:path').join(__dirname, '..'));
require('electron/cli.js');
