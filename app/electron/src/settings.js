'use strict';

// Tiny persisted settings (last opened project) in the per-user app data folder.

const { app } = require('electron');
const fs = require('node:fs');
const path = require('node:path');

const file = () => path.join(app.getPath('userData'), 'settings.json');

function load() {
  try {
    return JSON.parse(fs.readFileSync(file(), 'utf8'));
  } catch {
    return {};
  }
}

function save(settings) {
  try {
    fs.mkdirSync(path.dirname(file()), { recursive: true });
    fs.writeFileSync(file(), JSON.stringify(settings, null, 2));
  } catch (error) {
    console.error('Could not save settings:', error);
  }
}

module.exports = { load, save };
