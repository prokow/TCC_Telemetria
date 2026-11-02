export class AppError extends Error {
  constructor(code, message) { super(message); this.code = code; }
}

export function validateName(value) {
  const name = String(value ?? '').trim().replace(/\s+/gu, ' ');
  if (!name || [...name].length > 120 || /[\u0000-\u001f\u007f]/u.test(name)) {
    throw new AppError('name', 'Informe um nome com até 120 caracteres.');
  }
  return name;
}

export function parseParticipants(data) {
  if (!Array.isArray(data)) throw new AppError('response', 'Não foi possível validar a lista recebida.');
  return data.map(row => {
    if (!row || typeof row.participant_code !== 'string' || !/^[a-f\d]{32}$/iu.test(row.participant_code)
      || !(row.display_name === null || typeof row.display_name === 'string')) {
      throw new AppError('response', 'A lista contém um cadastro com formato inválido.');
    }
    return { participant_code: row.participant_code, display_name: row.display_name?.trim() || `Participante ${row.participant_code.slice(0, 8)}` };
  });
}

export class ParticipantClient {
  constructor(config, { fetchImpl = globalThis.fetch, now = () => Date.now(), timeoutMs = 20000 } = {}) {
    const url = new URL(config.supabaseUrl);
    if (url.protocol !== 'https:' || url.username || url.password || url.search || url.hash
      || !['', '/', '/rest/v1', '/rest/v1/'].includes(url.pathname)) throw new AppError('config', 'A conexão precisa de uma Project URL HTTPS válida.');
    if (typeof config.publicKey !== 'string' || !config.publicKey || config.publicKey.startsWith('sb_secret_')) throw new AppError('config', 'A chave pública não está configurada.');
    if (!config.publicKey.startsWith('sb_publishable_')) {
      try {
        const payload = JSON.parse(atob(config.publicKey.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
        if (payload.role !== 'anon') throw new Error();
      } catch { throw new AppError('config', 'Use somente uma chave pública anon ou publishable.'); }
    }
    this.origin = url.origin;
    this.key = config.publicKey;
    // O fetch nativo verifica seu receptor no navegador. Chamá-lo como this.fetch
    // sem bind atribui a instância ao this e pode lançar Illegal invocation antes
    // de enviar o pedido; o fetch do Node não revela esse erro nos testes comuns.
    this.fetch = fetchImpl.bind(globalThis);
    this.now = now;
    this.timeoutMs = timeoutMs;
    this.session = null;
  }

  isAuthenticated() { return !!this.session && this.now() < this.session.expiresAt; }
  signOut() { this.session = null; }

  async request(path, { body, authenticated = true, signal, prefer, binary = false, maxBytes = 64 * 1024 * 1024 } = {}) {
    if (authenticated && !this.isAuthenticated()) throw new AppError('session', 'Seu acesso expirou. Entre novamente.');
    const controller = new AbortController();
    const abort = () => controller.abort();
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) controller.abort();
    const timeout = setTimeout(abort, this.timeoutMs);
    const headers = { apikey: this.key, Accept: binary ? '*/*' : 'application/json' };
    if (authenticated) { headers.Authorization = `Bearer ${this.session.accessToken}`; headers['Accept-Profile'] = 'public'; }
    else if (!this.key.startsWith('sb_publishable_')) headers.Authorization = `Bearer ${this.key}`;
    if (body !== undefined) { headers['Content-Type'] = 'application/json'; if (authenticated) headers['Content-Profile'] = 'public'; }
    if (prefer) headers.Prefer = prefer;
    try {
      const response = await this.fetch(`${this.origin}${path}`, {
        method: body === undefined ? 'GET' : 'POST', headers,
        ...(body === undefined ? {} : { body: JSON.stringify(body) }), signal: controller.signal,
        cache: 'no-store', credentials: 'omit', redirect: 'error',
      });
      if (controller.signal.aborted) throw new AppError(signal?.aborted ? 'cancelled' : 'timeout',
        signal?.aborted ? 'A consulta foi interrompida.' : 'O Supabase não respondeu a tempo. Tente verificar a conexão novamente.');
      if (response.ok && binary) {
        const length = Number(response.headers.get('content-length'));
        if (Number.isFinite(length) && length > maxBytes) throw new AppError('size', 'O arquivo excede o limite permitido para download.');
        const blob = await response.blob();
        if (controller.signal.aborted) throw new AppError('cancelled', 'Download interrompido.');
        if (blob.size > maxBytes) throw new AppError('size', 'O arquivo excede o limite permitido para download.');
        return blob;
      }
      let data;
      try { data = await response.json(); } catch { throw new AppError('response', 'O serviço não retornou uma resposta válida.'); }
      if (!response.ok) {
        if (data?.message === 'PARTICIPANT_HAS_TELEMETRY') throw new AppError('history', 'Este cadastro possui sessões ou arquivos de telemetria e não pode ser excluído pelo painel.');
        if (data?.message === 'PARTICIPANT_NOT_FOUND') throw new AppError('missing', 'O cadastro não foi encontrado. Atualize a lista.');
        if (data?.code === 'PGRST202' || data?.code === 'PGRST205') throw new AppError('setup', 'A API solicitada não está disponível. Confira as migrações SQL do projeto.');
        if (data?.error_code === 'invalid_credentials') throw new AppError('credentials', 'E-mail ou senha incorretos.');
        if (data?.error_code === 'email_not_confirmed') throw new AppError('confirmation', 'Confirme o e-mail da sua conta antes de entrar.');
        if (response.status === 429) throw new AppError('limit', 'Muitas tentativas. Aguarde um pouco e tente novamente.');
        if (authenticated && response.status === 401) throw new AppError('session', 'Seu acesso expirou ou foi revogado. Entre novamente.');
        if (response.status === 401 || response.status === 403) {
          const registering = body !== undefined && path.startsWith('/rest/v1/participants?');
          throw new AppError('permission', registering
            ? 'O login funcionou, mas esta conta ainda não está autorizada a cadastrar participantes no Supabase.'
            : 'Sua conta não tem permissão para esta operação. Confira as permissões no Supabase.');
        }
        throw new AppError('server', 'Não foi possível concluir a operação. Tente novamente.');
      }
      return data;
    } catch (error) {
      if (error instanceof AppError) throw error;
      if (signal?.aborted) throw new AppError('cancelled', 'A consulta foi interrompida.');
      if (controller.signal.aborted) throw new AppError('timeout', 'O Supabase não respondeu a tempo. Confira se o projeto está ativo e tente novamente.');
      throw new AppError('network', `O navegador não conseguiu acessar ${new URL(this.origin).hostname}. Use “Verificar conexão” abaixo; não é necessário digitar a chave novamente.`);
    } finally { clearTimeout(timeout); signal?.removeEventListener('abort', abort); }
  }

  async checkConnection(signal) {
    try {
      await this.request('/auth/v1/health', { authenticated: false, signal });
      return 'O serviço de autenticação respondeu. Agora tente entrar com seu e-mail e sua senha.';
    } catch (error) {
      if (error instanceof AppError && error.code === 'permission')
        throw new AppError('key', 'O Supabase recusou a chave pública ou o acesso ao projeto. Confira a Publishable key no painel do mesmo projeto.');
      if (error instanceof AppError && error.code === 'network')
        throw new AppError('network', `A chave já está gravada, mas o navegador não alcançou ${new URL(this.origin).hostname}. Pressione F12, abra Console e informe a mensagem em vermelho, sem enviar senha ou tokens.`);
      throw error;
    }
  }

  async signIn(email, password, signal) {
    this.signOut();
    email = String(email ?? '').trim();
    if (!email.includes('@') || !password) throw new AppError('input', 'Preencha seu e-mail e sua senha.');
    const data = await this.request('/auth/v1/token?grant_type=password', { body: { email, password }, authenticated: false, signal });
    if (signal?.aborted) throw new AppError('cancelled', 'Entrada interrompida.');
    if (typeof data?.access_token !== 'string' || !Number.isFinite(data.expires_in) || data.expires_in <= 0
      || typeof data.user?.id !== 'string') throw new AppError('response', 'Não foi possível validar o acesso.');
    this.session = { accessToken: data.access_token, expiresAt: this.now() + data.expires_in * 1000, userId: data.user.id };
  }

  async listParticipants(signal) {
    const rows = [], seen = new Set();
    let cursor = null;
    while (true) {
      const params = new URLSearchParams({ select: 'participant_code,display_name', order: 'participant_code.asc', limit: '100' });
      if (cursor) params.set('participant_code', `gt.${cursor}`);
      const page = parseParticipants(await this.request(`/rest/v1/participants?${params}`, { signal }));
      if (signal?.aborted) throw new AppError('cancelled', 'Atualização interrompida.');
      if (!page.length) return rows;
      for (const row of page) {
        if (seen.has(row.participant_code) || rows.length >= 10000) throw new AppError('response', 'Não foi possível completar a lista. Atualize para tentar novamente.');
        seen.add(row.participant_code); rows.push(row);
      }
      cursor = page.at(-1).participant_code;
    }
  }

  async registerParticipant(displayName, participantCode, signal) {
    const name = validateName(displayName);
    if (!/^[a-f\d]{32}$/u.test(participantCode)) throw new AppError('id', 'Não foi possível preparar o cadastro.');
    const params = new URLSearchParams({ select: 'participant_code,display_name', on_conflict: 'participant_code' });
    let rows = parseParticipants(await this.request(`/rest/v1/participants?${params}`, {
      body: { participant_code: participantCode, display_name: name }, signal,
      prefer: 'resolution=ignore-duplicates,return=representation',
    }));
    if (signal?.aborted) throw new AppError('cancelled', 'Cadastro interrompido.');
    // Um retry após timeout conserva o UUID. DO NOTHING pode retornar []; confirmar pelo mesmo código.
    if (!rows.length) {
      const query = new URLSearchParams({ select: 'participant_code,display_name', participant_code: `eq.${participantCode}` });
      rows = parseParticipants(await this.request(`/rest/v1/participants?${query}`, { signal }));
    }
    if (signal?.aborted) throw new AppError('cancelled', 'Cadastro interrompido.');
    if (rows.length !== 1 || rows[0].participant_code !== participantCode || rows[0].display_name !== name)
      throw new AppError('confirmation', 'O cadastro ainda não foi confirmado. Tente novamente mantendo o mesmo nome.');
    return rows[0];
  }
}
