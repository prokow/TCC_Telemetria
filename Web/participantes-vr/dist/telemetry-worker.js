import { parseLocalSession } from './telemetry.js';
import { parseStoredCsv } from './telemetry-csv.js';
self.onmessage = async ({ data }) => {
  try {
    if (data.kind === 'csv') {
      if (data.file.size > 64 * 1024 * 1024) throw new Error('Arquivo CSV maior que 64 MB.');
      self.postMessage({ preview: parseStoredCsv(await data.file.text(), data.participantCode, data.sessionId, data.fileType) });
      return;
    }
    if (data.file.size > 32 * 1024 * 1024) throw new Error('Selecione um Session.json de até 32 MB.');
    const report = parseLocalSession(JSON.parse(await data.file.text()), data.participantCode);
    self.postMessage({ report });
  } catch (error) {
    self.postMessage({ error: error.code ? error.message : 'Não foi possível ler o arquivo. Use um Session.json válido de até 32 MB gerado pelo Unity.' });
  }
};
