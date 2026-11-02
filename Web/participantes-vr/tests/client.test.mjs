import test from 'node:test';
import assert from 'node:assert/strict';
import { ParticipantClient, AppError, validateName, parseParticipants } from '../dist/client.js';

const settings = { supabaseUrl: 'https://example.invalid', publicKey: 'sb_publishable_fixture' };
const id = n => n.toString(16).padStart(32, '0');
const row = (n, name = 'Mesmo nome') => ({ participant_code: id(n), display_name: name });
const response = (data, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } });
const signedIn = fetchImpl => { const client = new ParticipantClient(settings, { fetchImpl }); client.session = { accessToken: 'fixture-token', expiresAt: Date.now() + 60000, userId: 'fixture-user' }; return client; };

test('fetch nativo conserva o contexto global exigido pelo navegador', async () => {
  const originalFetch = globalThis.fetch;
  let requests = 0;
  globalThis.fetch = async function (url) {
    if (this !== globalThis) throw new TypeError('Illegal invocation');
    requests++;
    if (url.includes('/auth/v1/token')) return response({ access_token: 'fixture-token', expires_in: 3600, user: { id: 'fixture-user' } });
    return response({ version: 'fixture' });
  };
  try {
    const client = new ParticipantClient(settings);
    assert.match(await client.checkConnection(), /serviço de autenticação respondeu/);
    await client.signIn('operator@example.invalid', 'fixture-password');
    assert.equal(client.isAuthenticated(), true);
    assert.equal(requests, 2);
  } finally { globalThis.fetch = originalFetch; }
});

test('fetch fornecido explicitamente também não recebe a instância como contexto', async () => {
  const nativeLikeFetch = async function () {
    if (this !== globalThis) throw new TypeError('Illegal invocation');
    return response({ version: 'fixture' });
  };
  const client = new ParticipantClient(settings, { fetchImpl: nativeLikeFetch });
  assert.match(await client.checkConnection(), /serviço de autenticação respondeu/);
});

test('normaliza nome, aceita homônimos e valida códigos reais', () => {
  assert.equal(validateName('  Ana   Maria  '), 'Ana Maria');
  assert.equal(validateName('李'), '李');
  assert.throws(() => validateName(' '), AppError);
  assert.throws(() => validateName('a'.repeat(121)), AppError);
  assert.equal(parseParticipants([row(1), row(2)]).length, 2);
  assert.match(parseParticipants([row(1, null)])[0].display_name, /Participante/);
  for (const invalid of [null, {}, [null], [{ participant_code: 'wrong', display_name: 'Nome' }], [{ participant_code: id(1) }]]) assert.throws(() => parseParticipants(invalid), AppError);
});

test('aceita somente URL HTTPS e chave pública', () => {
  for (const supabaseUrl of ['http://example.invalid', 'https://example.invalid/path', 'https://name:password@example.invalid', 'https://example.invalid/?key=x'])
    assert.throws(() => new ParticipantClient({ ...settings, supabaseUrl }), AppError);
  for (const publicKey of ['', 'sb_secret_fixture', 'invalid']) assert.throws(() => new ParticipantClient({ ...settings, publicKey }), AppError);
  const serviceRole = `fixture.${btoa(JSON.stringify({ role: 'service_role' }))}.fixture`;
  assert.throws(() => new ParticipantClient({ ...settings, publicKey: serviceRole }), AppError);
});

test('login preserva senha, separa chave e token e não usa cookies', async () => {
  let request;
  const client = new ParticipantClient(settings, { fetchImpl: async (url, options) => {
    request = { url, ...options }; return response({ access_token: 'user-token', expires_in: 3600, user: { id: 'user-id' } });
  }});
  const password = ' fixture"\\ password ';
  await client.signIn('  operator@example.invalid ', password);
  assert.equal(request.url, 'https://example.invalid/auth/v1/token?grant_type=password');
  assert.equal(JSON.parse(request.body).password, password);
  assert.equal(JSON.parse(request.body).email, 'operator@example.invalid');
  assert.equal(request.headers.Authorization, undefined);
  assert.equal(request.headers.apikey, settings.publicKey);
  assert.equal(request.credentials, 'omit');
  assert.equal(request.cache, 'no-store');
  assert.equal(client.isAuthenticated(), true);
  client.signOut(); assert.equal(client.session, null);
});

test('lista pagina até vazio mesmo com limite menor imposto pelo servidor', async () => {
  const queries = [], pages = [[row(1), row(2)], [row(3)], []];
  const client = signedIn(async (url, options) => { queries.push({ url, options }); return response(pages.shift()); });
  const result = await client.listParticipants();
  assert.equal(result.length, 3); assert.equal(queries.length, 3);
  assert.equal(new URL(queries[1].url).searchParams.get('participant_code'), `gt.${id(2)}`);
  assert.equal(new URL(queries[0].url).searchParams.get('select'), 'participant_code,display_name');
  assert.equal(queries[0].options.headers.Authorization, 'Bearer fixture-token');
});

