'use strict';
// Owner-authorized LOCAL foundation only. No API, seed, financial command or automatic retry.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),cp=require('node:child_process'),assert=require('node:assert/strict');
const ROOT='C:/dev/stalltrack/eemo',ID='20261017040000_AddNpmRentMonthFoundation';
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const hash=x=>crypto.createHash('sha256').update(JSON.stringify(x)).digest('hex');
const digest=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const q=s=>'"'+s.replaceAll('"','""')+'"';
const baseline=read(ROOT+'/docs/evidence/task2d/checkpoint.json');
const history=read(ROOT+'/docs/evidence/2026-10-17_ef_migration_lineage_metadata.json').appliedMigrations;
const rehearsedConstraints=read(ROOT+'/docs/evidence/task2d2/result-2026-10-17T03-12-22-079Z.json').installed.constraints;
function durable(p,value){const fd=fs.openSync(p,'wx');try{fs.writeFileSync(fd,JSON.stringify(value,null,2));fs.fsyncSync(fd);}finally{fs.closeSync(fd);}}
function config(){
 const entries=['EEMOCantilanSDS.Api/appsettings.json','EEMOCantilanSDS.Api/appsettings.Development.json'].map(p=>read(ROOT+'/'+p));
 const secret=path.join(process.env.APPDATA,'Microsoft/UserSecrets/9c52f74a-9378-4de3-b669-9ac14e61ef44/secrets.json');
 if(fs.existsSync(secret))entries.push(read(secret));let cs;
 for(const e of entries)cs=e.ConnectionStrings?.DefaultConnection||e['ConnectionStrings:DefaultConnection']||cs;
 cs=process.env.ConnectionStrings__DefaultConnection||cs;assert.ok(cs);
 const f={};for(const m of cs.matchAll(/(?:^|;)\s*([^=;]+)\s*=\s*("(?:[^"]|"")*"|'(?:[^']|'')*'|[^;]*)/g)){let v=m[2].trim();if(/^['"]/.test(v))v=v.slice(1,-1);f[m[1].trim().toLowerCase()]=v;}
 assert.ok(['localhost','127.0.0.1','::1'].includes(f.host));assert.equal(f.database,'EEMOCantilanSDS.DB');assert.equal(Number(f.port||5432),5432);
 return {cs,pg:{host:f.host,port:5432,database:f.database,user:f.username||f['user id'],password:f.password}};
}
function inactive(){
 const command="$ErrorActionPreference='Stop'; $a=@(Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^EEMOCantilanSDS.Api' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'EEMOCantilanSDS.Api') }); $p=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -in 5117,7097,5123,7597 }); if($a.Count -ne 0 -or $p.Count -ne 0){exit 2}; exit 0";
 assert.equal(cp.spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',command],{windowsHide:true,timeout:20000}).status,0,'API/writer inactivity not established');
}
function artifacts(){
 const expected={
  [ID+'.cs']:'5a4df8d065aac62b858748a9aedc0e951435f969bcbe34c7aed84a730feb2346',
  [ID+'.Designer.cs']:'01eeca24f4797404ad2d846ee808d588f932d10baaffae14f2f23c34f5516ef7',
  'AppDbContextModelSnapshot.cs':'15129f5b2e9aa7c19cfb2005f2feb92297178c7be48dcbd33942d776a519796f'};
 for(const [name,h] of Object.entries(expected))assert.equal(digest(ROOT+'/EEMOCantilanSDS.Infrastructure/Migrations/'+name),h,'Promoted artifact mismatch');
 assert.equal(digest(ROOT+'/docs/evidence/task2d3/'+ID+'.Up.sql.review'),'f65f63121fe7d439f549ebb5b8a96ab0774a302d207a8558e04b6391ed2722c7');
 return expected;
}
async function capture(cfg){
 const {Client}=require(ROOT+'/tools/postgres-dev-mcp/node_modules/pg');
 const c=new Client({...cfg,application_name:'stalltrack_foundation_independent_readonly',connectionTimeoutMillis:5000,
  options:'-c default_transaction_read_only=on -c statement_timeout=15000 -c lock_timeout=1500'});
 c.on('error',()=>{});
 try{
  await c.connect();await c.query('BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY');
  const server=(await c.query("SELECT current_database() AS database,host(inet_server_addr()) AS address,inet_server_port() AS port,current_setting('transaction_read_only') AS readonly")).rows[0];
  assert.equal(server.database,'EEMOCantilanSDS.DB');assert.ok(['::1','127.0.0.1'].includes(server.address));assert.equal(server.port,5432);assert.equal(server.readonly,'on');
  assert.equal((await c.query('SELECT count(*)::int AS n FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()')).rows[0].n,0,'Other database sessions present');
  const tables=(await c.query("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename")).rows.map(x=>x.tablename);
  const data=[];for(const t of tables)data.push({table:t,...(await c.query(`SELECT count(*)::text AS rows,md5(coalesce(string_agg(h,'' ORDER BY h),'')) AS fingerprint FROM (SELECT md5(to_jsonb(t)::text) AS h FROM public.${q(t)} t) s`)).rows[0]});
  const schema=(await c.query(`SELECT 'index' AS kind,c.relname AS name,pg_get_indexdef(c.oid) AS definition FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='i' UNION ALL SELECT 'constraint',co.conname,pg_get_constraintdef(co.oid) FROM pg_constraint co JOIN pg_namespace n ON n.oid=co.connamespace WHERE n.nspname='public' UNION ALL SELECT 'column',cl.relname||'.'||a.attname,format_type(a.atttypid,a.atttypmod)||':'||a.attnotnull::text||':'||coalesce(pg_get_expr(ad.adbin,ad.adrelid),'') FROM pg_class cl JOIN pg_namespace n ON n.oid=cl.relnamespace JOIN pg_attribute a ON a.attrelid=cl.oid LEFT JOIN pg_attrdef ad ON ad.adrelid=cl.oid AND ad.adnum=a.attnum WHERE n.nspname='public' AND cl.relkind IN ('r','p') AND a.attnum>0 AND NOT a.attisdropped UNION ALL SELECT 'view',c.relname,pg_get_viewdef(c.oid,true) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='v' ORDER BY kind,name,definition`)).rows;
  const objects=(await c.query("SELECT c.relname,c.relkind,c.relpersistence,c.relrowsecurity,c.relforcerowsecurity,c.relowner::text,c.relacl::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' ORDER BY c.relname")).rows;
  const triggers=(await c.query("SELECT c.relname,t.tgname,pg_get_triggerdef(t.oid) AS definition FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal ORDER BY c.relname,t.tgname")).rows;
  const eventTriggers=(await c.query('SELECT evtname,evtevent,evtenabled FROM pg_event_trigger ORDER BY evtname')).rows;
  const rules=(await c.query("SELECT c.relname,r.rulename FROM pg_rewrite r JOIN pg_class c ON c.oid=r.ev_class JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind NOT IN ('v','m') ORDER BY c.relname,r.rulename")).rows;
  const sequences=[];for(const s of objects.filter(x=>x.relkind==='S'))sequences.push({name:s.relname,...(await c.query(`SELECT last_value::text,is_called FROM public.${q(s.relname)}`)).rows[0]});
  const history=(await c.query('SELECT "MigrationId","ProductVersion" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId"')).rows;
  const indexes=(await c.query("SELECT c.relname AS name,pg_get_indexdef(c.oid) AS definition,i.indisvalid AS valid,i.indisready AS ready FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' ORDER BY c.relname")).rows;
  await c.query('ROLLBACK');return {server,tables,data,schema,objects,triggers,eventTriggers,rules,sequences,history,indexes};
 }finally{await c.end();}
}
function preflight(s){
 assert.equal(s.tables.length,95);assert.ok(!s.tables.includes('NpmRentMonths'));assert.deepEqual(s.history,history);
 assert.equal(hash(s.data),baseline.allTableDataHash);assert.equal(hash(s.schema),baseline.checkedSchemaHash);
 for(const key of ['triggers','eventTriggers','rules'])assert.equal(s[key].length,0);
 assert.ok(!s.indexes.some(x=>x.name==='IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId'));
 for(const x of baseline.indexes)assert.deepEqual(s.indexes.find(i=>i.name===x.name),x);
 for(const t of baseline.retainedTables)assert.ok(s.tables.includes(t.table));
}
function postflight(before,after){
 assert.deepEqual(after.tables,[...before.tables,'NpmRentMonths'].sort());
 assert.deepEqual(after.history,[...before.history,{MigrationId:ID,ProductVersion:'9.0.4'}].sort((a,b)=>a.MigrationId.localeCompare(b.MigrationId)));
 for(const old of before.data)if(old.table!=='__EFMigrationsHistory')assert.deepEqual(after.data.find(x=>x.table===old.table),old,'Original data changed: '+old.table);
 assert.equal(after.data.find(x=>x.table==='NpmRentMonths').rows,'0');
 for(const key of ['sequences','triggers','eventTriggers','rules'])assert.deepEqual(after[key],before[key],key+' changed');
 for(const old of before.schema)assert.ok(after.schema.some(x=>JSON.stringify(x)===JSON.stringify(old)),'Original schema changed: '+old.name);
 const added=after.schema.filter(x=>!before.schema.some(b=>JSON.stringify(b)===JSON.stringify(x)));
 const parent=['AK_Facilities_MunicipalityId_Id','AK_Stalls_MunicipalityId_Id_FacilityId'];
 // PostgreSQL 18 exposes the table's 18 NOT NULL constraints in pg_constraint too.
 // Match the already accepted rehearsal definitions, rather than weakening a count check.
 assert.equal(added.length,68); // 26 columns, 8 indexes, 34 constraints including NOT NULL.
 for(const x of added)assert.ok(x.name.includes('NpmRentMonths')||parent.includes(x.name),'Unexpected schema addition');
 assert.equal(added.filter(x=>x.kind==='column').length,26);assert.equal(added.filter(x=>x.kind==='index').length,8);
 const constraints=added.filter(x=>x.kind==='constraint');assert.equal(constraints.length,34);
 for(const x of rehearsedConstraints)assert.ok(constraints.some(c=>c.name===x.conname&&c.definition===x.definition),'Constraint differs from rehearsal: '+x.conname);
 assert.equal(constraints.filter(x=>x.definition.startsWith('CHECK ')).length,9);
 assert.equal(constraints.filter(x=>x.definition.startsWith('FOREIGN KEY ')).length,3);
 for(const old of before.objects)assert.deepEqual(after.objects.find(x=>x.relname===old.relname),old);
 const newObjects=after.objects.filter(x=>!before.objects.some(b=>b.relname===x.relname));
 assert.equal(newObjects.length,9);for(const x of newObjects)assert.ok(x.relname.includes('NpmRentMonths')||parent.includes(x.relname));
 for(const old of before.indexes)assert.deepEqual(after.indexes.find(x=>x.name===old.name),old);
 for(const x of after.indexes)assert.ok(x.valid&&x.ready);
 return {addedSchemaEntries:added,originalDataUnchanged:true,originalHistoryPreserved:true,sequencesUnchanged:true,retainedTablesPreserved:13};
}
function backup(cfg,tableCount,label){
 const file='C:/dev/stalltrack-v2-worktrees/local-db-backups/EEMOCantilanSDS.DB_npm_install_'+label+'_'+new Date().toISOString().replace(/[:.]/g,'-')+'.dump';
 assert.ok(!fs.existsSync(file));const env={...process.env,PGPASSWORD:cfg.password,PGCONNECT_TIMEOUT:'5',PGAPPNAME:'stalltrack_foundation_full_backup'};
 const bin='C:/Program Files/PostgreSQL/18/bin/';
 assert.equal(cp.spawnSync(bin+'pg_dump.exe',['--host',cfg.host,'--port','5432','--username',cfg.user,'--dbname',cfg.database,'--format=custom','--file',file,'--no-password','--lock-wait-timeout=1500ms'],{env,windowsHide:true,timeout:120000}).status,0,'Full backup failed');
 const list=cp.spawnSync(bin+'pg_restore.exe',['--list',file],{encoding:'utf8',windowsHide:true,timeout:30000});assert.equal(list.status,0);
 assert.equal(cp.spawnSync(bin+'pg_restore.exe',['--file','NUL',file],{windowsHide:true,timeout:120000}).status,0,'Full decode failed');
 const data=list.stdout.split(/\r?\n/).filter(x=>/TABLE DATA public /.test(x));assert.equal(data.length,tableCount);
 for(const t of [...baseline.retainedTables.map(x=>x.table),'__EFMigrationsHistory',...(tableCount===96?['NpmRentMonths']:[])])assert.ok(data.some(x=>x.includes('TABLE DATA public '+t+' ')));
 return {path:file,sha256:digest(file),bytes:fs.statSync(file).size,verifiedFullDecode:true,publicTableDataEntries:data.length};
}
async function main(){
 assert.ok(['preflight','apply','verify-installed'].includes(process.argv[2]));assert.ok(!process.env.KIRO_PG_RESTORE_TEST,'Reset-oriented test opt-in must be absent');
 if(process.argv[2]==='verify-installed'){
  // This mode cannot invoke the migration child. Use the fsynced ORIGINAL baseline after an uncertain result.
  const intent=path.resolve(process.argv[3]||'');assert.equal(path.dirname(intent),__dirname);assert.ok(intent.endsWith('-intent.json'));
  const original=read(intent);assert.equal(original.operation,ID);artifacts();inactive();
  const cfg=config();const after=await capture(cfg.pg);const verification=postflight(original.baseline,after);
  const file=path.join(__dirname,new Date().toISOString().replace(/[:.]/g,'-')+'-reconciled.json');
  durable(file,{outcome:'independently_verified_installed',intent,scriptSha256:digest(__filename),verification,postflight:after});
  console.log(JSON.stringify({outcome:'independently_verified_installed',evidence:file}));
  const postBackup=backup(cfg.pg,96,'after');durable(file+'.backup.json',{postBackup});console.log(JSON.stringify({postBackup}));return;
 }
 const cfg=config();const reviewed=artifacts();inactive();const before=await capture(cfg.pg);preflight(before);
 const run=new Date().toISOString().replace(/[:.]/g,'-'),prefix=path.join(__dirname,run);
 const evidence={startedAtUtc:new Date().toISOString(),scriptSha256:digest(__filename),reviewed,preflight:{server:before.server,tables:95,history:143,dataHash:hash(before.data),schemaHash:hash(before.schema),sequences:before.sequences}};
 if(process.argv[2]==='preflight'){durable(prefix+'-preflight.json',evidence);console.log('Readonly preflight passed; no database mutation.');return;}
 assert.equal(process.env.STALLTRACK_FOUNDATION_APPROVAL,'owner-approved-local-foundation-only');
 evidence.preBackup=backup(cfg.pg,95,'before');inactive();assert.deepEqual(await capture(cfg.pg),before,'Baseline changed during backup');
 // Durable intent precedes invocation; an unsuccessful child can still have committed. Never retry.
 durable(prefix+'-intent.json',{...evidence,operation:ID,state:'migration_invocation_not_yet_attempted',baseline:before});
 const child=cp.spawnSync('dotnet',['tools/npm-foundation-install/bin/Release/net9.0/Install.dll','apply'],{
  cwd:ROOT,env:{...process.env,STALLTRACK_INSTALL_CONNECTION:cfg.cs},windowsHide:true,timeout:120000,encoding:'utf8'});
 evidence.invocation={attempted:true,exitCode:child.status,acknowledged:child.stdout?.includes('ef_migration_acknowledged')===true};
 // Do not publish child exception text (may contain connection details). Verify on a NEW connection.
 try{
  inactive();const after=await capture(cfg.pg);
  if(hash(after.schema)===hash(before.schema)&&hash(after.data)===hash(before.data)&&hash(after.history)===hash(before.history)){
   assert.deepEqual(after,before);evidence.outcome='independently_verified_unchanged';
  }else{
   evidence.verification=postflight(before,after);evidence.outcome='independently_verified_installed';
   evidence.postflight={server:after.server,tables:after.tables.length,history:after.history.length,dataHash:hash(after.data),schemaHash:hash(after.schema),sequences:after.sequences};
  }
 }catch(e){evidence.outcome='unresolved_or_unexpected_state';evidence.verificationError=e.name+': '+e.message;}
 // Local evidence failure does not change the recorded DB conclusion or trigger a retry.
 try{durable(prefix+'-result.json',evidence);}catch{console.log('EVIDENCE_WRITE_FAILED; database outcome: '+evidence.outcome);process.exitCode=2;return;}
 if(evidence.outcome!=='independently_verified_installed'){console.log(JSON.stringify({outcome:evidence.outcome,evidence:prefix+'-result.json'}));process.exitCode=2;return;}
 // No further schema operation after verification. Only create the required post-install archive.
 try{const postBackup=backup(cfg.pg,96,'after');durable(prefix+'-post-backup.json',{outcome:evidence.outcome,postBackup});console.log(JSON.stringify({outcome:evidence.outcome,evidence:prefix+'-result.json',postBackup}));}
 catch{console.log('Foundation independently installed; post-install backup failed. Stop database operations.');process.exitCode=2;}
}
if(require.main===module)main().catch(e=>{console.error(e.name+': '+e.message);process.exitCode=1;});
module.exports={postflight,preflight,artifacts};
