import { AppError } from './client.js';

const guid = /^[a-f\d]{32}$/iu;
const invalid = () => new AppError('telemetry-response', 'A telemetria recebida tem um formato inválido. Nenhum resultado parcial será apresentado.');
export function identifier(value) { if (typeof value !== 'string' || !guid.test(value)) throw invalid(); return value; }
const number = value => { if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) throw invalid(); return value; };
const count = value => { number(value); if (!Number.isSafeInteger(value)) throw invalid(); return value; };
const text = value => { if (value === null || value === undefined) return ''; if (typeof value !== 'string') throw invalid(); return value; };
const date = (value, optional = false) => {
  if (optional && (value === null || value === '')) return null;
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:?\d{2})$/u.test(value) || !Number.isFinite(Date.parse(value))) throw invalid();
  return value;
};
const array = value => { if (!Array.isArray(value)) throw invalid(); return value; };
const key = value => { if (typeof value !== 'string' || !value.trim()) throw invalid(); return value; };
const databaseId = value => { if ((typeof value !== 'string' && typeof value !== 'number') || (typeof value === 'number' && !Number.isSafeInteger(value)) || !/^\d+$/u.test(String(value))) throw invalid(); return String(value); };

export function parseSessions(data, participantCode) {
  identifier(participantCode);
  return array(data).map(row => {
    if (!row || row.participant_code !== participantCode) throw invalid();
    const result = {
      session_id: identifier(row.session_id), participant_code: identifier(row.participant_code),
      started_utc: date(row.started_utc), ended_utc: date(row.ended_utc, true), duration_seconds: number(row.duration_seconds),
      attempts: count(row.attempts), correct_answers: count(row.correct_answers), errors: count(row.errors),
      application_version: text(row.application_version), schema_version: count(row.schema_version), coordinate_space: text(row.coordinate_space), source: 'supabase', sample_counts: row.sample_counts ?? null,
    };
    if (result.ended_utc && Date.parse(result.ended_utc) < Date.parse(result.started_utc)) throw invalid();
    return result;
  });
}

export function parseEvents(data, sessionId) {
  identifier(sessionId);
  return array(data).map(row => {
    if (!row || row.session_id !== sessionId) throw invalid();
    return { id: databaseId(row.id), session_id: sessionId, time_since_start: number(row.time_since_start), event_type: key(row.event_type), object_id: text(row.object_id), details: text(row.details), task_key: text(row.task_key) };
  });
}
export function parseFiles(data, sessionId) {
  identifier(sessionId);
  return array(data).map(row => {
    if (!row || row.session_id !== sessionId) throw invalid();
    const sha256 = row.sha256 ?? null;
    if (sha256 !== null && (typeof sha256 !== 'string' || !/^[a-f\d]{64}$/u.test(sha256))) throw invalid();
    return { id: databaseId(row.id), session_id: sessionId, file_type: key(row.file_type), bucket_id: key(row.bucket_id), storage_path: key(row.storage_path), byte_size: row.byte_size === null ? null : count(row.byte_size), uploaded_at: date(row.uploaded_at), sha256, content_type: row.content_type ?? null };
  });
}

export function accuracy(attempts, correct) {
  return attempts > 0 && correct <= attempts ? correct / attempts * 100 : null;
}
export function summarize(sessions) {
  const totals = sessions.reduce((sum, row) => ({ duration: sum.duration + row.duration_seconds, attempts: sum.attempts + row.attempts, correct: sum.correct + row.correct_answers, errors: sum.errors + row.errors }), { duration: 0, attempts: 0, correct: 0, errors: 0 });
  const consistent = sessions.every(row => row.correct_answers + row.errors === row.attempts);
  return { ...totals, sessions: sessions.length, accuracy: consistent ? accuracy(totals.attempts, totals.correct) : null, consistent };
}
export function groupTasks(events, catalog = []) {
  const groups = new Map(), titles = new Map(catalog.map(task => [task.task_key, task.title]));
  for (const event of events) {
    if (!['task_correct', 'task_error'].includes(event.event_type)) continue;
    const taskKey = event.task_key || event.object_id;
    if (!taskKey) continue;
    if (!groups.has(taskKey)) groups.set(taskKey, { task_key: taskKey, title: titles.get(taskKey) || taskKey, attempts: 0, correct: 0, errors: 0 });
    const task = groups.get(taskKey); task.attempts++;
    if (event.event_type === 'task_correct') task.correct++; else task.errors++;
  }
  return [...groups.values()].sort((a, b) => a.title.localeCompare(b.title, 'pt-BR'));
}

