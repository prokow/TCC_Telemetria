import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash, webcrypto } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { Worker } from 'node:worker_threads';
import { ParticipantClient } from '../dist/client.js';
import { parseStoredCsv } from '../dist/telemetry-csv.js';
import { TelemetryRepository } from '../dist/telemetry.js';
globalThis.crypto ??= webcrypto;
const id = n => n.toString(16).padStart(32,'0');
const owner='00000000-0000-0000-0000-000000000001';
const session={session_id:id(2),participant_code:id(1)};
const head='ParticipantCode,SessionId,TimeSinceStart,HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ\n';
const csv = head + `${id(1)},${id(2)},0.1,-1,2,3,0,0,0\n`;
const file = text => ({ session_id:id(2),file_type:'head_csv',bucket_id:'vr-telemetry',storage_path:`${owner}/${id(1)}/${id(2)}/Head.csv`,byte_size:Buffer.byteLength(text),sha256:createHash('sha256').update(text).digest('hex') });

test('CSV exige participante e sessao em todas as linhas e aceita posicao negativa', () => {
  const result=parseStoredCsv(csv,id(1),id(2),'head_csv');
  assert.equal(result.totalRows,1); assert.equal(result.rows[0][3],'-1');
  for(const [text,participant,code] of [[csv,id(9),id(2)],[csv,id(1),id(9)],[csv.replace(',0.1,',',NaN,'),id(1),id(2)],[csv.replace(',0.1,',',-1,'),id(1),id(2)],[csv.replace('HeadPosX','Unknown'),id(1),id(2)]]) assert.throws(()=>parseStoredCsv(text,participant,code,'head_csv'));
});
test('CSV de eventos preserva campos com virgulas, aspas e quebras de linha', () => {
  const text=`ParticipantCode,SessionId,TimeSinceStart,EventType,ObjectId,Details\n${id(1)},${id(2)},1,task_error,R1,"texto, com ""aspas""\ne linha"\n`;
  const result=parseStoredCsv(text,id(1),id(2),'events_csv');
  assert.equal(result.totalRows,1); assert.equal(result.rows[0].at(-1),'texto, com "aspas"\ne linha');
  assert.throws(()=>parseStoredCsv(text.replace('e linha"','e linha'),id(1),id(2),'events_csv'));
});
test('previa limita memoria a 200 linhas e conta o arquivo completo', () => {
  const text=head+Array.from({length:600},(_,i)=>`${id(1)},${id(2)},${i},0,0,0,0,0,0\n`).join('');
  const result=parseStoredCsv(text,id(1),id(2),'head_csv'); assert.equal(result.totalRows,600); assert.equal(result.rows.length,200);
});
test('download usa autenticacao, confere tamanho e SHA-256 e nao aceita outro UUID ou URL', async () => {
  let observed;
  const client=new ParticipantClient({supabaseUrl:'https://example.invalid',publicKey:'sb_publishable_fixture'},{fetchImpl:async(url,options)=>{observed={url,options};return new Response(csv,{headers:{'Content-Type':'text/csv'}});}});
  client.session={accessToken:'user-token',expiresAt:Date.now()+60000}; const repository=new TelemetryRepository(client);
  assert.equal(await(await repository.download(session,file(csv))).text(),csv);
  assert(observed.url.startsWith('https://example.invalid/storage/v1/object/authenticated/'));
  assert.equal(observed.options.headers.Authorization,'Bearer user-token'); assert.equal(observed.options.credentials,'omit'); assert.equal(observed.options.redirect,'error');
  await assert.rejects(()=>repository.download(session,{...file(csv),storage_path:`${owner}/${id(9)}/${id(2)}/Head.csv`}),error=>error.code==='file-link');
  await assert.rejects(()=>repository.download(session,{...file(csv),storage_path:'https://other.invalid/Head.csv'}),error=>error.code==='file-link');
  await assert.rejects(()=>repository.download(session,{...file(csv),sha256:'0'.repeat(64)}),error=>error.code==='integrity');
  await assert.rejects(()=>repository.download(session,{...file(csv),byte_size:1}),error=>error.code==='integrity');
});
test('objetos privados respeitam acesso expirado e limite antes de interpretar o arquivo', async () => {
  const client=new ParticipantClient({supabaseUrl:'https://example.invalid',publicKey:'sb_publishable_fixture'},{fetchImpl:async()=>new Response(csv,{headers:{'Content-Length':'70000000'}})});
  const repo=new TelemetryRepository(client); await assert.rejects(()=>repo.download(session,file(csv)),error=>error.code==='session');
  client.session={accessToken:'fixture',expiresAt:Date.now()+60000}; await assert.rejects(()=>repo.download(session,file(csv)),error=>error.code==='size');
});
test('Worker do frontend interpreta os quatro arquivos reais gerados pelo C#', async t => {
  let metadata;
  const folder=new URL('../../../Temp/TelemetryUploadInterop/',import.meta.url);
  try{metadata=JSON.parse(await readFile(new URL('Metadata.json',folder),'utf8'));}catch(error){if(error.code==='ENOENT'){t.skip('Execute primeiro Tools/ParticipantIntegrationChecks/Run.ps1 para gerar os arquivos de interoperabilidade.');return;}throw error;}
  for(const entry of metadata.p_files){
    const bytes=await readFile(new URL(entry.storage_path.split('/').at(-1),folder));
    assert.equal(createHash('sha256').update(bytes).digest('hex'),entry.sha256);
    const worker=new Worker(new URL('./worker-harness.mjs',import.meta.url));
    try{
      const pending=new Promise((resolve,reject)=>{worker.once('message',resolve);worker.once('error',reject);});
      worker.postMessage({kind:entry.file_type==='session_json'?'json':'csv',file:new Blob([bytes]),participantCode:metadata.p_session.participant_code,sessionId:metadata.p_session.session_id,fileType:entry.file_type});
      const result=await pending; assert.equal(result.error,undefined,entry.file_type);
      if(result.report){assert.equal(result.report.session.session_id,metadata.p_session.session_id);assert.equal(result.report.samples.head,1);}
      else{assert.equal(result.preview.rows[0][0],metadata.p_session.participant_code);assert.equal(result.preview.rows[0][1],metadata.p_session.session_id);if(entry.file_type==='events_csv')assert.equal(result.preview.totalRows,2);}
    }finally{await worker.terminate();}
  }
});
