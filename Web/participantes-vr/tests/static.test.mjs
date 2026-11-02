import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';

const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
const app = await readFile(new URL('../dist/app.js', import.meta.url), 'utf8');
const analysis = await readFile(new URL('../dist/analysis-ui.js', import.meta.url), 'utf8');
const admin = await readFile(new URL('../dist/admin.js', import.meta.url), 'utf8');
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);

test('todos os controles usados pelo aplicativo existem e possuem identidade única', () => {
  assert.equal(new Set(ids).size, ids.length, 'IDs HTML únicos');
  for (const match of (app + analysis + admin).matchAll(/\$\('([^']+)'\)/g)) assert(ids.includes(match[1]), `Controle ausente: ${match[1]}`);
  for (const match of html.matchAll(/\bfor="([^"]+)"/g)) assert(ids.includes(match[1]), `Label sem campo: ${match[1]}`);
});

test('arquivos referenciados estão disponíveis para hospedagem estática', async () => {
  for (const match of html.matchAll(/(?:href|src)="\.\/([^"#]+)"/g)) await access(new URL(`../dist/${match[1]}`, import.meta.url));
  await access(new URL('../dist/config.js', import.meta.url));
  assert.match(html, /<html lang="pt-BR">/);
  assert.match(html, /type="module"/);
});

test('nomes são exibidos como texto e credenciais não são persistidas pelo app', () => {
  assert(!app.includes('innerHTML'));
  assert(!analysis.includes('innerHTML'));
  assert(!admin.includes('innerHTML'));
  assert(!/localStorage|sessionStorage|document\.cookie/.test(analysis));
  assert(!/localStorage|sessionStorage|document\.cookie/.test(app));
  assert.match(html, /id="password"[^>]+type="password"/);
  assert.match(app, /textContent/);
});

test('modulos de analise e worker estao permitidos no servidor local', async () => {
  const server = await readFile(new URL('../serve.mjs', import.meta.url), 'utf8');
  for (const file of ['admin.js','analysis-ui.js','telemetry.js','telemetry-csv.js','telemetry-worker.js','analysis.css']) {
    await access(new URL(`../dist/${file}`, import.meta.url)); assert(server.includes(`'${file}'`));
  }
});
