'use strict';

// The only bridge between the web UI and the desktop: two fire-and-forget actions, no Node access.

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('tfstudio', {
  shell: 'electron',
  openProject: () => ipcRenderer.invoke('project:open'),
  revealProject: () => ipcRenderer.invoke('project:reveal'),
});
