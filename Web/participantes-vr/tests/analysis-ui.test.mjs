import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { AnalysisUI, duration } from '../dist/analysis-ui.js';
import { AppError } from '../dist/client.js';

const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
const id = n => n.toString(16).padStart(32, '0');
const person = n => ({ participant_code: id(n), display_name: `Pessoa ${n}` });
const session = (n = 2, patch = {}) => ({ session_id: id(n), participant_code: id(1), started_utc: '2026-10-31T12:00:00Z', ended_utc: '2026-10-31T12:10:00Z', duration_seconds: 600, attempts: 4, correct_answers: 3, errors: 1, application_version: '1.0', schema_version: 1, coordinate_space: 'World', source: 'supabase', ...patch });
const report = (n = 2) => ({ session: session(n, { source: 'local' }), events: [], tasks: [], files: [], samples: { head: 12, controllers: 24, left: 100, right: 50 } });
const settle = () => new Promise(resolve => setImmediate(resolve));

class Element {
  constructor(tag = 'div') { this.tagName = tag.toUpperCase(); this.children = []; this.listeners = new Map(); this.attributes = {}; this.style = {}; this.value = ''; this.hidden = false; this.disabled = false; this.files = []; this._text = ''; }
  set textContent(text) { this._text = String(text); this.children = []; }
  get textContent() { return this._text + this.children.map(child => child.textContent).join(''); }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; this._text = ''; }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(name, listener) { if (!this.listeners.has(name)) this.listeners.set(name, []); this.listeners.get(name).push(listener); }
  dispatch(name) { for (const listener of this.listeners.get(name) || []) listener({ preventDefault() {} }); }
  focus() { globalThis.document.activeElement = this; }
  remove() {}
  click() { this.dispatch('click'); }
}
function fixture(t, repository = {}) {
  const elements = new Map(ids.map(name => [name, new Element()]));
  globalThis.document = { body: new Element('body'), getElementById: name => { if (!elements.has(name)) throw new Error(`Missing HTML control ${name}`); return elements.get(name); }, createElement: tag => new Element(tag) };
  const workers = [], state = { expired: 0, back: 0 };
  const ui = new AnalysisUI(null, { onBack: () => { state.back++; elements.get('dashboard').hidden = false; }, onSessionExpired: () => { state.expired++; ui.reset(); }, workerFactory: () => { const worker = { terminate() { this.terminated = true; }, postMessage(message) { this.message = message; } }; workers.push(worker); return worker; } });
  ui.repository = { participant: async () => ({ createdAt: '2026-10-01T00:00:00Z', sessions: [session()] }), session: async entry => ({ ...report(), session: entry, samples: null }), ...repository };
  t.after(() => ui.reset());
  return { ui, get: name => elements.get(name), workers, state };
}

test('analise mostra nome, UUID, totais e detalhes com retorno ao cadastro', async t => {
  const { ui, get, state } = fixture(t);
  ui.open(person(1)); await settle();
  assert.equal(get('analysis-panel').hidden, false); assert.equal(get('dashboard').hidden, true);
  assert.equal(get('analysis-code').textContent, id(1)); assert.equal(get('metric-accuracy').textContent, '75%');
  assert.equal(get('session-rows').children.length, 1); assert.equal(get('analysis-export').disabled, false);
  const action = get('session-rows').children[0].children.at(-1).children[0]; action.click(); await settle();
  assert.equal(get('session-detail').hidden, false); assert.match(get('sample-summary').textContent, /Session.json/);
  get('analysis-back').click(); assert.equal(state.back, 1); assert.equal(get('analysis-panel').hidden, true);
  assert.equal(get('analysis-name').textContent, ''); assert.equal(get('event-rows').children.length, 0);
});

test('troca rapida de pessoa ignora respostas atrasadas de outro UUID', async t => {
  const pending = new Map();
  const { ui, get } = fixture(t, { participant: person => new Promise(resolve => pending.set(person.participant_code, resolve)) });
  ui.open(person(1)); ui.open(person(9));
  pending.get(id(1))({ createdAt: '2026-10-01T00:00:00Z', sessions: [session()] }); await settle();
  assert.equal(get('analysis-name').textContent, 'Pessoa 9'); assert.equal(get('metric-sessions').textContent, '—');
  pending.get(id(9))({ createdAt: '2026-10-01T00:00:00Z', sessions: [session(8, { participant_code: id(9), correct_answers: 0, errors: 4 })] }); await settle();
  assert.equal(get('metric-correct').textContent, '0'); assert.equal(get('metric-sessions').textContent, '1');
});

