import test from 'node:test';
import assert from 'node:assert/strict';
import { ParticipantClient } from '../dist/client.js';
import { parseSessions, parseEvents, parseFiles, parseLocalSession, summarize, groupTasks, csvSessions, TelemetryRepository } from '../dist/telemetry.js';

const id = n => n.toString(16).padStart(32, '0');
const person = { participant_code: id(1), display_name: 'Pessoa teste' };
const session = (n = 2, patch = {}) => ({ session_id: id(n), participant_code: id(1), started_utc: '2026-10-31T12:00:00Z', ended_utc: '2026-10-31T12:10:00Z', duration_seconds: 600, attempts: 4, correct_answers: 3, errors: 1, application_version: '1.0', schema_version: 1, coordinate_space: 'World', ...patch });
const event = (n = 1, patch = {}) => ({ id: n, session_id: id(2), time_since_start: n, event_type: 'task_correct', object_id: 'fase_1', details: '', task_key: null, ...patch });
const response = (data, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } });
const client = fetchImpl => { const result = new ParticipantClient({ supabaseUrl: 'https://example.invalid', publicKey: 'sb_publishable_fixture' }, { fetchImpl }); result.session = { accessToken: 'fixture', expiresAt: Date.now() + 60000, userId: 'fixture' }; return result; };
export const localPayload = () => ({ SchemaVersion: 1, SessionId: id(2), ParticipantCode: id(1), ParticipantDisplayName: 'Nome ignorado do arquivo', StartedUtc: '2026-10-31T12:00:00Z', EndedUtc: '2026-10-31T12:10:00Z', ApplicationVersion: '1.0', CoordinateSpace: 'World', DurationSeconds: 600, Attempts: 2, CorrectAnswers: 1, Errors: 1,
  Events: [{ TimeSinceStart: 1, EventType: 'task_correct', ObjectId: 'fase_1', Details: '' }, { TimeSinceStart: 2, EventType: 'task_error', ObjectId: 'fase_1', Details: '<script>fixture</script>' }],
  HeadFrames: [{ TimeSinceStart: 1, HeadPositionX: -1, HeadPositionY: 1, HeadPositionZ: 0, HeadRotationX: 0, HeadRotationY: 0, HeadRotationZ: 0 }],
  ControllerFrames: [{ TimeSinceStart: 1, IsLeftTracked: true, IsRightTracked: false }, { TimeSinceStart: 2, IsLeftTracked: true, IsRightTracked: true }],
});

test('resumo usa taxa ponderada por tentativas e nao media dos percentuais', () => {
  const rows = parseSessions([session(2, { attempts: 1, correct_answers: 1, errors: 0 }), session(3, { attempts: 9, correct_answers: 0, errors: 9 })], id(1));
  const result = summarize(rows);
  assert.equal(result.sessions, 2); assert.equal(result.accuracy, 10); assert.equal(result.duration, 1200);
  assert.equal(summarize([]).accuracy, null);
  assert.equal(summarize(parseSessions([session(2, { attempts: 0, correct_answers: 0, errors: 0 })], id(1))).accuracy, null);
  const mismatch = summarize(parseSessions([session(2, { attempts: 1, correct_answers: 3 })], id(1)));
  assert.equal(mismatch.consistent, false); assert.equal(mismatch.accuracy, null);
});

test('rejeita dados de outra pessoa, datas invalidas, metricas negativas e contagens fracionarias', () => {
  for (const patch of [{ participant_code: id(99) }, { session_id: 'invalid' }, { duration_seconds: -1 }, { attempts: 1.2 }, { correct_answers: Infinity }, { started_utc: 'not-a-date' }, { started_utc: '2026-10-31T13:00:00' }, { ended_utc: '2026-10-30T00:00:00Z' }])
    assert.throws(() => parseSessions([session(2, patch)], id(1)));
  assert.equal(parseSessions([session(2, { ended_utc: null })], id(1))[0].ended_utc, null);
  assert.throws(() => parseEvents([event(1, { session_id: id(3) })], id(2)));
  assert.throws(() => parseEvents([event(Number.MAX_SAFE_INTEGER + 1)], id(2)));
  assert.throws(() => parseFiles([{ id: 1, session_id: id(3) }], id(2)));
});

test('agrupamento segue task_key antes de object_id e ignora interacoes fora da avaliacao', () => {
  const events = parseEvents([event(1), event(2, { event_type: 'task_error' }), event(3, { task_key: 'fase_2', object_id: 'fase_1' }), event(4, { event_type: 'resistor_selected', object_id: 'R1' })], id(2));
  const tasks = groupTasks(events, [{ task_key: 'fase_1', title: 'Circuito inicial' }]);
  assert.equal(tasks.length, 2); assert.equal(tasks[0].title, 'Circuito inicial');
  assert.equal(tasks[0].attempts, 2); assert.equal(tasks[0].errors, 1); assert.equal(tasks[1].task_key, 'fase_2');
});

