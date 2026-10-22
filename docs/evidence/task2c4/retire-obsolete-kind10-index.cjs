// Single-index permanent local-development correction, explicitly owner-approved 2026-10-17.
// Fails closed. No other schema/data/history changes, never deploy or target production.
'use strict';
const fs=require('fs'),path=require('path'),crypto=require('crypto'),cp=require('child_process'),assert=require('node:assert/strict');

// Shared by the live adapter and deterministic offline tests. No retries or inverse DDL.
async function executeCorrection(io){
 const state={commit:'not_attempted',verification:'not_run',outcome:'not_started',evidenceWriteFailed:false};
 let context,stage='preflight';
 try{
  context=await io.prepare();
  stage='mutation';context.inside=await io.mutate(context);
  stage='durable_commit_intent';
  await io.persistIntent(context); // Must fsync successfully BEFORE sending COMMIT.
  stage='commit';state.commit='attempted_outcome_unknown';
  try{await io.commit();state.commit='acknowledged';}catch(e){state.commitError=e.code||e.name;}
  // Terminate the original session; never send ROLLBACK after an uncertain COMMIT.
  stage='close_writer';await io.closeWriter();
  stage='independent_verification';
  try{
   const verified=await io.verify(context);state.verification=verified.state;state.postflight=verified.evidence;
   if(verified.state==='committed')state.outcome='independently_verified_committed';
   else if(verified.state==='unchanged'&&state.commit!=='acknowledged')state.outcome='independently_verified_unchanged';
   else state.outcome='manual_reconciliation_required';
  }catch(e){state.verification=e.name==='AssertionError'?'unexpected':'unavailable';state.verificationError=e.code||e.name;state.outcome='unresolved_outcome_manual_reconciliation';}
 }catch(e){
  state.error={stage,code:e.code||e.name};
  if(state.commit==='not_attempted'){
   try{await io.abort();state.outcome='not_committed_aborted';}catch{state.outcome='precommit_cleanup_unverified';}
  }else state.outcome='unresolved_outcome_manual_reconciliation';
 }finally{
  try{await io.closeWriter();}catch{state.cleanupError=true;}
  try{await io.persistResult(state,context);}catch{state.evidenceWriteFailed=true;}
 }
 state.success=state.outcome==='independently_verified_committed'&&!state.evidenceWriteFailed;
 return state;
}

function assertSingleIndexDelta(before,inside,expected){
 const removed=before.schema.filter(x=>!inside.schema.some(y=>JSON.stringify(x)===JSON.stringify(y)));
 const added=inside.schema.filter(x=>!before.schema.some(y=>JSON.stringify(x)===JSON.stringify(y)));
 assert.deepEqual(removed,[{kind:'index',name:'IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId',definition:expected}]);
 assert.deepEqual(added,[]);assert.equal(inside.schema.length,before.schema.length-1);
}

function assertServerIdentity(server){
 assert.equal(server.db,'EEMOCantilanSDS.DB');assert.equal(server.port,5432);
 assert.ok(['127.0.0.1','::1'].includes(server.address));assert.ok(server.version>=180000&&server.version<190000);
}
function assertPriorAttemptSafe(prior,journal){
 // Only an explicitly documented preflight-only failure is resumable after review.
 assert.equal(prior.execution.commit,'not_attempted');assert.equal(prior.execution.error?.stage,'preflight');
 assert.equal(prior.execution.outcome,'not_committed_aborted');assert.equal(prior.before,undefined);
 assert.ok(!journal.some(event=>event.event==='commit_intent'),'Prior COMMIT intent requires manual reconciliation; no rerun');
}