export function parseLocalSession(payload, participantCode) {
  identifier(participantCode);
  if (!payload || payload.ParticipantCode !== participantCode)
    throw new AppError('participant-mismatch', 'Esse arquivo pertence a outro participante. Abra a análise do cadastro com o UUID correspondente.');
  const session = parseSessions([{
    session_id: payload.SessionId, participant_code: payload.ParticipantCode, started_utc: payload.StartedUtc, ended_utc: payload.EndedUtc,
    duration_seconds: payload.DurationSeconds, attempts: payload.Attempts, correct_answers: payload.CorrectAnswers, errors: payload.Errors,
    application_version: payload.ApplicationVersion, schema_version: payload.SchemaVersion, coordinate_space: payload.CoordinateSpace,
  }], participantCode)[0];
  session.source = 'local';
  const rawEvents = array(payload.Events);
  if (rawEvents.length > 10000) throw new AppError('size', 'O arquivo excede o limite de 10.000 eventos por sessão para análise.');
  const events = parseEvents(rawEvents.map((event, index) => ({ id: String(index + 1), session_id: session.session_id, time_since_start: event.TimeSinceStart, event_type: event.EventType, object_id: event.ObjectId, details: event.Details, task_key: null })), session.session_id);
  const head = array(payload.HeadFrames), controls = array(payload.ControllerFrames);
  for (const frame of head) {
    number(frame.TimeSinceStart);
    for (const name of ['HeadPositionX','HeadPositionY','HeadPositionZ','HeadRotationX','HeadRotationY','HeadRotationZ'])
      if (typeof frame[name] !== 'number' || !Number.isFinite(frame[name])) throw invalid();
  }
  for (const frame of controls) {
    number(frame.TimeSinceStart);
    if (typeof frame.IsLeftTracked !== 'boolean' || typeof frame.IsRightTracked !== 'boolean') throw invalid();
  }
  const samples = { head: head.length, controllers: controls.length, left: controls.length ? controls.filter(frame => frame.IsLeftTracked).length / controls.length * 100 : null, right: controls.length ? controls.filter(frame => frame.IsRightTracked).length / controls.length * 100 : null };
  return { session, events, files: [], tasks: groupTasks(events), samples };
}