test('Session.json real do recorder e analisado pelo UUID com amostras e rastreio', () => {
  const result = parseLocalSession(localPayload(), id(1));
  assert.equal(result.session.source, 'local'); assert.equal(result.events.length, 2); assert.equal(result.tasks[0].attempts, 2);
  assert.deepEqual(result.samples, { head: 1, controllers: 2, left: 100, right: 50 });
  assert.equal(result.events[1].details, '<script>fixture</script>');
  assert.throws(() => parseLocalSession(localPayload(), id(99)), error => error.code === 'participant-mismatch');
  const bad = localPayload(); bad.HeadFrames[0].HeadPositionX = NaN; assert.throws(() => parseLocalSession(bad, id(1)));
  const empty = localPayload(); empty.HeadFrames = []; empty.ControllerFrames = []; assert.equal(parseLocalSession(empty, id(1)).samples.left, null);
  const missing = localPayload(); delete missing.Events; assert.throws(() => parseLocalSession(missing, id(1)));
});

test('consulta remota verifica acesso e pagina ate vazio sem misturar participantes', async () => {
  const requests = []; let page = 0;
  const repository = new TelemetryRepository(client(async (url, options) => {
    requests.push({ url, options });
    if (url.includes('/rpc/')) return response(true);
    if (url.includes('/participants?')) return response([{ ...person, created_at: '2026-10-01T00:00:00Z' }]);
    return response([[session(2)], [session(3, { started_utc: '2026-10-31T12:05:00Z' })], []][page++]);
  }));
  const result = await repository.participant(person);
  assert.equal(result.sessions[0].session_id, id(3)); assert.equal(result.sessions.length, 2);
  assert.equal(JSON.parse(requests[0].options.body).p_participant_code, id(1));
  const queries = requests.filter(request => request.url.includes('/sessions?')).map(request => new URL(request.url));
  assert(queries.every(url => url.searchParams.get('participant_code') === `eq.${id(1)}`));
  assert.equal(queries[1].searchParams.get('session_id'), `gt.${id(2)}`);
  assert(requests.every(request => request.options.headers.Authorization === 'Bearer fixture'));
});

test('migracao ausente ou permissao recusada nao vira lista vazia e nao altera banco', async () => {
  for (const status of [403, 404]) {
    const repository = new TelemetryRepository(client(async () => response({ message: 'private-fixture' }, status)));
    await assert.rejects(() => repository.participant(person), error => error.code === 'telemetry-setup' && !error.message.includes('private-fixture'));
  }
  const denied = new TelemetryRepository(client(async () => response(false)));
  await assert.rejects(() => denied.participant(person), error => error.code === 'permission');
  const revoked = new TelemetryRepository(client(async () => response({}, 401)));
  await assert.rejects(() => revoked.participant(person), error => error.code === 'session');
});

test('falha de rede e resposta duplicada nao produzem analise parcial', async () => {
  const repo = new TelemetryRepository({ request: async () => [session()] });
  await assert.rejects(() => repo.pages('sessions', {}, data => parseSessions(data, id(1)), 'session_id'), error => error.code === 'telemetry-response');
  const offline = new TelemetryRepository(client(async () => { throw new TypeError('private-fixture'); }));
  await assert.rejects(() => offline.participant(person), error => error.code === 'network' && !error.message.includes('private-fixture'));
});

test('cancelamento impede leituras e instalacao de resposta antiga', async () => {
  const cancellation = new AbortController(); cancellation.abort(); let calls = 0;
  const repo = new TelemetryRepository({ request: async () => { calls++; return []; } });
  await assert.rejects(() => repo.pages('sessions', {}, data => data, 'session_id', cancellation.signal)); assert.equal(calls, 0);
  const cancelled = new AbortController();
  const pending = new TelemetryRepository({ request: async () => { cancelled.abort(); return [session()]; } });
  await assert.rejects(() => pending.pages('sessions', {}, data => parseSessions(data, id(1)), 'session_id', cancelled.signal));
});

test('detalhes leem apenas eventos e arquivos da sessao escolhida e catalogo de tarefas', async () => {
  const requests = [], seen = new Set();
  const repo = new TelemetryRepository(client(async url => {
    const parsed = new URL(url), table = parsed.pathname.split('/').at(-1); requests.push(parsed);
    if (seen.has(table)) return response([]); seen.add(table);
    if (table === 'session_events') return response([event(1, { id: '9007199254740995' }), event(2, { event_type: 'task_error' })]);
    if (table === 'session_files') return response([{ id: 1, session_id: id(2), file_type: 'session_json', bucket_id: 'vr-telemetry', storage_path: `${id(2)}/Session.json`, byte_size: 100, uploaded_at: '2026-10-31T12:11:00Z' }]);
    return response([{ task_key: 'fase_1', title: 'Circuito inicial' }]);
  }));
  const result = await repo.session(parseSessions([session()], id(1))[0]);
  assert.equal(result.tasks[0].title, 'Circuito inicial'); assert.equal(result.files.length, 1); assert.equal(result.events.length, 2);
  assert(requests.filter(url => !url.pathname.endsWith('/tasks')).every(url => url.searchParams.get('session_id') === `eq.${id(2)}`));
  assert(requests.some(url => url.searchParams.get('id') === 'gt.2'));
});

test('CSV separa origens, preserva valores e neutraliza formulas de planilha', () => {
  const parsed = parseSessions([session(2, { application_version: '=HYPERLINK("fixture")', coordinate_space: '+fixture' })], id(1));
  const csv = csvSessions(parsed);
  assert(csv.startsWith('\uFEFF')); assert(csv.includes('"supabase"')); assert(csv.includes('"\'=HYPERLINK(""fixture"")"')); assert(csv.includes('"\'+fixture"'));
  assert.equal(csv.split('\r\n').length, 2); assert(csv.includes('taxa_acerto_percentual'));
});
