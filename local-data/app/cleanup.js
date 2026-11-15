'use strict';
// Runs as a hidden Node process, not a Chromium instance. Outlives crashes/game shutdown.
const fs = require('fs');
const path = require('path');
const os = require('os');
const dir = process.argv[2];
const pid = Number(process.argv[3]);
const root = path.join(os.tmpdir(), 'TheInternet-BAS');
if (!dir || !Number.isSafeInteger(pid) || pid <= 0 ||
    path.dirname(path.resolve(dir)) !== path.resolve(root) ||
    !new RegExp('^run-' + pid + '-\\d+$').test(path.basename(dir))) process.exit(1);

let attempts = 0;
const timer = setInterval(() => {
  try { process.kill(pid, 0); return; }
  catch (e) { if (e.code !== 'ESRCH') return; }
  try { fs.rmSync(dir, { recursive: true, force: true }); clearInterval(timer); }
  catch (e) { if (++attempts >= 60) { clearInterval(timer); process.exitCode = 1; } }
}, 500);