test('falha de página posterior não retorna lista parcial e repetição não entra em loop', async () => {
  let call = 0;
  const client = signedIn(async () => ++call === 1 ? response([row(1)]) : response({ message: 'secret-fixture' }, 403));
  await assert.rejects(() => client.listParticipants(), error => error.code === 'permission' && !error.message.includes('secret-fixture'));
  let duplicateCalls = 0;
  const duplicate = signedIn(async () => { duplicateCalls++; return response([row(1)]); });
  await assert.rejects(() => duplicate.listParticipants(), error => error.code === 'response');
  assert.equal(duplicateCalls, 2);
});

test('cadastro gera apenas INSERT e confirma o mesmo UUID no retry', async () => {
  const requests = [];
  const client = signedIn(async (url, options) => {
    requests.push({ url, ...options }); return options.method === 'POST' ? response([]) : response([row(7, 'Ana')]);
  });
  const saved = await client.registerParticipant(' Ana ', id(7));
  assert.equal(saved.participant_code, id(7)); assert.equal(requests.length, 2);
  assert.equal(requests[0].method, 'POST');
  assert.equal(JSON.parse(requests[0].body).participant_code, id(7));
  assert.match(requests[0].headers.Prefer, /resolution=ignore-duplicates/);
  assert.equal(new URL(requests[1].url).searchParams.get('participant_code'), `eq.${id(7)}`);
  await client.registerParticipant('Ana', id(7));
  assert.equal(JSON.parse(requests[2].body).participant_code, id(7));
});

test('cadastro não anuncia sucesso para outro código/nome ou ausência de confirmação', async () => {
  for (const rows of [[row(8, 'Ana')], [row(7, 'Outra')], []]) {
    const client = signedIn(async () => response(rows));
    await assert.rejects(() => client.registerParticipant('Ana', id(7)), error => error.code === 'confirmation');
  }
});

test('cadastro recusado é apresentado como falta de autorização e mantém o login', async () => {
  const client = signedIn(async () => response({ code: '42501', message: 'secret-fixture' }, 403));
  await assert.rejects(() => client.registerParticipant('Ana', id(7)), error =>
    error.code === 'permission' && error.message.includes('cadastrar participantes') && !error.message.includes('secret-fixture'));
  assert.equal(client.isAuthenticated(), true);
});

test('login diferencia senha incorreta, confirmação, bloqueio e resposta inválida', async () => {
  for (const [data, status, code] of [
    [{ error_code: 'invalid_credentials' }, 400, 'credentials'],
    [{ error_code: 'email_not_confirmed' }, 400, 'confirmation'],
    [{ message: 'secret-fixture' }, 429, 'limit'],
    [{ access_token: 'token' }, 200, 'response'],
    [null, 200, 'response'],
  ]) {
    const client = new ParticipantClient(settings, { fetchImpl: async () => response(data, status) });
    await assert.rejects(() => client.signIn('operator@example.invalid', 'fixture'), error => error.code === code && !error.message.includes('secret-fixture'));
    assert.equal(client.session, null);
  }
});

test('token expirado impede consulta antes de chamar a rede', async () => {
  let calls = 0; const client = signedIn(async () => { calls++; return response([]); });
  client.session.expiresAt = Date.now() - 1;
  await assert.rejects(() => client.listParticipants(), error => error.code === 'session'); assert.equal(calls, 0);
});

test('cancelar login impede instalação de token de resposta antiga', async () => {
  const cancellation = new AbortController();
  const client = new ParticipantClient(settings, { fetchImpl: async () => { cancellation.abort(); return response({ access_token: 'old-token', expires_in: 3600, user: { id: 'old-user' } }); } });
  await assert.rejects(() => client.signIn('operator@example.invalid', 'fixture', cancellation.signal));
  assert.equal(client.session, null);
});

test('falha de conexão não é interpretada como lista vazia', async () => {
  const client = signedIn(async () => { throw new Error('secret-fixture'); });
  await assert.rejects(() => client.listParticipants(), error => error.code === 'network' && !error.message.includes('secret-fixture'));
});

test('verificação de conexão não envia e-mail, senha ou token de usuário', async () => {
  let observed;
  const client = signedIn(async (url, options) => { observed = { url, ...options }; return response({ version: 'fixture' }); });
  assert.match(await client.checkConnection(), /serviço de autenticação respondeu/);
  assert.equal(observed.url, 'https://example.invalid/auth/v1/health');
  assert.equal(observed.method, 'GET');
  assert.equal(observed.body, undefined);
  assert.equal(observed.headers.apikey, settings.publicKey);
  assert.equal(observed.headers.Authorization, undefined);
});

test('verificação distingue chave recusada de falha de rede sem expor a resposta', async () => {
  const refused = new ParticipantClient(settings, { fetchImpl: async () => response({ message: 'secret-fixture' }, 401) });
  await assert.rejects(() => refused.checkConnection(), error => error.code === 'key' && !error.message.includes('secret-fixture'));
  const offline = new ParticipantClient(settings, { fetchImpl: async () => { throw new TypeError('secret-fixture'); } });
  await assert.rejects(() => offline.checkConnection(), error => error.code === 'network' && error.message.includes('F12') && !error.message.includes('secret-fixture'));
});

test('timeout da conexão é identificado sem confundir com senha incorreta', async () => {
  const client = new ParticipantClient(settings, { timeoutMs: 5, fetchImpl: async (_, options) => new Promise((resolve, reject) => {
    options.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
  }) });
  await assert.rejects(() => client.checkConnection(), error => error.code === 'timeout');
});
