import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
const directory = path.resolve('dist');
const port = Number(process.argv[2] ?? 4173);
if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('Informe uma porta valida entre 0 e 65535.');
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml' };
const allowed = new Set(['index.html', 'app.js', 'client.js', 'admin.js', 'analysis-ui.js', 'telemetry.js', 'telemetry-csv.js', 'telemetry-worker.js', 'config.js', 'styles.css', 'analysis.css', 'icon.svg']);
const server = createServer(async (req, res) => {
  const name = new URL(req.url, 'http://localhost').pathname.slice(1) || 'index.html';
  if (!allowed.has(name)) { res.writeHead(404); res.end('Not found'); return; }
  try {
    const data = await readFile(path.join(directory, name));
    res.writeHead(200, { 'Content-Type': types[path.extname(name)], 'Cache-Control': 'no-store' }); res.end(data);
  } catch { res.writeHead(500); res.end('Unable to read asset'); }
}).listen(port, '127.0.0.1', () => process.stdout.write(`Local: http://127.0.0.1:${server.address().port}\n`));
