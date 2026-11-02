import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

test('servidor entrega pagina, CSS e modulos novos sem expor arquivos do projeto', async t => {
  const process = spawn(globalThis.process.execPath, ['--preserve-symlinks-main','--preserve-symlinks',fileURLToPath(new URL('../serve.mjs', import.meta.url)),'0'], { cwd: fileURLToPath(new URL('..', import.meta.url)), windowsHide: true, stdio: ['ignore','pipe','pipe'] });
  t.after(() => process.kill());
  const origin = await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Local server timeout')), 10000);
    process.once('error', error => { clearTimeout(timer); reject(error); });
    process.once('exit', code => { clearTimeout(timer); reject(new Error(`Local server exited ${code}`)); });
    process.stdout.on('data', data => { const found = data.toString().match(/Local: (http:\/\/127\.0\.0\.1:\d+)/); if (found) { clearTimeout(timer); resolve(found[1]); } });
  });
  for (const path of ['/','/app.js','/admin.js','/analysis-ui.js','/telemetry.js','/telemetry-csv.js','/telemetry-worker.js','/analysis.css']) {
    const result = await fetch(origin + path); assert.equal(result.status, 200, path); assert.equal(result.headers.get('cache-control'), 'no-store');
  }
  assert.match((await fetch(origin + '/analysis-ui.js')).headers.get('content-type'), /javascript/);
  assert.match((await fetch(origin + '/analysis.css')).headers.get('content-type'), /text\/css/);
  for (const path of ['/.git/config','/.openai/hosting.json','/../README.md','/Supabase/005_participant_telemetry_analysis.sql']) assert.equal((await fetch(origin + path)).status, 404, path);
});