export function csvSessions(sessions) {
  const escape = value => {
    let cell = String(value ?? '');
    if (/^\s*[=+\-@]/u.test(cell) || /^[\t\r\n]/u.test(cell)) cell = "'" + cell;
    return '"' + cell.replace(/"/gu, '""') + '"';
  };
  const rows = [['participante_uuid','sessao_uuid','origem','inicio_utc','fim_utc','duracao_segundos','tentativas','acertos','erros','taxa_acerto_percentual','versao_aplicacao','espaco_coordenadas']];
  for (const row of sessions) rows.push([row.participant_code,row.session_id,row.source,row.started_utc,row.ended_utc,row.duration_seconds,row.attempts,row.correct_answers,row.errors,row.correct_answers + row.errors === row.attempts ? accuracy(row.attempts,row.correct_answers) : null,row.application_version,row.coordinate_space]);
  return '\uFEFF' + rows.map(row => row.map(escape).join(';')).join('\r\n');
}

export class TelemetryRepository {
  constructor(client) { this.client = client; }
  async pages(table, params, parse, idField, signal) {
    const rows = [], seen = new Set(); let cursor;
    while (true) {
      if (signal?.aborted) throw new AppError('cancelled', 'Análise interrompida.');
      const query = new URLSearchParams({ ...params, order: `${idField}.asc`, limit: '100' });
      if (cursor) query.set(idField, `gt.${cursor}`);
      const page = parse(await this.client.request(`/rest/v1/${table}?${query}`, { signal }));
      if (signal?.aborted) throw new AppError('cancelled', 'Análise interrompida.');
      if (!page.length) return rows;
      for (const row of page) {
        const id = row[idField];
        if (seen.has(id) || rows.length >= 10000) throw new AppError('telemetry-response', 'A consulta excedeu o limite ou repetiu registros. Nenhuma análise parcial será apresentada.');
        seen.add(id); rows.push(row);
      }
      cursor = page.at(-1)[idField];
    }
  }
  async participant(person, signal) {
    identifier(person.participant_code);
    let allowed;
    try {
      allowed = await this.client.request('/rest/v1/rpc/participant_telemetry_access', { body: { p_participant_code: person.participant_code }, signal });
    } catch (error) {
      if (['server', 'permission'].includes(error.code)) throw new AppError('telemetry-setup', 'A consulta de telemetria ainda não está autorizada. Execute o arquivo 005_participant_telemetry_analysis.sql no SQL Editor do Supabase. Você também pode analisar um Session.json local abaixo.');
      throw error;
    }
    if (allowed !== true) throw new AppError('permission', 'Este participante não está disponível para sua conta. Atualize os cadastros.');
    const profileQuery = new URLSearchParams({ select: 'participant_code,display_name,created_at', participant_code: `eq.${person.participant_code}` });
    const profile = await this.client.request(`/rest/v1/participants?${profileQuery}`, { signal });
    if (!Array.isArray(profile) || profile.length !== 1 || profile[0].participant_code !== person.participant_code) throw invalid();
    const createdAt = date(profile[0].created_at);
    let sessions;
    try { sessions = await this.pages('sessions', { select: 'session_id,participant_code,started_utc,ended_utc,duration_seconds,attempts,correct_answers,errors,application_version,schema_version,coordinate_space,sample_counts', participant_code: `eq.${person.participant_code}` }, data => parseSessions(data, person.participant_code), 'session_id', signal); }
    catch (error) { if (['server','permission'].includes(error.code)) throw new AppError('telemetry-setup', 'Execute a migração 007_telemetry_storage_ingestion.sql para consultar os dados enviados pelo Unity.'); throw error; }
    return { createdAt, sessions: sessions.sort((a, b) => Date.parse(b.started_utc) - Date.parse(a.started_utc)) };
  }
  async session(session, signal) {
    identifier(session.session_id);
    const [events, files, catalog] = await Promise.all([
      this.pages('session_events', { select: 'id,session_id,time_since_start,event_type,object_id,details,task_key', session_id: `eq.${session.session_id}` }, data => parseEvents(data, session.session_id), 'id', signal),
      this.pages('session_files', { select: 'id,session_id,file_type,bucket_id,storage_path,byte_size,uploaded_at,sha256,content_type', session_id: `eq.${session.session_id}` }, data => parseFiles(data, session.session_id), 'id', signal),
      this.pages('tasks', { select: 'task_key,title' }, data => array(data).map(row => ({ task_key: key(row?.task_key), title: text(row?.title) })), 'task_key', signal),
    ]).catch(error => { if (['server','permission'].includes(error.code)) throw new AppError('telemetry-setup', 'A consulta dos arquivos precisa da migração 007_telemetry_storage_ingestion.sql. Confira também as permissões da sua conta.'); throw error; });
    let samples = null;
    if (session.sample_counts !== null && session.sample_counts !== undefined) {
      const source = session.sample_counts;
      for (const field of ['head','controllers','left_tracked','right_tracked']) count(source[field]);
      if (source.left_tracked > source.controllers || source.right_tracked > source.controllers) throw invalid();
      samples = { head: source.head, controllers: source.controllers, left: source.controllers ? source.left_tracked / source.controllers * 100 : null, right: source.controllers ? source.right_tracked / source.controllers * 100 : null };
    }
    return { session, events: events.sort((a,b) => a.time_since_start - b.time_since_start), files, tasks: groupTasks(events, catalog), samples };
  }
  async download(session, file, signal) {
    identifier(session.session_id); identifier(session.participant_code);
    const parts = file.storage_path.split('/'), names = { session_json: 'Session.json', head_csv: 'Head.csv', controllers_csv: 'Controllers.csv', events_csv: 'Events.csv' };
    if (file.session_id !== session.session_id || file.bucket_id !== 'vr-telemetry' || parts.length !== 4 || !/^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/u.test(parts[0]) || parts[1] !== session.participant_code || parts[2] !== session.session_id || parts[3] !== names[file.file_type] || !/^[a-f\d]{64}$/u.test(file.sha256 ?? ''))
      throw new AppError('file-link', 'O arquivo não tem um vínculo de UUID e integridade válido para esta sessão.');
    const blob = await this.client.request('/storage/v1/object/authenticated/vr-telemetry/' + parts.map(encodeURIComponent).join('/'), { signal, binary: true });
    if (file.byte_size !== blob.size) throw new AppError('integrity', 'O tamanho do arquivo não corresponde ao registrado.');
    const hash = [...new Uint8Array(await crypto.subtle.digest('SHA-256', await blob.arrayBuffer()))].map(byte => byte.toString(16).padStart(2,'0')).join('');
    if (signal?.aborted) throw new AppError('cancelled', 'Download interrompido.');
    if (hash !== file.sha256) throw new AppError('integrity', 'O conteúdo do arquivo diverge do registro de integridade.');
    return blob;
  }
}
