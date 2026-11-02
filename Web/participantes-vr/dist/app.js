import { config } from './config.js';
import { ParticipantClient, AppError, validateName } from './client.js';
import { AnalysisUI } from './analysis-ui.js';
import { AdminRepository, ParticipantManagementUI } from './admin.js';

const $ = id => document.getElementById(id);
const collator = new Intl.Collator('pt-BR', { sensitivity: 'base' });
let client, analysis, admin, management, accountRequest = new AbortController(), rosterRequest, rosterVersion = 0;
let roster = [], recent = [], draft = null, registering = false, signingIn = false, hasRoster = false;
let checkingConnection = false;

function message(id, text = '', kind = '') { const node = $(id); node.textContent = text; node.className = `message ${kind}`; }
function pageMessage(text = '') { $('page-message').textContent = text; $('page-message').hidden = !text; }
function readable(error) { return error instanceof AppError ? error.message : 'Não foi possível concluir a operação. Tente novamente.'; }
function node(tag, className, text) { const result = document.createElement(tag); if (className) result.className = className; if (text !== undefined) result.textContent = text; return result; }
function showEmpty(title, text) { const empty = $('roster-empty'); empty.replaceChildren(node('span', 'empty-icon', '◎'), node('h3', '', title), node('p', '', text)); empty.hidden = false; }

function renderRoster() {
  const search = $('search').value.trim().toLocaleLowerCase('pt-BR');
  const visible = roster.filter(person => `${person.display_name} ${person.participant_code}`.toLocaleLowerCase('pt-BR').includes(search))
    .sort((a, b) => collator.compare(a.display_name, b.display_name) || a.participant_code.localeCompare(b.participant_code));
  $('participant-rows').replaceChildren();
  $('total-count').textContent = hasRoster ? String(roster.length).padStart(2, '0') : '—';
  $('visible-count').textContent = hasRoster ? `${visible.length}` : '—';
  for (const person of visible) {
    const tr = node('tr');
    const personCell = node('td'), content = node('div', 'person-cell');
    const initials = person.display_name.trim().split(/\s+/u).slice(0, 2).map(part => [...part][0] ?? '').join('').toLocaleUpperCase('pt-BR');
    const avatar = node('span', 'avatar', initials); avatar.setAttribute('aria-hidden', 'true');
    content.append(avatar, node('span', 'person-name', person.display_name)); personCell.append(content);
    const idCell = node('td'); const code = node('code', '', person.participant_code.slice(0, 8)); code.title = person.participant_code; idCell.append(code);
    const status = node('td'); status.append(node('span', 'status-badge', 'Cadastrado'));
    const action = node('td'), button = node('button', 'secondary', 'Ver análise'); button.type = 'button';
    button.setAttribute('aria-label', `Ver análise de ${person.display_name}, código ${person.participant_code.slice(0, 8)}`);
    button.addEventListener('click', () => { management.close(); analysis.open(person); }); action.append(button);
    const edit = node('button', 'secondary', 'Editar cadastro'); edit.type = 'button';
    edit.setAttribute('aria-label', `Editar cadastro de ${person.display_name}`); edit.addEventListener('click', () => management.open(person)); action.append(edit);
    const cells = ['session_count','attempts','correct_answers','errors'].map(field => node('td', '', new Intl.NumberFormat('pt-BR').format(person[field])));
    tr.append(personCell, idCell, ...cells, status, action); $('participant-rows').append(tr);
  }
  $('roster-empty').hidden = visible.length > 0;
  if (!visible.length && hasRoster) showEmpty(search ? 'Nenhum resultado' : 'O primeiro nome vem agora.', search ? 'Tente outro nome ou parte do código.' : 'Cadastre um participante para preparar a experiência.');
}

