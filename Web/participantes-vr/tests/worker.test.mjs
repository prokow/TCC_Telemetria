import test from 'node:test';
import assert from 'node:assert/strict';
import { Worker } from 'node:worker_threads';

const id = n => n.toString(16).padStart(32, '0');
const payload = { SchemaVersion: 1, SessionId: id(2), ParticipantCode: id(1), StartedUtc: '2026-10-31T12:00:00Z', EndedUtc: '2026-10-31T12:01:00Z', DurationSeconds: 60, Attempts: 0, CorrectAnswers: 0, Errors: 0, ApplicationVersion: '1', CoordinateSpace: 'World', Events: [], HeadFrames: [], ControllerFrames: [] };
async function analyze(file, participantCode) {
  const worker = new Worker(new URL('./worker-harness.mjs', import.meta.url));
  try {
    const result = new Promise((resolve, reject) => { worker.once('message', resolve); worker.once('error', reject); });
    worker.postMessage({ file, participantCode });
    return await result;
  } finally { await worker.terminate(); }
}

test('worker le Blob em outra thread e retorna apenas relatorio compacto', async () => {
  const result = await analyze(new Blob([JSON.stringify(payload)], { type: 'application/json' }), id(1));
  assert.equal(result.report.session.session_id, id(2)); assert.equal(result.report.session.source, 'local');
  assert.equal(result.report.HeadFrames, undefined); assert.equal(result.report.ControllerFrames, undefined);
});
test('worker rejeita arquivo de outro participante, JSON invalido e tamanho excessivo', async () => {
  const mismatch = await analyze(new Blob([JSON.stringify(payload)]), id(99)); assert.match(mismatch.error, /outro participante/);
  const invalid = await analyze(new Blob(['not-json-private-fixture']), id(1)); assert.match(invalid.error, /Session.json válido/); assert(!invalid.error.includes('private-fixture'));
  const large = await analyze({ size: 65 * 1024 * 1024 }, id(1)); assert.match(large.error, /64 MB/);
});
