import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { ParticipantClient, AppError } from '../dist/client.js';
import { AdminRepository, ParticipantManagementUI } from '../dist/admin.js';

const uuid = '11111111-2222-4333-8444-555555555555';
const id = n => n.toString(16).padStart(32,'0');
const response = (data, status=200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type':'application/json' } });
const row = (n=1, patch={}) => ({ participant_code:id(n), display_name:'Nome', created_at:'2026-11-01T12:00:00Z', session_count:2, duration_seconds:120, attempts:5, correct_answers:3, errors:2, ...patch });
function repository(fetchImpl) {
  const client = new ParticipantClient({ supabaseUrl:'https://example.invalid', publicKey:'sb_publishable_fixture' }, { fetchImpl });
  client.session = { accessToken:'user-fixture', userId:uuid, expiresAt:Date.now()+60000 };
  return { client, admin:new AdminRepository(client) };
}
test('identidade administrativa vem da RPC e precisa corresponder ao usuario autenticado', async () => {
  let request;
  const { admin } = repository(async (url, options) => { request={url,...options}; return response({ user_id:uuid,role:'admin',is_admin:true }); });
  assert.deepEqual(await admin.identity(), { userId:uuid,role:'admin' });
  assert.match(request.url, /\/rpc\/telemetry_account_access$/); assert.equal(request.headers.Authorization,'Bearer user-fixture');
  assert.equal(request.credentials,'omit'); assert.equal(request.method,'POST');
});
test('operador comum, usuario desconhecido e identidade divergente nao recebem acesso administrativo', async () => {
  for (const data of [ {user_id:uuid,role:'operator',is_admin:false}, {user_id:uuid,role:'none',is_admin:false},
    {user_id:'99999999-2222-4333-8444-555555555555',role:'admin',is_admin:true}, {user_id:uuid,role:'operator',is_admin:true} ]) {
    const { admin } = repository(async () => response(data)); await assert.rejects(() => admin.identity(), AppError);
  }
});
test('visao consolidada pagina por UUID e preserva contagens sem calculos duplicados', async () => {
  const pages=[[row(1)],[row(2,{session_count:0,duration_seconds:0,attempts:0,correct_answers:0,errors:0})],[]], requests=[];
  const { admin }=repository(async url => { requests.push(url); return response(pages.shift()); });
  const rows=await admin.overview(); assert.equal(rows.length,2); assert.equal(rows[0].attempts,5); assert.equal(rows[1].attempts,0);
  assert.equal(new URL(requests[1]).searchParams.get('participant_code'),`gt.${id(1)}`);
});
test('resumo invalido e pagina repetida sao recusados sem divulgar resultado parcial', async () => {
  for (const patch of [{attempts:-1},{errors:0.5},{session_count:9007199254740992},{duration_seconds:-1},{created_at:'invalid'}]) {
    const { admin }=repository(async () => response([row(1,patch)])); await assert.rejects(() => admin.overview(),AppError);
  }
  const { admin }=repository(async () => response([row()])); await assert.rejects(() => admin.overview(),AppError);
});
test('renomear envia o UUID e nome normalizado e exige ACK correspondente', async () => {
  let body;
  const { admin }=repository(async (url,options) => { assert.match(url,/admin_rename_participant$/); body=JSON.parse(options.body); return response({participant_code:id(1),display_name:'Ana Maria'}); });
  assert.equal((await admin.rename(id(1),' Ana   Maria ')).participant_code,id(1));
  assert.deepEqual(body,{p_participant_code:id(1),p_display_name:'Ana Maria'});
  await assert.rejects(() => admin.rename('invalid','Nome'),AppError);
  await assert.rejects(() => admin.rename(id(1),' '),AppError);
  const other=repository(async () => response({participant_code:id(2),display_name:'Nome'})); await assert.rejects(() => other.admin.rename(id(1),'Nome'),AppError);
});
test('exclusao exige confirmacao do UUID e verifica o retorno do banco', async () => {
  let calls=0;
  const { admin }=repository(async (url,options) => { calls++; assert.match(url,/admin_delete_empty_participant$/); assert.deepEqual(JSON.parse(options.body),{p_participant_code:id(1)}); return response({participant_code:id(1),deleted:true}); });
  await assert.rejects(() => admin.deleteEmpty(id(1),'Nome'), error => error.code==='confirmation'); assert.equal(calls,0);
  assert.equal((await admin.deleteEmpty(id(1),id(1))).deleted,true);
});
test('recusa, historico existente e migracao ausente produzem mensagens seguras', async () => {
  for (const [data,status,expected] of [[{message:'secret-fixture'},403,'permission'], [{message:'PARTICIPANT_HAS_TELEMETRY'},400,'history'],
    [{code:'PGRST202',message:'secret-fixture'},404,'admin-setup']]) {
    const {admin}=repository(async () => response(data,status));
    await assert.rejects(() => admin.deleteEmpty(id(1),id(1)), error => error.code===expected && !error.message.includes('secret-fixture'));
  }
});
test('sessao expirada impede operacao administrativa antes de chamar a rede', async () => {
  let calls=0; const {client,admin}=repository(async () => { calls++; return response({}); }); client.signOut();
  await assert.rejects(() => admin.identity(), error => error.code==='session'); assert.equal(calls,0);
});

class Element {
  constructor() { this.listeners=new Map(); this.value=''; this.textContent=''; this.hidden=false; this.disabled=false; }
  addEventListener(name, fn) { this.listeners.set(name,fn); }
  focus() {}
}
const html=await readFile(new URL('../dist/index.html',import.meta.url),'utf8');
function fixture(t, overrides={}) {
  const elements=new Map([...html.matchAll(/\bid="([^"]+)"/g)].map(match => [match[1],new Element()]));
  globalThis.document={getElementById:name=>{assert(elements.has(name),name); return elements.get(name);}};
  const changes=[], expired=[];
  const ui=new ParticipantManagementUI({rename:async (code,name)=>({participant_code:code,display_name:name}),deleteEmpty:async code=>({participant_code:code,deleted:true}),...overrides}, {onChanged:data=>changes.push(data),onSessionExpired:message=>expired.push(message)});
  t.after(()=>ui.close()); return {ui,get:name=>elements.get(name),changes,expired};
}
test('edicao conserva UUID e cadastro com sessoes nao libera exclusao', async t => {
  let deleted=false;
  const {ui,get,changes}=fixture(t,{deleteEmpty:async()=>{deleted=true;}}); ui.open(row());
  assert.equal(get('manage-code').textContent,id(1)); assert.equal(get('manage-delete').disabled,true);
  await ui.remove(); assert.equal(deleted,false); get('manage-name').value='<img src=x>'; await ui.save();
  assert.equal(changes[0].display_name,'<img src=x>'); assert.equal(changes[0].participant_code,id(1)); assert.equal(get('participant-management').hidden,true);
});
test('fechar edicao cancela requisicao e ignora resposta atrasada', async t => {
  let resolve, signal;
  const {ui,get,changes}=fixture(t,{rename:(_,__,token)=>{signal=token; return new Promise(done=>{resolve=done;});}});
  ui.open(row()); const pending=ui.save(); ui.close(); resolve({participant_code:id(1),display_name:'Antigo'}); await pending;
  assert.equal(signal.aborted,true); assert.equal(changes.length,0); assert.equal(get('manage-code').textContent,'');
});
test('falha de alteracao preserva formulario e nao anuncia sucesso', async t => {
  const {ui,get,changes}=fixture(t,{rename:async()=>{throw new AppError('permission','Sem acesso');}}); ui.open(row()); await ui.save();
  assert.equal(changes.length,0); assert.equal(get('manage-message').textContent,'Sem acesso'); assert.equal(get('manage-save').disabled,false);
});
test('exclusao de cadastro vazio e encerramento por expiracao limpam a identidade', async t => {
  const {ui,get,changes,expired}=fixture(t,{rename:async()=>{throw new AppError('session','Expirou');}});
  ui.open(row(1,{session_count:0})); get('manage-delete-confirm').value=id(1); ui.buttons(); assert.equal(get('manage-delete').disabled,false);
  await ui.remove(); assert.equal(changes[0].deleted,true);
  ui.open(row()); await ui.save(); assert.equal(expired.length,1); assert.equal(get('manage-code').textContent,'');
});