test('falta de autorizacao nao e apresentada como zero sessoes ou desempenho zero', async t => {
  const { ui, get } = fixture(t, { participant: async () => { throw new AppError('telemetry-setup', 'Execute 005_participant_telemetry_analysis.sql'); } });
  ui.open(person(1)); await settle();
  assert.match(get('analysis-message').textContent, /005_/); assert.equal(get('metric-sessions').textContent, '—'); assert.equal(get('metric-accuracy').textContent, '—');
  assert.equal(get('analysis-export').disabled, true);
  get('analysis-source').value = 'local'; get('analysis-source').dispatch('change');
  get('analysis-source').value = 'supabase'; get('analysis-source').dispatch('change');
  assert.match(get('analysis-message').textContent, /005_/);
});

test('consulta vazia confirmada e distinguida de dados pendentes', async t => {
  const { ui, get } = fixture(t, { participant: async () => ({ createdAt: '2026-10-01T00:00:00Z', sessions: [] }) });
  ui.open(person(1)); await settle();
  assert.equal(get('metric-sessions').textContent, '0'); assert.equal(get('metric-accuracy').textContent, '—');
  assert.match(get('analysis-message').textContent, /Nenhuma sessão recebida/);
});

test('arquivo local tem origem explicita, nao duplica sessoes e nao e misturado ao banco', async t => {
  const { ui, get, workers } = fixture(t); ui.open(person(1)); await settle();
  ui.import({ size: 120 }); assert.equal(workers[0].message.participantCode, id(1));
  workers[0].onmessage({ data: { report: report() } });
  assert.equal(ui.source, 'local'); assert.equal(get('metric-sessions').textContent, '1'); assert.match(get('analysis-source-badge').textContent, /não enviado/);
  assert.match(get('sample-summary').textContent, /12 amostras/); assert.equal(workers[0].terminated, true);
  ui.import({ size: 120 }); workers[1].onmessage({ data: { report: report() } });
  assert.equal(ui.locals.size, 1); assert.equal(get('metric-sessions').textContent, '1');
  get('analysis-source').value = 'supabase'; get('analysis-source').dispatch('change');
  assert.equal(ui.sessions.length, 1); assert.equal(get('session-detail').hidden, true); assert.equal(get('analysis-source-badge').textContent, 'Supabase');
});

test('resultado de arquivo encerrado nao volta a aparecer em outra conta ou pessoa', async t => {
  const { ui, get, workers } = fixture(t); ui.open(person(1)); await settle(); ui.import({ size: 120 });
  const old = workers[0]; ui.reset(); old.onmessage({ data: { report: report() } });
  assert.equal(get('analysis-panel').hidden, true); assert.equal(get('analysis-code').textContent, ''); assert.equal(ui.locals.size, 0); assert.equal(old.terminated, true);
});

test('resposta remota atrasada nao sobrescreve aviso de analise local', async t => {
  let reject;
  const { ui, get, workers } = fixture(t, { participant: () => new Promise((_, fail) => { reject = fail; }) });
  ui.open(person(1)); ui.import({ size: 100 }); workers[0].onmessage({ data: { report: report() } });
  reject(new AppError('telemetry-setup', 'Execute 005')); await settle();
  assert.match(get('analysis-message').textContent, /não foram enviados/); assert.equal(ui.source, 'local');
});

test('limites e erro de importacao preservam a analise existente', async t => {
  const { ui, get, workers } = fixture(t); ui.open(person(1)); await settle();
  ui.import({ size: 65 * 1024 * 1024 }); assert.equal(workers.length, 0); assert.match(get('analysis-message').textContent, /64 MB/);
  ui.import({ size: 100 }); workers[0].onmessage({ data: { error: 'Esse arquivo pertence a outro participante.' } });
  assert.match(get('analysis-message').textContent, /outro participante/); assert.equal(ui.source, 'supabase'); assert.equal(get('analysis-file').disabled, false);
});