function renderRecent() {
  $('recent-list').replaceChildren(); $('recent-count').textContent = String(recent.length); $('recent-empty').hidden = recent.length > 0;
  for (const person of recent) {
    const li = node('li'), identity = node('div'); identity.append(node('strong', '', person.display_name), node('small', '', person.participant_code.slice(0, 8)));
    const time = node('time', '', new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' }).format(person.savedAt));
    time.dateTime = person.savedAt.toISOString(); li.append(identity, time); $('recent-list').append(li);
  }
}

function signOut(text = '') {
  accountRequest.abort(); rosterRequest?.abort(); rosterVersion++; accountRequest = new AbortController(); client?.signOut();
  analysis?.reset();
  management?.close(); $('admin-account').textContent = ''; $('admin-account').hidden = true;
  roster = []; recent = []; draft = null; registering = false; signingIn = false; hasRoster = false;
  $('dashboard').hidden = true; $('login-section').hidden = false; $('sign-out').hidden = true;
  $('connection').textContent = 'Aguardando acesso'; $('connection').classList.remove('connected');
  $('register-form').reset(); $('password').value = ''; $('search').value = ''; $('participant-name').disabled = false;
  $('register-submit').disabled = false; $('login-submit').disabled = false; $('email').disabled = false; $('password').disabled = false;
  message('register-message'); pageMessage(); renderRoster(); renderRecent(); message('login-message', text, text ? 'error' : '');
}

async function loadRoster() {
  if (!client.isAuthenticated()) { signOut('Seu acesso expirou. Entre novamente.'); return; }
  rosterRequest?.abort(); const version = ++rosterVersion; const request = new AbortController(); rosterRequest = request;
  const accountSignal = accountRequest.signal; const abort = () => request.abort(); accountSignal.addEventListener('abort', abort, { once: true });
  $('refresh').disabled = true; pageMessage(); if (!roster.length) showEmpty('Buscando participantes', 'Aguarde a consulta dos cadastros.');
  try {
    const data = await admin.overview(request.signal);
    if (request.signal.aborted || version !== rosterVersion || accountSignal.aborted) return;
    roster = data; hasRoster = true; renderRoster(); $('connection').textContent = 'Lista atualizada'; $('connection').classList.add('connected');
  } catch (error) {
    if (request.signal.aborted || accountSignal.aborted || version !== rosterVersion) return;
    if (error.code === 'session') { signOut(error.message); return; }
    pageMessage(readable(error)); $('connection').textContent = 'Atualização pendente'; $('connection').classList.remove('connected');
    if (!roster.length) showEmpty('A lista ainda não chegou.', 'Confira sua conexão e tente atualizar novamente.');
  } finally {
    accountSignal.removeEventListener('abort', abort);
    if (version === rosterVersion && !accountSignal.aborted) $('refresh').disabled = false;
  }
}

$('login-form').addEventListener('submit', async event => {
  event.preventDefault(); if (signingIn || !client) return;
  signingIn = true; const signal = accountRequest.signal;
  $('login-submit').disabled = true; $('email').disabled = true; $('password').disabled = true; message('login-message', 'Entrando...');
  let password = $('password').value; $('password').value = '';
  try {
    const pending = client.signIn($('email').value, password, signal); password = null; await pending;
    if (signal.aborted) return;
    const account = await admin.identity(signal);
    if (signal.aborted) return;
    $('admin-account').textContent = `Administrador · ${account.userId}`; $('admin-account').hidden = false;
    $('login-section').hidden = true; $('dashboard').hidden = false; $('sign-out').hidden = false;
    $('connection').textContent = 'Consultando cadastros'; message('login-message'); $('participant-name').focus(); await loadRoster();
  } catch (error) { if (!signal.aborted) { client.signOut(); message('login-message', readable(error), 'error'); } }
  finally { password = null; if (!signal.aborted) { signingIn = false; $('login-submit').disabled = false; $('email').disabled = false; $('password').disabled = false; } }
});

$('register-form').addEventListener('submit', async event => {
  event.preventDefault(); if (registering || !client) return;
  let name; try { name = validateName($('participant-name').value); } catch (error) { message('register-message', readable(error), 'error'); return; }
  if (!draft || draft.name !== name) draft = { name, id: crypto.randomUUID().replace(/-/g, '') };
  registering = true; const signal = accountRequest.signal;
  $('register-submit').disabled = true; $('participant-name').disabled = true; message('register-message', 'Confirmando cadastro...');
  try {
    const person = await client.registerParticipant(name, draft.id, signal);
    if (signal.aborted) return;
    if (!recent.some(row => row.participant_code === person.participant_code)) recent.unshift({ ...person, savedAt: new Date() });
    draft = null; $('register-form').reset(); renderRecent();
    message('register-message', 'Cadastro confirmado. Atualize a lista no menu VR.', 'success');
    void loadRoster();
  } catch (error) {
    if (signal.aborted) return;
    if (error.code === 'session') { signOut(error.message); return; }
    message('register-message', readable(error), 'error');
  } finally { if (!signal.aborted) { registering = false; $('register-submit').disabled = false; $('participant-name').disabled = false; $('participant-name').focus(); } }
});

$('refresh').addEventListener('click', () => { void loadRoster(); });
$('check-connection').addEventListener('click', async () => {
  if (!client || checkingConnection) return;
  checkingConnection = true; $('check-connection').disabled = true;
  message('connection-check-message', 'Testando o serviço de autenticação...');
  const signal = accountRequest.signal;
  try {
    const result = await client.checkConnection(signal);
    if (!signal.aborted) message('connection-check-message', result, 'success');
  } catch (error) {
    if (!signal.aborted) message('connection-check-message', readable(error), 'error');
  } finally { checkingConnection = false; $('check-connection').disabled = false; }
});
$('search').addEventListener('input', renderRoster);
$('sign-out').addEventListener('click', () => signOut());
setInterval(() => { if (client?.session && !client.isAuthenticated()) signOut('Seu acesso expirou. Entre novamente.'); }, 10000);
try {
  client = new ParticipantClient(config);
  admin = new AdminRepository(client);
  management = new ParticipantManagementUI(admin, { onChanged: result => {
    if (result.deleted) recent = recent.filter(row => row.participant_code !== result.participant_code);
    else recent = recent.map(row => row.participant_code === result.participant_code ? { ...row, display_name: result.display_name } : row);
    renderRecent(); pageMessage(result.deleted ? 'Cadastro excluído.' : 'Nome atualizado. A próxima consulta do menu VR recebe a alteração.'); void loadRoster();
  }, onSessionExpired: text => signOut(text) });
  analysis = new AnalysisUI(client, { onBack: () => { $('dashboard').hidden = false; $('search').focus(); }, onSessionExpired: text => signOut(text) });
} catch (error) { message('login-message', readable(error), 'error'); $('login-submit').disabled = true; }
