import { parentPort } from 'node:worker_threads';
globalThis.self = globalThis;
globalThis.postMessage = message => parentPort.postMessage(message);
await import('../dist/telemetry-worker.js');
parentPort.on('message', data => globalThis.onmessage({ data }));
