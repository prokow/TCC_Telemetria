import { AppError } from './client.js';
import { TelemetryRepository, accuracy, summarize, csvSessions } from './telemetry.js';

const $ = id => document.getElementById(id);
const element = (tag, className = '', text) => { const node = document.createElement(tag); node.className = className; if (text !== undefined) node.textContent = text; return node; };
const integer = value => new Intl.NumberFormat('pt-BR').format(value);
const percent = value => value === null ? '—' : new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 }).format(value) + '%';
const timestamp = value => value ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) : 'Sem encerramento';
export function duration(value) { const seconds = Math.round(value); return seconds >= 3600 ? `${Math.floor(seconds / 3600)}h ${Math.floor(seconds % 3600 / 60)}min` : `${Math.floor(seconds / 60)}min ${seconds % 60}s`; }
const readable = error => error instanceof AppError ? error.message : 'Não foi possível carregar a análise. Tente novamente.';

export class AnalysisUI {
  constructor(client, { onBack, onSessionExpired, workerFactory = () => new Worker(new URL('./telemetry-worker.js', import.meta.url), { type: 'module' }) } = {}) {
    this.repository = new TelemetryRepository(client); this.onBack = onBack; this.onSessionExpired = onSessionExpired; this.workerFactory = workerFactory;
    this.person = null; this.remote = null; this.locals = new Map(); this.source = 'supabase'; this.detail = null; this.sessionPage = 0; this.eventPage = 0;
    $('analysis-back').addEventListener('click', () => { this.reset(); this.onBack?.(); });
    $('analysis-refresh').addEventListener('click', () => { void this.load(); });
    $('analysis-source').addEventListener('change', () => { this.source = $('analysis-source').value; this.closeDetail(); this.sessionPage = 0; this.render(); });
    $('analysis-file').addEventListener('change', () => { const file = $('analysis-file').files[0]; $('analysis-file').value = ''; if (file) this.import(file); });
    $('analysis-export').addEventListener('click', () => this.export());
    $('detail-close').addEventListener('click', () => this.closeDetail());
    $('storage-preview-close').addEventListener('click', () => { $('storage-preview').hidden = true; });
    $('event-search').addEventListener('input', () => { this.eventPage = 0; this.renderEvents(); });
    for (const [id, delta] of [['sessions-prev', -1], ['sessions-next', 1]]) $(id).addEventListener('click', () => { this.sessionPage += delta; this.renderSessions(); });
    for (const [id, delta] of [['events-prev', -1], ['events-next', 1]]) $(id).addEventListener('click', () => { this.eventPage += delta; this.renderEvents(); });
    $('analysis-timezone').textContent = `Horários em ${Intl.DateTimeFormat().resolvedOptions().timeZone}.`;
  }
  reset() {
    this.request?.abort(); this.importWorker?.terminate(); this.importWorker = null; this.closeDetail();
    this.person = null; this.remote = null; this.remoteNotice = ''; this.locals.clear(); this.source = 'supabase'; this.sessionPage = 0;
    $('analysis-panel').hidden = true; $('analysis-name').textContent = ''; $('analysis-code').textContent = ''; $('analysis-created').textContent = '';
    $('analysis-message').textContent = ''; $('analysis-file').value = ''; $('analysis-file').disabled = false;
    $('analysis-source').value = 'supabase'; $('analysis-export').disabled = true; $('session-rows').replaceChildren(); $('analysis-chart').replaceChildren();
    for (const id of ['sessions','duration','attempts','correct','errors','accuracy']) $(`metric-${id}`).textContent = '—';
  }
  open(person) {
    this.reset(); this.person = person;
    $('analysis-name').textContent = person.display_name; $('analysis-code').textContent = person.participant_code;
    $('dashboard').hidden = true; $('analysis-panel').hidden = false; $('analysis-name').focus();
    void this.load();
  }
  get sessions() { return this.source === 'supabase' ? this.remote : [...this.locals.values()].map(report => report.session).sort((a,b) => Date.parse(b.started_utc) - Date.parse(a.started_utc)); }
  notice(message) { $('analysis-message').textContent = message; }
  async load() {
    if (!this.person) return;
    this.importWorker?.terminate(); this.importWorker = null; $('analysis-file').disabled = false;
    this.request?.abort(); this.closeDetail(); this.remote = null; this.remoteNotice = ''; this.source = 'supabase'; $('analysis-source').value = 'supabase'; this.sessionPage = 0;
    const request = new AbortController(); this.request = request; const person = this.person;
    $('analysis-refresh').disabled = true; this.render(); this.notice('Consultando sessões no Supabase...');
    try {
      const result = await this.repository.participant(person, request.signal);
      if (request.signal.aborted || this.person !== person || this.request !== request) return;
      this.remote = result.sessions; $('analysis-created').textContent = `Cadastro em ${timestamp(result.createdAt)}`;
      this.render();
    } catch (error) {
      if (request.signal.aborted || this.person !== person || this.request !== request) return;
      if (error.code === 'session') { this.onSessionExpired?.(error.message); return; }
      this.remoteNotice = readable(error);
      if (this.source === 'supabase') this.notice(this.remoteNotice);
    } finally { if (this.request === request) $('analysis-refresh').disabled = false; }
  }
  render() {
    const sessions = this.sessions;
    $('analysis-source-badge').textContent = this.source === 'local' ? 'Arquivo local · não enviado' : 'Supabase';
    $('analysis-export').disabled = !sessions?.length;
    const totals = summarize(sessions || []);
    const metrics = { sessions: sessions === null ? '—' : integer(totals.sessions), duration: sessions?.length ? duration(totals.duration) : '—', attempts: sessions?.length ? integer(totals.attempts) : '—', correct: sessions?.length ? integer(totals.correct) : '—', errors: sessions?.length ? integer(totals.errors) : '—', accuracy: percent(totals.accuracy) };
    for (const [id, value] of Object.entries(metrics)) $(`metric-${id}`).textContent = value;
    this.notice(this.source === 'local' ? 'Análise dos arquivos abertos neste acesso. Estes dados não foram enviados por este navegador ao Supabase.' : sessions === null ? this.remoteNotice || 'Aguardando consulta autorizada ao banco.' : sessions.length ? 'Sessões consultadas no banco. Selecione Detalhes para explorar atividades, eventos e arquivos do Storage.' : 'Nenhuma sessão recebida do Supabase. Finalize uma coleta no Unity e confira o envio. Arquivos locais também podem ser analisados com Analisar Session.json.');
    if (!totals.consistent) this.notice($('analysis-message').textContent + ' Há divergência entre tentativas e resultados; a taxa total não foi calculada.');
    this.renderSessions(); this.renderChart();
  }
  renderSessions() {
    const sessions = this.sessions || [], pages = Math.max(1, Math.ceil(sessions.length / 25));
    this.sessionPage = Math.max(0, Math.min(this.sessionPage, pages - 1)); $('session-rows').replaceChildren();
    for (const session of sessions.slice(this.sessionPage * 25, (this.sessionPage + 1) * 25)) {
      const row = element('tr'), identity = element('td'); identity.append(element('strong', '', timestamp(session.started_utc)), element('code', 'block-code', session.session_id));
      row.append(identity);
      for (const text of [session.ended_utc ? 'Encerrada' : 'Sem encerramento', duration(session.duration_seconds), integer(session.attempts), integer(session.correct_answers), integer(session.errors), percent(session.correct_answers + session.errors === session.attempts ? accuracy(session.attempts, session.correct_answers) : null)]) row.append(element('td', '', text));
      const action = element('td'), button = element('button', 'secondary', 'Detalhes'); button.type = 'button'; button.setAttribute('aria-label', `Analisar sessão de ${timestamp(session.started_utc)}`);
      button.addEventListener('click', () => { void this.openSession(session); }); action.append(button); row.append(action); $('session-rows').append(row);
    }
    $('sessions-count').textContent = this.sessions === null ? '—' : integer(sessions.length);
    $('sessions-empty').hidden = sessions.length > 0;
    $('sessions-empty').textContent = this.sessions === null ? 'Aguardando consulta autorizada ao banco.' : this.source === 'local' ? 'Abra o Session.json do participante para começar.' : 'Nenhuma sessão disponível nesta origem.';
    $('sessions-page').textContent = sessions.length ? `${this.sessionPage + 1} / ${pages}` : '—';
    $('sessions-prev').disabled = this.sessionPage === 0; $('sessions-next').disabled = this.sessionPage >= pages - 1;
  }
  renderChart() {
    $('analysis-chart').replaceChildren();
    const sessions = (this.sessions || []).slice(0, 8).reverse(), max = Math.max(1, ...sessions.map(session => session.correct_answers + session.errors));
    if (!sessions.length) { $('analysis-chart').append(element('p', 'section-note', 'A evolução aparecerá quando houver sessões nesta origem.')); return; }
    for (const session of sessions) {
      const row = element('div', 'chart-row'), track = element('div', 'chart-track');
      for (const [type, value] of [['correct', session.correct_answers], ['error', session.errors]]) { const bar = element('span', `chart-bar ${type}`); bar.style.width = `${value / max * 100}%`; track.append(bar); }
      track.setAttribute('role', 'img'); track.setAttribute('aria-label', `${session.correct_answers} acertos e ${session.errors} erros`);
      row.append(element('span', 'chart-date', timestamp(session.started_utc)), track, element('span', 'chart-values', `${session.correct_answers} / ${session.errors}`)); $('analysis-chart').append(row);
    }
  }
  closeDetail() {
    this.fileRequest?.abort(); this.remoteFileWorker?.terminate(); this.remoteFileWorker = null;
    $('storage-preview').hidden = true; $('storage-preview-summary').textContent = '';
    $('storage-preview-head').replaceChildren(); $('storage-preview-rows').replaceChildren();
    this.detailRequest?.abort(); this.detail = null; $('session-detail').hidden = true;
    for (const id of ['detail-code','detail-message','sample-summary','events-page']) $(id).textContent = '';
    for (const id of ['detail-attributes','task-rows','event-rows','file-list']) $(id).replaceChildren();
    $('event-search').value = ''; this.eventPage = 0;
  }
  async openSession(session) {
    this.closeDetail();
    const person = this.person, request = new AbortController(); this.detailRequest = request;
    $('session-detail').hidden = false; $('detail-code').textContent = session.session_id; $('detail-message').textContent = 'Consultando eventos e arquivos...';
    try {
      const detail = this.source === 'local' ? this.locals.get(session.session_id) : await this.repository.session(session, request.signal);
      if (request.signal.aborted || this.person !== person || this.detailRequest !== request) return;
      this.detail = detail; this.renderDetail();
    } catch (error) {
      if (request.signal.aborted || this.person !== person) return;
      if (error.code === 'session') { this.onSessionExpired?.(error.message); return; }
      $('detail-message').textContent = readable(error);
    }
  }
  renderDetail() {
    const { session, tasks, events, files, samples } = this.detail;
    $('detail-message').textContent = session.correct_answers + session.errors === session.attempts ? `${integer(events.length)} eventos disponíveis nesta sessão.` : 'As contagens desta sessão divergem. Confira os eventos antes de interpretar os resultados.';
    if (tasks.reduce((total, task) => total + task.attempts, 0) !== session.attempts) $('detail-message').textContent += ' Os eventos de avaliação disponíveis não correspondem ao total de tentativas do resumo.';
    for (const [label, value] of [['Início', timestamp(session.started_utc)],['Encerramento', timestamp(session.ended_utc)],['Versão da aplicação', session.application_version || 'Não informada'],['Versão do formato', String(session.schema_version)],['Espaço de coordenadas', session.coordinate_space || 'Não informado'],['Origem', session.source === 'local' ? 'Arquivo local' : 'Supabase']]) {
      const item = element('div'); item.append(element('dt', '', label), element('dd', '', value)); $('detail-attributes').append(item);
    }
    for (const task of tasks) { const row = element('tr'); for (const value of [task.title, integer(task.attempts), integer(task.correct), integer(task.errors), percent(accuracy(task.attempts, task.correct))]) row.append(element('td', '', value)); $('task-rows').append(row); }
    $('tasks-empty').hidden = tasks.length > 0;
    $('sample-summary').textContent = samples ? `${integer(samples.head)} amostras do headset; ${integer(samples.controllers)} dos controles. Controle esquerdo rastreado em ${percent(samples.left)} das amostras; direito em ${percent(samples.right)}.` : 'As amostras de pose não estão no resumo das tabelas. Analise o Session.json do Unity para consultar contagens e disponibilidade de rastreio.';
    for (const file of files) {
      const titles = { session_json: 'Sessão completa · JSON', head_csv: 'Headset · CSV', controllers_csv: 'Controles · CSV', events_csv: 'Eventos · CSV' };
      const item = element('li'); item.append(element('strong', '', titles[file.file_type] || file.file_type), element('span', '', file.byte_size === null ? 'Tamanho não informado' : `${integer(file.byte_size)} bytes`));
      for (const [mode, label] of [['download','Baixar arquivo'],['preview',file.file_type === 'session_json' ? 'Analisar JSON do Storage' : 'Ver amostras do CSV']]) {
        const button = element('button','secondary',label); button.type = 'button'; button.disabled = !file.sha256;
        button.addEventListener('click', () => { void this.openStoredFile(file, mode); }); item.append(button);
      }
      item.append(element('code','',file.storage_path)); $('file-list').append(item);
    }
    $('files-empty').hidden = files.length > 0;
    $('files-empty').textContent = session.source === 'local' ? 'Arquivo analisado apenas neste navegador; não enviado ao banco ou Storage.' : 'Nenhum arquivo indexado no Supabase para esta sessão.';
    this.renderEvents();
  }
  renderEvents() {
    const query = $('event-search').value.trim().toLocaleLowerCase('pt-BR');
    const events = (this.detail?.events || []).filter(event => `${event.event_type} ${event.task_key} ${event.object_id} ${event.details}`.toLocaleLowerCase('pt-BR').includes(query));
    const pages = Math.max(1, Math.ceil(events.length / 50)); this.eventPage = Math.max(0, Math.min(this.eventPage, pages - 1)); $('event-rows').replaceChildren();
    for (const event of events.slice(this.eventPage * 50, (this.eventPage + 1) * 50)) { const row = element('tr'); const elapsed = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 }).format(event.time_since_start) + ' s'; for (const value of [elapsed, event.event_type, event.task_key || event.object_id || '—', event.details || '—']) row.append(element('td', '', value)); $('event-rows').append(row); }
    $('events-empty').hidden = events.length > 0; $('events-empty').textContent = query ? 'Nenhum evento corresponde à busca.' : 'Nenhum evento disponível.';
    $('events-page').textContent = events.length ? `${this.eventPage + 1} / ${pages} · ${integer(events.length)} eventos` : '—';
    $('events-prev').disabled = this.eventPage === 0; $('events-next').disabled = this.eventPage >= pages - 1;
  }
  async openStoredFile(file, mode) {
    if (!this.detail || !this.person) return;
    this.fileRequest?.abort(); this.remoteFileWorker?.terminate(); this.remoteFileWorker = null;
    const request = new AbortController(), detail = this.detail, person = this.person; this.fileRequest = request;
    const current = () => !request.signal.aborted && this.detail === detail && this.person === person && this.fileRequest === request;
    $('detail-message').textContent = 'Baixando arquivo privado e verificando integridade...';
    try {
      const blob = await this.repository.download(detail.session, file, request.signal);
      if (!current()) return;
      if (mode === 'download') {
        this.downloadBlob(blob, file.storage_path.split('/').at(-1)); $('detail-message').textContent = 'Arquivo conferido por SHA-256. Download iniciado.'; return;
      }
      const worker = this.workerFactory(); this.remoteFileWorker = worker;
      worker.onmessage = ({ data }) => {
        if (!current()) { worker.terminate(); return; }
        if (data.error) $('detail-message').textContent = data.error;
        else if (data.preview) {
          $('storage-preview').hidden = false; $('storage-preview-title').textContent = file.storage_path.split('/').at(-1);
          $('storage-preview-summary').textContent = `${integer(data.preview.totalRows)} registros no arquivo. Exibindo os primeiros ${integer(data.preview.rows.length)}; baixe o CSV para consultar todos. UUIDs e SHA-256 conferidos.`;
          const header = element('tr');
          for (const label of data.preview.headers) { const cell = element('th','',label); cell.setAttribute('scope','col'); header.append(cell); }
          $('storage-preview-head').replaceChildren(header); $('storage-preview-rows').replaceChildren();
          for (const values of data.preview.rows) { const row = element('tr'); for (const value of values) row.append(element('td','',value)); $('storage-preview-rows').append(row); }
          $('detail-message').textContent = 'CSV do Storage analisado. Cada linha pertence ao participante e à sessão escolhidos.';
        } else if (data.report && data.report.session.session_id === detail.session.session_id && data.report.session.participant_code === person.participant_code && data.report.session.attempts === detail.session.attempts && data.report.session.correct_answers === detail.session.correct_answers && data.report.session.errors === detail.session.errors) {
          this.detail.samples = data.report.samples; $('detail-attributes').replaceChildren(); $('task-rows').replaceChildren(); $('file-list').replaceChildren(); this.renderDetail();
          $('detail-message').textContent += ' Amostras conferidas no JSON privado do Storage.';
        } else $('detail-message').textContent = 'O arquivo não corresponde ao resumo da sessão escolhida.';
        worker.terminate(); if (this.remoteFileWorker === worker) this.remoteFileWorker = null;
      };
      worker.onerror = () => { if (current()) $('detail-message').textContent = 'Não foi possível analisar esse arquivo no navegador.'; worker.terminate(); };
      worker.postMessage({ kind: file.file_type === 'session_json' ? 'json' : 'csv', file: blob, participantCode: person.participant_code, sessionId: detail.session.session_id, fileType: file.file_type });
    } catch (error) {
      if (!current()) return;
      if (error.code === 'session') { this.onSessionExpired?.(error.message); return; }
      $('detail-message').textContent = readable(error);
    }
  }
  downloadBlob(blob, name) {
    const link = element('a'), url = URL.createObjectURL(blob); link.href = url; link.download = name;
    document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  import(file) {
    if (!this.person) return;
    if (file.size > 64 * 1024 * 1024) { this.notice('Selecione um Session.json de até 64 MB.'); return; }
    this.importWorker?.terminate(); this.importWorker = null; $('analysis-file').disabled = false; let worker;
    try { worker = this.workerFactory(); }
    catch { this.notice('Não foi possível abrir a análise de arquivo neste navegador. Tente novamente em um navegador atualizado.'); return; }
    const person = this.person; this.importWorker = worker;
    $('analysis-file').disabled = true; this.notice('Analisando arquivo local...');
    const finish = () => { worker.terminate(); if (this.importWorker === worker) { this.importWorker = null; $('analysis-file').disabled = false; } };
    worker.onmessage = ({ data }) => {
      if (this.person !== person || this.importWorker !== worker) { finish(); return; }
      if (data.error) this.notice(data.error);
      else if (!this.locals.has(data.report.session.session_id) && this.locals.size >= 20) this.notice('Limite de 20 arquivos locais por participante neste acesso. Volte e abra a análise novamente para limpar os arquivos.');
      else { this.locals.set(data.report.session.session_id, data.report); this.source = 'local'; $('analysis-source').value = 'local'; this.sessionPage = 0; this.closeDetail(); this.render(); void this.openSession(data.report.session); }
      finish();
    };
    worker.onerror = () => { if (this.person === person && this.importWorker === worker) this.notice('Não foi possível analisar o arquivo. Tente novamente em um navegador atualizado.'); finish(); };
    try { worker.postMessage({ file, participantCode: person.participant_code }); }
    catch { this.notice('Não foi possível abrir o arquivo local. Selecione o Session.json novamente.'); finish(); }
  }
  export() {
    if (!this.person || !this.sessions?.length) return;
    const link = element('a'), url = URL.createObjectURL(new Blob([csvSessions(this.sessions)], { type: 'text/csv;charset=utf-8' }));
    link.href = url; link.download = `sessoes-${this.person.participant_code}-${this.source}.csv`; document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