async function main(){
const {Client}=require('C:/dev/stalltrack/eemo/tools/postgres-dev-mcp/node_modules/pg');
assert.equal(process.env.STALLTRACK_TASK2C4_AUTHORIZATION,'owner-approved-permanent-kind10-index-only-20261017');
const root='C:/dev/stalltrack/eemo';
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const conf=['EEMOCantilanSDS.Api/appsettings.json','EEMOCantilanSDS.Api/appsettings.Development.json'].map(p=>read(path.join(root,p)));
const secrets=path.join(process.env.APPDATA,'Microsoft/UserSecrets/9c52f74a-9378-4de3-b669-9ac14e61ef44/secrets.json');
if(fs.existsSync(secrets))conf.push(read(secrets));
let value;for(const x of conf)value=x.ConnectionStrings?.DefaultConnection||x['ConnectionStrings:DefaultConnection']||value;
value=process.env.ConnectionStrings__DefaultConnection||value;
assert.ok(value,'Missing configured local connection');
const fields={};
for(const m of value.matchAll(/(?:^|;)\s*([^=;]+)\s*=\s*("(?:[^"]|"")*"|'(?:[^']|'')*'|[^;]*)/g)){
 let v=m[2].trim();if((v.startsWith('"')&&v.endsWith('"'))||(v.startsWith("'")&&v.endsWith("'")))v=v.slice(1,-1);
 fields[m[1].trim().toLowerCase()]=v;
}
assert.ok(['localhost','127.0.0.1','::1'].includes(fields.host),'Nonlocal PostgreSQL host refused');
assert.equal(fields.database,'EEMOCantilanSDS.DB');assert.equal(Number(fields.port||5432),5432);
const cfg={host:fields.host,port:Number(fields.port||5432),database:fields.database,user:fields.username||fields['user id'],password:fields.password};
const c=new Client({...cfg,connectionTimeoutMillis:5000,application_name:'stalltrack_task2c4_owner_approved_index_only',options:'-c statement_timeout=15000 -c lock_timeout=1500 -c idle_in_transaction_session_timeout=30000'});
const previous=read(path.join(root,'docs/evidence/2026-10-17_legacy_allocation_index_rehearsal.json'));
const index='IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId';
const lookup='IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId_So~';
const kind8='IX_CollectionAllocations_MunicipalityId_SourceId';
const qid=s=>'"'+s.replaceAll('"','""')+'"';
const sha=x=>crypto.createHash('sha256').update(JSON.stringify(x)).digest('hex');
const shaFile=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
let transaction=false,locked=false,closed=false,writerPid;
const evidence={task:'2C.4',action:'Retire only obsolete kind-10 unique allocation index',authorization:'explicit owner 2026-10-17',target:{host:cfg.host,port:cfg.port,database:cfg.database},startedAtUtc:new Date().toISOString()};
// pg emits idle connection errors separately from query rejections. Never let an
// unhandled event terminate recovery after an uncertain COMMIT.
const writerErrors=[];c.on('error',e=>writerErrors.push(e.code||e.name));
async function snapshot(connection=c){
 const c=connection;
 const tables=(await c.query("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename")).rows.map(x=>x.tablename);
 const data=[];for(const t of tables){
  const sql="SELECT count(*)::text AS rows,md5(coalesce(string_agg(h,'' ORDER BY h),'')) AS fingerprint FROM (SELECT md5(to_jsonb(t)::text) AS h FROM public."+qid(t)+" t) s";
  data.push({table:t,...(await c.query(sql)).rows[0]});
 }
 const schemaSql="SELECT 'index' AS kind,c.relname AS name,pg_get_indexdef(c.oid) AS definition FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='i' UNION ALL SELECT 'constraint',co.conname,pg_get_constraintdef(co.oid) FROM pg_constraint co JOIN pg_namespace n ON n.oid=co.connamespace WHERE n.nspname='public' UNION ALL SELECT 'column',cl.relname||'.'||a.attname,format_type(a.atttypid,a.atttypmod)||':'||a.attnotnull::text||':'||coalesce(pg_get_expr(ad.adbin,ad.adrelid),'') FROM pg_class cl JOIN pg_namespace n ON n.oid=cl.relnamespace JOIN pg_attribute a ON a.attrelid=cl.oid LEFT JOIN pg_attrdef ad ON ad.adrelid=cl.oid AND ad.adnum=a.attnum WHERE n.nspname='public' AND cl.relkind IN ('r','p') AND a.attnum>0 AND NOT a.attisdropped UNION ALL SELECT 'view',c.relname,pg_get_viewdef(c.oid,true) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='v' ORDER BY kind,name,definition";
 const schema=(await c.query(schemaSql)).rows;
 const sequence=(await c.query('SELECT last_value::text,is_called FROM public."CollectionReferenceNumberSeq"')).rows[0];
 const obj='public.'+qid(index);
 const entry=(await c.query('SELECT pg_get_indexdef(indexrelid) AS definition,indisvalid AS valid,indisready AS ready FROM pg_index WHERE indexrelid=to_regclass($1)',[obj])).rows[0]||null;
 const history=(await c.query('SELECT count(*)::int AS n FROM "__EFMigrationsHistory"')).rows[0].n;
 return {tables,data,schema,dataHash:sha(data),schemaHash:sha(schema),sequence,index:entry,history};
}
async function noOthers(connection=c){
 const r=(await connection.query('SELECT count(*)::int AS n FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()')).rows[0].n;
 assert.equal(r,0,'Concurrent database connection; abort');
}
async function indexGuard(name,definition,unique,connection=c){
 const r=(await connection.query('SELECT pg_get_indexdef(indexrelid) AS definition,indisunique AS unique,indisvalid AS valid,indisready AS ready FROM pg_index WHERE indexrelid=to_regclass($1)',['public.'+qid(name)])).rows[0];
 assert.ok(r,'Missing expected index: '+name);assert.equal(r.definition,definition);assert.equal(r.unique,unique);assert.equal(r.valid,true);assert.equal(r.ready,true);
}
async function backup(){
 const old='C:/dev/stalltrack-v2-worktrees/local-db-backups/EEMOCantilanSDS.DB_task2c3_full_2026-10-17T00-57-38-716Z.dump';
 assert.equal(fs.statSync(old).size,1712039);assert.equal(shaFile(old),'08c54731264fb352b3e6d40c10dc129cd3240873ae8c982df7d047bf9ba7de99');
 const stamp=new Date().toISOString().replace(/[:.]/g,'-');
 const file='C:/dev/stalltrack-v2-worktrees/local-db-backups/EEMOCantilanSDS.DB_before_kind10_permanent_'+stamp+'.dump';
 assert.ok(!fs.existsSync(file));const env={...process.env,PGPASSWORD:cfg.password,PGCONNECT_TIMEOUT:'5',PGAPPNAME:'stalltrack_task2c4_backup'};
 const b=cp.spawnSync('C:/Program Files/PostgreSQL/18/bin/pg_dump.exe',['--host',cfg.host,'--port',String(cfg.port),'--username',cfg.user,'--dbname',cfg.database,'--format=custom','--file',file,'--no-password','--lock-wait-timeout=1500ms'],{env,windowsHide:true,timeout:120000});
 assert.equal(b.status,0,'Fresh pg_dump failed');
 const tool='C:/Program Files/PostgreSQL/18/bin/pg_restore.exe';
 const list=cp.spawnSync(tool,['--list',file],{encoding:'utf8',windowsHide:true,timeout:30000});
 assert.equal(list.status,0,'Backup archive unreadable');
 const decode=cp.spawnSync(tool,['--file','NUL',file],{windowsHide:true,timeout:120000});
 assert.equal(decode.status,0,'Full backup archive decoding failed');
 const names=['ConfigurableServiceAssessments','ConfigurableServiceDefinitions','ConfigurableServicePolicies','EcfApprovedAssessments','FishMeatVendorFeeObligations','FishMeatVendorFeePolicies','KanmanggayMonthlyObligations','KanmanggayMonthlyPolicies','KanmanggayRentalAccounts','LandingBerthingActivities','VegetableSpaceRentalAssessments','VegetableSpaceRentalPolicies','WcfApprovedChargePolicies','__EFMigrationsHistory'];
 for(const name of names)assert.ok(list.stdout.split(/\r?\n/).some(l=>l.includes('TABLE DATA public '+name+' ')),'Backup missing retained '+name);
 return {path:file,createdAtUtc:new Date().toISOString(),bytes:fs.statSync(file).size,sha256:shaFile(file),readabilityVerified:true,retainedTablesAndHistoryCovered:14};
}
async function identity(connection){
 const server=(await connection.query("SELECT current_database() AS db,host(inet_server_addr()) AS address,inet_server_port() AS port,current_setting('server_version_num')::int AS version,pg_backend_pid() AS pid")).rows[0];
 assertServerIdentity(server);return server;
}
function apiStopped(){
 const ps="$ErrorActionPreference='Stop'; $p=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'EEMOCantilanSDS.Api*' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'EEMOCantilanSDS.Api') }); $l=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object LocalPort -in 5117,7097); if ($p.Count -or $l.Count) { exit 23 }";
 const check=cp.spawnSync('powershell.exe',['-NoProfile','-Command',ps],{windowsHide:true,timeout:20000});assert.equal(check.status,0,'API quiescence could not be established');
}
function durableAppend(file,record){
 const fd=fs.openSync(file,'a');try{fs.writeFileSync(fd,JSON.stringify(record)+'\n');fs.fsyncSync(fd);}finally{fs.closeSync(fd);}
}
const runId=new Date().toISOString().replace(/[:.]/g,'-');
const journal='C:/dev/stalltrack-v2-worktrees/local-db-backups/kind10-correction-'+runId+'.journal.jsonl';
// Exclusive journal creation must succeed before opening the database.
const journalFd=fs.openSync(journal,'wx');try{fs.fsyncSync(journalFd);}finally{fs.closeSync(journalFd);}
evidence.journal=journal;evidence.scriptSha256=shaFile(__filename);
async function closeWriter(){
 if(closed)return;
 // A lost acknowledgement is resolved by another connection, never by retrying COMMIT.
 try{await Promise.race([c.end(),new Promise((_,reject)=>{const t=setTimeout(()=>reject(Error('close timeout')),3000);t.unref();})]);}
 catch{c.connection?.stream?.destroy();}
 closed=true;transaction=false;locked=false;
}
const io={
prepare:async()=>{
 const priorResult=path.join(root,'docs/evidence/2026-10-17_kind10_permanent_correction.json');
 if(fs.existsSync(priorResult)){
  const prior=read(priorResult),records=fs.readFileSync(prior.journal,'utf8').trim().split(/\r?\n/).filter(Boolean).map(JSON.parse);
  assertPriorAttemptSafe(prior,records);evidence.reviewedPreflightOnlyAttempt=priorResult;
 }
 const backupDir='C:/dev/stalltrack-v2-worktrees/local-db-backups';
 for(const file of fs.readdirSync(backupDir).filter(n=>n.startsWith('kind10-correction-')&&n.endsWith('.journal.jsonl'))){
  if(path.resolve(backupDir,file)===path.resolve(journal))continue;
  const records=fs.readFileSync(path.join(backupDir,file),'utf8').trim().split(/\r?\n/).filter(Boolean).map(JSON.parse);
  assert.ok(!records.some(r=>r.event==='commit_intent'),'Existing COMMIT intent: no repeated execution');
 }
 apiStopped();
 await c.connect();
 const server=await identity(c);writerPid=server.pid;evidence.writer=server;
 await noOthers();
 await c.query('BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY');transaction=true;
 const baseline=await snapshot();
 assert.equal(baseline.history,143);assert.equal(baseline.tables.length,95);
 assert.equal(baseline.dataHash,previous.before.dataHash,'Database contents changed since reviewed rehearsal');
 assert.equal(baseline.schemaHash,previous.before.schemaHash,'Schema differs from reviewed rehearsal');
 assert.deepEqual(baseline.index,previous.before.index);assert.deepEqual(baseline.sequence,previous.before.sequence);
 await indexGuard(lookup,previous.nonuniqueLookup,false);
 await indexGuard(kind8,previous.kind8Unmodified.definition,true);
 await c.query('ROLLBACK');transaction=false;
 evidence.backup=await backup();apiStopped();
 return {baseline,backup:evidence.backup};
},
mutate:async({baseline})=>{
 await noOthers();
 assert.equal((await c.query('SELECT pg_try_advisory_lock($1::bigint) AS ok',['8472113509001'])).rows[0].ok,true,'Migration advisory lock unavailable');locked=true;
 await noOthers();
 await c.query('BEGIN ISOLATION LEVEL REPEATABLE READ');transaction=true;
 const tables=(await c.query("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename")).rows.map(x=>x.tablename);
 await c.query('LOCK TABLE '+tables.map(t=>'public.'+qid(t)).join(',')+' IN SHARE ROW EXCLUSIVE MODE NOWAIT');
 await c.query('LOCK TABLE public."CollectionAllocations" IN ACCESS EXCLUSIVE MODE NOWAIT');
 const triggers=(await c.query("SELECT count(*)::int AS n FROM pg_trigger t JOIN pg_class cl ON cl.oid=t.tgrelid JOIN pg_namespace n ON n.oid=cl.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal")).rows[0].n;
 assert.equal(triggers,0,'Unexpected public trigger');
 const before=await snapshot();assert.equal(before.dataHash,baseline.dataHash);assert.equal(before.schemaHash,baseline.schemaHash);assert.deepEqual(before.index,previous.before.index);
 const dependencies=(await c.query("SELECT count(*)::int AS n FROM pg_depend WHERE refclassid='pg_class'::regclass AND refobjid=to_regclass($1)",['public.'+qid(index)])).rows[0].n;
 assert.equal(dependencies,0,'Unexpected index dependencies');
 await indexGuard(lookup,previous.nonuniqueLookup,false);await indexGuard(kind8,previous.kind8Unmodified.definition,true);
 await c.query('DROP INDEX public."IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId"');
 const inside=await snapshot();assert.equal(inside.index,null);assert.equal(inside.history,143);assert.equal(inside.dataHash,before.dataHash);assert.deepEqual(inside.sequence,before.sequence);
 assertSingleIndexDelta(before,inside,previous.before.index.definition);
 await indexGuard(lookup,previous.nonuniqueLookup,false);await indexGuard(kind8,previous.kind8Unmodified.definition,true);
 return inside;
},
persistIntent:async context=>{
 assert.equal(writerErrors.length,0,'Writer connection failed before commit intent');
 assert.equal(shaFile(context.backup.path),context.backup.sha256,'Fresh backup changed');
 durableAppend(journal,{event:'commit_intent',atUtc:new Date().toISOString(),action:'single fixed-name index retirement',target:evidence.target,writer:evidence.writer,scriptSha256:evidence.scriptSha256,backup:context.backup,before:context.baseline,expectedAfter:context.inside});
},
commit:async()=>{await c.query('COMMIT');transaction=false;},
abort:async()=>{if(transaction&&!closed){await c.query('ROLLBACK');transaction=false;}},
closeWriter,
verify:async context=>{
 const verifier=new Client({...cfg,connectionTimeoutMillis:5000,application_name:'stalltrack_task2c4_independent_readonly',options:'-c default_transaction_read_only=on -c statement_timeout=15000 -c lock_timeout=1500'});
 verifier.on('error',()=>{}); // Query failures are reported by the explicit verifier outcome.
 try{
  await verifier.connect();const server=await identity(verifier);assert.notEqual(server.pid,writerPid);
  await noOthers(verifier);await verifier.query('BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY');
  assert.equal((await verifier.query("SHOW transaction_read_only")).rows[0].transaction_read_only,'on');
  // Fails immediately if an unresolved original transaction still holds exclusive table locks.
  await verifier.query('LOCK TABLE '+context.baseline.tables.map(t=>'public.'+qid(t)).join(',')+' IN ACCESS SHARE MODE NOWAIT');
  const after=await snapshot(verifier),before=context.baseline;
  assert.equal(after.history,143);assert.equal(after.dataHash,before.dataHash);assert.deepEqual(after.tables,before.tables);assert.deepEqual(after.sequence,before.sequence);
  await indexGuard(lookup,previous.nonuniqueLookup,false,verifier);await indexGuard(kind8,previous.kind8Unmodified.definition,true,verifier);
  let state='unexpected';
  if(after.index===null&&after.schemaHash===context.inside.schemaHash){assert.deepEqual(after.schema,context.inside.schema);state='committed';}
  else if(after.schemaHash===before.schemaHash){assert.deepEqual(after.index,before.index);state='unchanged';}
  await verifier.query('ROLLBACK');
  return {state,evidence:{server,readOnly:true,tables:after.tables.length,migrations:after.history,dataHash:after.dataHash,schemaHash:after.schemaHash,srcSequence:after.sequence,retiredIndexPresent:after.index!==null,otherIndexesPreserved:true}};
 }finally{try{await verifier.end();}catch{}}
},
persistResult:async(state,context)=>{
 evidence.execution=state;evidence.finishedAtUtc=new Date().toISOString();
 if(context)evidence.before={tables:context.baseline.tables.length,migrations:context.baseline.history,dataHash:context.baseline.dataHash,schemaHash:context.baseline.schemaHash,srcSequence:context.baseline.sequence,retiredIndex:context.baseline.index.definition};
 durableAppend(journal,{event:'result',...evidence});
 const resultFile=path.join(root,'docs/evidence/2026-10-17_kind10_permanent_correction_'+runId+'.json');
 // Do not overwrite evidence from another or an uncertain prior operation.
 const fd=fs.openSync(resultFile,'wx');try{fs.writeFileSync(fd,JSON.stringify(evidence,null,2));fs.fsyncSync(fd);}finally{fs.closeSync(fd);}
}
};
const result=await executeCorrection(io);
console.log(JSON.stringify({execution:result,backup:evidence.backup,journal}));
if(!result.success)process.exitCode=1;
return result;
}
module.exports={executeCorrection,assertSingleIndexDelta,assertServerIdentity,assertPriorAttemptSafe};
if(require.main===module)main().catch(e=>{console.error(JSON.stringify({outcome:'not_established_do_not_retry_without_reconciliation',code:e.code||e.name}));process.exitCode=1;});
