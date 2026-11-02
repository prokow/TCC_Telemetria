import { AppError, validateName, parseParticipants } from './client.js';

const code = /^[a-f\d]{32}$/u;
const uuid = /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/iu;
const invalid = () => new AppError('admin-response', 'Não foi possível validar a resposta administrativa. Atualize e tente novamente.');
const count = value => { if (!Number.isSafeInteger(value) || value < 0) throw invalid(); return value; };
const requireCode = value => { if (typeof value !== 'string' || !code.test(value)) throw invalid(); return value; };

export class AdminRepository {
  constructor(client) { this.client = client; }
  async request(path, body, signal) {
    try { return await this.client.request(path, { ...(body === undefined ? {} : { body }), signal }); }
    catch (error) {
      if (error.code === 'server' || error.code === 'setup') throw new AppError('admin-setup', 'Confira se a migração 009_administracao_frontend.sql foi aplicada e tente novamente.');
      throw error;
    }
  }
  async identity(signal) {
    const data = await this.request('/rest/v1/rpc/telemetry_account_access', {}, signal);
    if (!data || !uuid.test(data.user_id) || data.user_id !== this.client.session?.userId
      || typeof data.is_admin !== 'boolean' || !['admin','operator','none'].includes(data.role)
      || data.is_admin !== (data.role === 'admin')) throw invalid();
    if (!data.is_admin) throw new AppError('permission', 'Sua conta está autenticada, mas não está autorizada como administrador.');
    return { userId: data.user_id, role: data.role };
  }
  async overview(signal) {
    const rows = [], seen = new Set(); let cursor = null;
    while (true) {
      const params = new URLSearchParams({ select: 'participant_code,display_name,created_at,session_count,duration_seconds,attempts,correct_answers,errors', order: 'participant_code.asc', limit: '100' });
      if (cursor) params.set('participant_code', `gt.${cursor}`);
      const data = await this.request(`/rest/v1/admin_participant_overview?${params}`, undefined, signal);
      if (signal?.aborted) throw new AppError('cancelled', 'Consulta interrompida.');
      if (!Array.isArray(data)) throw invalid();
      if (!data.length) return rows;
      const identities = parseParticipants(data);
      for (let i = 0; i < data.length; i++) {
        const entry = data[i], identity = identities[i]; requireCode(identity.participant_code);
        if (seen.has(identity.participant_code) || rows.length >= 10000) throw invalid();
        if (typeof entry.created_at !== 'string' || !Number.isFinite(Date.parse(entry.created_at))
          || typeof entry.duration_seconds !== 'number' || !Number.isFinite(entry.duration_seconds) || entry.duration_seconds < 0) throw invalid();
        rows.push({ ...identity, created_at: entry.created_at, session_count: count(entry.session_count), duration_seconds: entry.duration_seconds,
          attempts: count(entry.attempts), correct_answers: count(entry.correct_answers), errors: count(entry.errors) });
        seen.add(identity.participant_code);
      }
      cursor = identities.at(-1).participant_code;
    }
  }
  async rename(participantCode, displayName, signal) {
    requireCode(participantCode); const name = validateName(displayName);
    const data = await this.request('/rest/v1/rpc/admin_rename_participant', { p_participant_code: participantCode, p_display_name: name }, signal);
    const rows = parseParticipants([data]);
    if (rows[0].participant_code !== participantCode || rows[0].display_name !== name) throw invalid();
    return rows[0];
  }
  async deleteEmpty(participantCode, confirmation, signal) {
    requireCode(participantCode);
    if (confirmation !== participantCode) throw new AppError('confirmation', 'Digite o UUID completo do cadastro para confirmar a exclusão.');
    const data = await this.request('/rest/v1/rpc/admin_delete_empty_participant', { p_participant_code: participantCode }, signal);
    if (data?.participant_code !== participantCode || data.deleted !== true) throw invalid();
    return data;
  }
}

const $ = id => document.getElementById(id);
const readable = error => error instanceof AppError ? error.message : 'Não foi possível concluir a alteração. Tente novamente.';

export class ParticipantManagementUI {
  constructor(repository, { onChanged, onSessionExpired } = {}) {
    this.repository = repository; this.onChanged = onChanged; this.onSessionExpired = onSessionExpired;
    $('manage-form').addEventListener('submit', event => { event.preventDefault(); void this.save(); });
    $('manage-close').addEventListener('click', () => this.close());
    $('manage-delete').addEventListener('click', () => { void this.remove(); });
    $('manage-delete-confirm').addEventListener('input', () => this.buttons());
  }
  close() {
    this.request?.abort(); this.request = null; this.person = null; this.busy = false;
    $('participant-management').hidden = true; $('manage-name').value = ''; $('manage-code').textContent = '';
    $('manage-delete-confirm').value = ''; $('manage-message').textContent = '';
  }
  open(person) {
    this.close(); this.person = person;
    $('manage-code').textContent = person.participant_code; $('manage-name').value = person.display_name;
    $('manage-delete-note').textContent = person.session_count > 0
      ? 'Este participante possui sessões. O histórico e os arquivos serão preservados; é possível alterar o nome.'
      : 'Exclua apenas cadastros que não serão utilizados. O banco também verifica sessões e arquivos antes de excluir.';
    $('participant-management').hidden = false; this.buttons(); $('manage-name').focus();
  }
  buttons() {
    $('manage-name').disabled = this.busy; $('manage-save').disabled = this.busy;
    $('manage-delete-confirm').disabled = this.busy || !!this.person?.session_count;
    $('manage-delete').disabled = this.busy || !this.person || this.person.session_count > 0 || $('manage-delete-confirm').value !== this.person.participant_code;
  }
  async mutate(operation) {
    if (!this.person || this.busy) return;
    const person = this.person, request = new AbortController(); this.request = request; this.busy = true; this.buttons();
    $('manage-message').textContent = 'Confirmando alteração no banco...';
    try {
      const result = await operation(person, request.signal);
      if (request.signal.aborted || this.person !== person) return;
      this.close(); this.onChanged?.(result);
    } catch (error) {
      if (request.signal.aborted || this.person !== person) return;
      if (error.code === 'session') { this.close(); this.onSessionExpired?.(error.message); return; }
      $('manage-message').textContent = readable(error);
    } finally { if (this.person === person && !request.signal.aborted) { this.busy = false; this.buttons(); } }
  }
  save() { const name = $('manage-name').value; return this.mutate((person, signal) => this.repository.rename(person.participant_code, name, signal)); }
  remove() {
    if (!this.person || this.person.session_count > 0) return;
    const confirmation = $('manage-delete-confirm').value;
    return this.mutate((person, signal) => this.repository.deleteEmpty(person.participant_code, confirmation, signal));
  }
}