test('sessoes e eventos sao paginados e nomes/conteudos sao tratados como texto', async t => {
  const detail = report(); detail.events = Array.from({ length: 125 }, (_, index) => ({ id: String(index), time_since_start: index, event_type: index % 2 ? 'task_error' : 'task_correct', task_key: '', object_id: 'R1', details: '<img src=fixture onerror=fixture()>' }));
  const { ui, get } = fixture(t, { participant: async () => ({ createdAt: '2026-10-01T00:00:00Z', sessions: Array.from({ length: 30 }, (_, i) => session(i + 2)) }), session: async () => detail });
  ui.open({ ...person(1), display_name: '<script>fixture</script>' }); await settle();
  assert.equal(get('analysis-name').textContent, '<script>fixture</script>'); assert.equal(get('session-rows').children.length, 25);
  get('sessions-next').click(); assert.equal(get('session-rows').children.length, 5); assert.equal(get('sessions-next').disabled, true);
  await ui.openSession(session()); assert.equal(get('event-rows').children.length, 50);
  get('events-next').click(); assert.equal(get('events-page').textContent, '2 / 3 · 125 eventos');
  get('event-search').value = 'task_error'; get('event-search').dispatch('input');
  assert.equal(ui.eventPage, 0); assert.equal(get('events-page').textContent, '1 / 2 · 62 eventos');
  assert.match(get('event-rows').children[0].textContent, /<img src=fixture/);
});

test('expiracao encerra a analise e remove identidade e arquivos em memoria', async t => {
  const { ui, get, state } = fixture(t, { participant: async () => { throw new AppError('session', 'Acesso expirado.'); } });
  ui.open(person(1)); await settle(); assert.equal(state.expired, 1); assert.equal(get('analysis-panel').hidden, true);
  assert.equal(get('analysis-name').textContent, ''); assert.equal(get('session-rows').children.length, 0);
});

test('atualizar banco cancela importacao anterior e falha ao abrir worker nao bloqueia novo arquivo', async t => {
  const { ui, get, workers } = fixture(t); ui.open(person(1)); await settle();
  ui.import({ size: 100 }); const old = workers[0];
  await ui.load(); old.onmessage({ data: { report: report() } });
  assert.equal(old.terminated, true); assert.equal(ui.source, 'supabase'); assert.equal(ui.locals.size, 0);
  ui.workerFactory = () => { throw new Error('Fixture: browser rejected worker'); };
  ui.import({ size: 100 }); assert.equal(get('analysis-file').disabled, false); assert.match(get('analysis-message').textContent, /navegador/);
});

test('duracao e arredondada sem segundos fora do intervalo e mantem horas', () => {
  assert.equal(duration(59.6), '1min 0s'); assert.equal(duration(3660), '1h 1min');
});

test('arquivo privado tem acoes e previa CSV mantem conteudo como texto', async t => {
  const detail=report(); detail.session.source='supabase';
  const file={file_type:'head_csv',session_id:detail.session.session_id,storage_path:`owner/${id(1)}/${id(2)}/Head.csv`,byte_size:10,sha256:'a'.repeat(64)}; detail.files=[file];
  const {ui,get,workers}=fixture(t,{session:async()=>detail,download:async()=>new Blob(['fixture'])});
  ui.open(person(1)); await settle(); await ui.openSession(session());
  const buttons=get('file-list').children[0].children.filter(child=>child.tagName==='BUTTON'); assert.equal(buttons.length,2); assert.equal(buttons[0].disabled,false);
  await ui.openStoredFile(file,'preview'); assert.equal(workers[0].message.kind,'csv');
  workers[0].onmessage({data:{preview:{headers:['ParticipantCode','Details'],rows:[[id(1),'<img src=fixture>']],totalRows:1}}});
  assert.equal(get('storage-preview').hidden,false); assert.match(get('storage-preview-summary').textContent,/UUIDs e SHA-256/); assert.match(get('storage-preview-rows').textContent,/<img src=fixture>/);
  ui.closeDetail(); assert.equal(get('storage-preview').hidden,true); assert.equal(get('storage-preview-rows').children.length,0);
});

test('fechar detalhes cancela download e ignora arquivo de uma sessao anterior', async t => {
  let finish;
  const {ui,get}=fixture(t,{download:()=>new Promise(resolve=>{finish=resolve;})});
  ui.open(person(1)); await settle(); await ui.openSession(session());
  const pending=ui.openStoredFile({file_type:'head_csv'},'preview'); ui.closeDetail(); finish(new Blob(['fixture'])); await pending;
  assert.equal(get('session-detail').hidden,true); assert.equal(get('storage-preview').hidden,true);
});

test('erro de integridade de arquivo e exibido sem alterar a sessao', async t => {
  const {ui,get}=fixture(t,{download:async()=>{throw new AppError('integrity','SHA-256 divergente.');}});
  ui.open(person(1)); await settle(); await ui.openSession(session()); await ui.openStoredFile({file_type:'head_csv'},'preview');
  assert.match(get('detail-message').textContent,/SHA-256 divergente/); assert.equal(ui.detail.session.session_id,id(2)); assert.equal(get('storage-preview').hidden,true);
});
