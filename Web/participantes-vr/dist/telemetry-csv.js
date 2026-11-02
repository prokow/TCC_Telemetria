import { AppError } from './client.js';
import { identifier } from './telemetry.js';
const bad = () => new AppError('csv', 'O CSV não corresponde ao formato ou aos UUIDs desta sessão.');
function* rows(text) {
  let row = [], cell = '', quoted = false, closed = false;
  for (let index = text.charCodeAt(0) === 0xfeff ? 1 : 0; index < text.length; index++) {
    const char = text[index];
    if (quoted) {
      if (char === '"') { if (text[index + 1] === '"') { cell += '"'; index++; } else { quoted = false; closed = true; } }
      else cell += char;
    } else if (char === ',' || char === '\n' || char === '\r') {
      row.push(cell); cell = ''; closed = false;
      if (row.length > 64) throw bad();
      if (char !== ',') { if (char === '\r' && text[index + 1] === '\n') index++; yield row; row = []; }
    } else if (char === '"' && !cell && !closed) quoted = true;
    else { if (closed || char === '"') throw bad(); cell += char; }
    if (cell.length > 100000) throw bad();
  }
  if (quoted) throw bad();
  if (cell || row.length || closed) { row.push(cell); yield row; }
}
export function parseStoredCsv(text, participantCode, sessionId, fileType) {
  identifier(participantCode); identifier(sessionId);
  const suffix = {
    head_csv: 'HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ',
    controllers_csv: 'IsLeftTracked,LeftPosX,LeftPosY,LeftPosZ,LeftRotX,LeftRotY,LeftRotZ,LeftStickX,LeftStickY,IsRightTracked,RightPosX,RightPosY,RightPosZ,RightRotX,RightRotY,RightRotZ,RightStickX,RightStickY',
    events_csv: 'EventType,ObjectId,Details',
  }[fileType];
  if (!suffix || typeof text !== 'string') throw bad();
  const iterator = rows(text), header = iterator.next().value;
  if (!header || header.join(',') !== 'ParticipantCode,SessionId,TimeSinceStart,' + suffix) throw bad();
  const preview = []; let total = 0;
  for (const row of iterator) {
    if (row.length !== header.length || row[0] !== participantCode || row[1] !== sessionId || !row[2] || !Number.isFinite(Number(row[2])) || Number(row[2]) < 0) throw bad();
    if (fileType !== 'events_csv' && row.slice(3).some(value => value === '' || !Number.isFinite(Number(value)))) throw bad();
    if (fileType === 'controllers_csv' && (!['0','1'].includes(row[3]) || !['0','1'].includes(row[12]))) throw bad();
    if (total < 200) preview.push(row);
    total++;
  }
  return { headers: header, rows: preview, totalRows: total };
}
