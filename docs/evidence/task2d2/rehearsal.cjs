// Task 2D.2: explicit owner-approved LOCAL rollback-only schema rehearsal. Never a migration runner.
'use strict';
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),cp=require('node:child_process'),assert=require('node:assert/strict');
const ROOT='C:/dev/stalltrack/eemo',DIR=__dirname;
const DDL=path.join(ROOT,'docs/evidence/task2d/NpmRentMonthFoundation.Up.sql.review');
const DDL_HASH='1cfc71caaaf302042e4c2fac66c74f5b24ea9fecb499f4a22d0ebc5ec4120a31';
const AUTH='owner-approved-task2d2-seven-operations-rollback-only-local';
const qid=s=>'"'+s.replaceAll('"','""')+'"';
const hash=x=>crypto.createHash('sha256').update(JSON.stringify(x)).digest('hex');
const fileHash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const baseline=read(path.join(ROOT,'docs/evidence/task2d/checkpoint.json'));
const originalHistory=read(path.join(ROOT,'docs/evidence/2026-10-17_ef_migration_lineage_metadata.json')).appliedMigrations;
const retained=baseline.retainedTables.map(x=>x.table);
function durable(file,value){const fd=fs.openSync(file,'wx');try{fs.writeFileSync(fd,JSON.stringify(value,null,2));fs.fsyncSync(fd);}finally{fs.closeSync(fd);}}

function verifyDdl(text,digest){
 assert.equal(digest,DDL_HASH,'Unreviewed foundation DDL');
 const statements=text.replace(/^--.*$/gm,'').split(';').map(s=>s.trim()).filter(Boolean);
 assert.equal(statements.length,7);
 assert.ok(statements[0].startsWith('ALTER TABLE "Stalls" ADD CONSTRAINT "AK_Stalls_MunicipalityId_Id_FacilityId" UNIQUE'));
 assert.ok(statements[1].startsWith('ALTER TABLE "Facilities" ADD CONSTRAINT "AK_Facilities_MunicipalityId_Id" UNIQUE'));
 assert.ok(statements[2].startsWith('CREATE TABLE "NpmRentMonths"'));
 assert.equal((statements[2].match(/FOREIGN KEY/g)||[]).length,3);
 assert.equal((statements[2].match(/ON DELETE RESTRICT/g)||[]).length,3);
 assert.equal((statements[2].match(/ CHECK /g)||[]).length,9);
 for(const s of statements.slice(3))assert.match(s,/^CREATE (UNIQUE )?INDEX "IX_NpmRentMonths_/);
 return statements;
}
function apiInactive(){
 const script="$ErrorActionPreference = 'Stop'; $api = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^EEMOCantilanSDS.Api' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'EEMOCantilanSDS.Api') }); $ports = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object { $_.LocalPort -in 5117,7097,5123,7597 }); if ($api.Count -ne 0 -or $ports.Count -ne 0) { exit 2 }; exit 0";
 const p=cp.spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',script],{windowsHide:true,timeout:20000});
 assert.equal(p.status,0,'API/process inactivity not established');
}
function config(){
 const entries=['EEMOCantilanSDS.Api/appsettings.json','EEMOCantilanSDS.Api/appsettings.Development.json'].map(p=>read(path.join(ROOT,p)));
 const secret=path.join(process.env.APPDATA,'Microsoft/UserSecrets/9c52f74a-9378-4de3-b669-9ac14e61ef44/secrets.json');
 if(fs.existsSync(secret))entries.push(read(secret));
 let value;for(const entry of entries)value=entry.ConnectionStrings?.DefaultConnection||entry['ConnectionStrings:DefaultConnection']||value;
 value=process.env.ConnectionStrings__DefaultConnection||value;assert.ok(value,'No local connection configuration');
 const fields={};for(const m of value.matchAll(/(?:^|;)\s*([^=;]+)\s*=\s*("(?:[^"]|"")*"|'(?:[^']|'')*'|[^;]*)/g)){
  let v=m[2].trim();if((v.startsWith('"')&&v.endsWith('"'))||(v.startsWith("'")&&v.endsWith("'")))v=v.slice(1,-1);fields[m[1].trim().toLowerCase()]=v;
 }
 assert.ok(['localhost','127.0.0.1','::1'].includes(fields.host));assert.equal(fields.database,'EEMOCantilanSDS.DB');assert.equal(Number(fields.port||5432),5432);
 return {host:fields.host,port:5432,database:fields.database,user:fields.username||fields['user id'],password:fields.password};
}
function client(cfg,readonly){
 const {Client}=require(path.join(ROOT,'tools/postgres-dev-mcp/node_modules/pg'));
 const c=new Client({...cfg,connectionTimeoutMillis:5000,application_name:'stalltrack_task2d2_'+(readonly?'readonly':'rollback_only'),
  options:`-c default_transaction_read_only=${readonly?'on':'off'} -c statement_timeout=15000 -c lock_timeout=1500 -c idle_in_transaction_session_timeout=60000`});
 c.on('error',()=>{});return c;
}
async function identity(c,readonly){
 const r=(await c.query("SELECT current_database() AS database,host(inet_server_addr()) AS address,inet_server_port() AS port,pg_backend_pid() AS pid,current_setting('transaction_read_only') AS read_only")).rows[0];
 assert.equal(r.database,'EEMOCantilanSDS.DB');assert.equal(r.port,5432);assert.ok(['127.0.0.1','::1'].includes(r.address));assert.equal(r.read_only,readonly?'on':'off');return r;
}
async function noOthers(c){const r=await c.query('SELECT count(*)::int AS n FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()');assert.equal(r.rows[0].n,0,'Another database session is present');}
async function snapshot(c){
 const tables=(await c.query("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename")).rows.map(x=>x.tablename);
 const data=[];for(const t of tables)data.push({table:t,...(await c.query(`SELECT count(*)::text AS rows,md5(coalesce(string_agg(h,'' ORDER BY h),'')) AS fingerprint FROM (SELECT md5(to_jsonb(t)::text) AS h FROM public.${qid(t)} t) s`)).rows[0]});
 const schema=(await c.query(`SELECT 'index' AS kind,c.relname AS name,pg_get_indexdef(c.oid) AS definition FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='i' UNION ALL SELECT 'constraint',co.conname,pg_get_constraintdef(co.oid) FROM pg_constraint co JOIN pg_namespace n ON n.oid=co.connamespace WHERE n.nspname='public' UNION ALL SELECT 'column',cl.relname||'.'||a.attname,format_type(a.atttypid,a.atttypmod)||':'||a.attnotnull::text||':'||coalesce(pg_get_expr(ad.adbin,ad.adrelid),'') FROM pg_class cl JOIN pg_namespace n ON n.oid=cl.relnamespace JOIN pg_attribute a ON a.attrelid=cl.oid LEFT JOIN pg_attrdef ad ON ad.adrelid=cl.oid AND ad.adnum=a.attnum WHERE n.nspname='public' AND cl.relkind IN ('r','p') AND a.attnum>0 AND NOT a.attisdropped UNION ALL SELECT 'view',c.relname,pg_get_viewdef(c.oid,true) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='v' ORDER BY kind,name,definition`)).rows;
 const objects=(await c.query(`SELECT c.relname,c.relkind,c.relpersistence,c.relrowsecurity,c.relforcerowsecurity,c.relowner::text,c.relacl::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' ORDER BY c.relname`)).rows;
 const triggers=(await c.query("SELECT c.relname,t.tgname,pg_get_triggerdef(t.oid) AS definition FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal ORDER BY c.relname,t.tgname")).rows;
 const eventTriggers=(await c.query('SELECT evtname,evtevent,evtenabled FROM pg_event_trigger ORDER BY evtname')).rows;
 const rewriteRules=(await c.query("SELECT c.relname,r.rulename FROM pg_rewrite r JOIN pg_class c ON c.oid=r.ev_class JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind NOT IN ('v','m') ORDER BY c.relname,r.rulename")).rows;
 const sequences=[];for(const s of objects.filter(x=>x.relkind==='S'))sequences.push({name:s.relname,...(await c.query(`SELECT last_value::text,is_called FROM public.${qid(s.relname)}`)).rows[0]});
 const history=(await c.query('SELECT "MigrationId","ProductVersion" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId"')).rows;
 const indexes=(await c.query("SELECT c.relname AS name,pg_get_indexdef(c.oid) AS definition,i.indisvalid AS valid,i.indisready AS ready FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' ORDER BY c.relname")).rows;
 return {tables,data,schema,objects,triggers,eventTriggers,rewriteRules,sequences,history,indexes,dataHash:hash(data),schemaHash:hash(schema),extraSchemaHash:hash({objects,triggers,eventTriggers,rewriteRules,sequences:sequences.map(s=>s.name)})};
}
function assertBaseline(s){
 assert.equal(s.tables.length,95);assert.ok(!s.tables.includes('NpmRentMonths'));assert.equal(s.history.length,143);
 assert.deepEqual(s.history,originalHistory);
 assert.equal(s.dataHash,baseline.allTableDataHash);assert.equal(s.schemaHash,baseline.checkedSchemaHash);
 assert.equal(s.triggers.length,0,'Unexpected user triggers; side effects not reviewed');
 assert.equal(s.eventTriggers.length,0,'Unexpected DDL event triggers; side effects not reviewed');
 assert.equal(s.rewriteRules.length,0,'Unexpected non-view rewrite rules; side effects not reviewed');
 for(const t of retained)assert.ok(s.tables.includes(t));
 assert.ok(!s.indexes.some(i=>i.name==='IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId'));
 for(const old of baseline.indexes)assert.deepEqual(s.indexes.find(i=>i.name===old.name),old);
 for(const name of ['AK_Stalls_MunicipalityId_Id_FacilityId','AK_Facilities_MunicipalityId_Id'])assert.ok(!s.schema.some(x=>x.name===name));
}
function sameState(before,after){
 for(const key of ['tables','data','schema','objects','triggers','eventTriggers','rewriteRules','sequences','history','indexes'])assert.deepEqual(after[key],before[key],`Rollback difference: ${key}`);
}
function summarize(s){return {tables:s.tables.length,migrations:s.history.length,dataHash:s.dataHash,schemaHash:s.schemaHash,extraSchemaHash:s.extraSchemaHash,historyHash:hash(s.history),retainedTables:retained.map(t=>s.data.find(x=>x.table===t)),srcSequence:s.sequences.find(x=>x.name==='CollectionReferenceNumberSeq'),allocationIndexes:s.indexes.filter(i=>i.name.startsWith('IX_CollectionAllocations_MunicipalityId_Source')),parentKeys:s.schema.filter(x=>x.kind==='constraint'&&/^(AK|PK)_(Facilities|Stalls|Contracts)/.test(x.name))};}
async function readOnlyCheckpoint(cfg){
 const c=client(cfg,true);try{await c.connect();await c.query('BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY');const server=await identity(c,true);await noOthers(c);const state=await snapshot(c);assertBaseline(state);await c.query('ROLLBACK');return {server,state};}finally{await c.end();}
}
function backup(cfg){
 const stamp=new Date().toISOString().replace(/[:.]/g,'-');const file=path.join('C:/dev/stalltrack-v2-worktrees/local-db-backups',`EEMOCantilanSDS.DB_task2d2_full_${stamp}.dump`);
 assert.ok(!fs.existsSync(file));const env={...process.env,PGPASSWORD:cfg.password,PGCONNECT_TIMEOUT:'5',PGAPPNAME:'stalltrack_task2d2_full_backup'};
 const tool='C:/Program Files/PostgreSQL/18/bin/';
 const dump=cp.spawnSync(tool+'pg_dump.exe',['--host',cfg.host,'--port','5432','--username',cfg.user,'--dbname',cfg.database,'--format=custom','--file',file,'--no-password','--lock-wait-timeout=1500ms'],{env,windowsHide:true,timeout:120000});
 assert.equal(dump.status,0,'Fresh full pg_dump failed');
 const list=cp.spawnSync(tool+'pg_restore.exe',['--list',file],{encoding:'utf8',windowsHide:true,timeout:30000});assert.equal(list.status,0,'Archive listing failed');
 const decode=cp.spawnSync(tool+'pg_restore.exe',['--file','NUL',file],{windowsHide:true,timeout:120000});assert.equal(decode.status,0,'Full archive decoding failed');
 const tables=baseline.tableCount;const dataLines=list.stdout.split(/\r?\n/).filter(l=>/TABLE DATA public /.test(l));assert.equal(dataLines.length,tables);
 for(const t of [...retained,'__EFMigrationsHistory'])assert.ok(dataLines.some(l=>l.includes('TABLE DATA public '+t+' ')),'Missing historical archive data');
 return {path:file,createdAtUtc:new Date().toISOString(),bytes:fs.statSync(file).size,sha256:fileHash(file),full:true,archiveListExitCode:0,fullDecodeExitCode:0,publicTableDataEntries:dataLines.length,retainedAndHistoryDataEntries:14};
}

// Deterministic offline tests exercise this lifecycle. On every path close the original connection
// before the fresh verifier. Never retry a write or try to compensate for an uncertain rollback.
async function rollbackOnly(io){
 const state={transaction:'not_started',verification:'not_run',success:false};let before,result;
 try{
  before=await io.prepare();state.transaction='begin_attempted';await io.begin();state.transaction='begun';
  result=await io.exercise(before);state.transaction='rollback_attempted';
  await io.rollback();state.transaction='rollback_acknowledged';
 }catch(e){
  state.failure=e.code||e.name;if(e.name==='AssertionError')state.failedCheck=e.message;
  else if(e.code)state.databaseError={code:e.code,message:e.message}; // No row detail or connection secrets.
  if(state.transaction!=='not_started'){
   state.transaction='rollback_attempted';try{await io.rollback();state.transaction='rollback_acknowledged';}catch{state.transaction='rollback_outcome_unknown';}
  }
 }finally{
  try{await io.close();}catch{state.closeFailure=true;}
  if(before){try{state.postflight=await io.verify(before);state.verification='independently_verified_unchanged';}catch(e){state.verification='unresolved_or_changed';state.verificationFailure=e.code||e.name;}}
 }
 state.success=!state.failure&&!state.closeFailure&&state.transaction==='rollback_acknowledged'&&state.verification==='independently_verified_unchanged';
 return {state,result};
}

async function exercise(c,before,evidence){
 evidence.stage='seven_reviewed_operations';await noOthers(c);
 const statements=verifyDdl(fs.readFileSync(DDL,'utf8'),fileHash(DDL));
 evidence.ddlOperationsCompleted=[];
 for(const [index,statement] of statements.entries()){evidence.stage='DDL operation '+(index+1);await c.query(statement);evidence.ddlOperationsCompleted.push(index+1);}
 const columns=(await c.query("SELECT column_name,data_type,numeric_precision,numeric_scale FROM information_schema.columns WHERE table_schema='public' AND table_name='NpmRentMonths' ORDER BY ordinal_position")).rows;
 assert.equal(columns.length,26);
 const constraints=(await c.query("SELECT conname,contype,pg_get_constraintdef(oid) AS definition FROM pg_constraint WHERE conrelid='public.\"NpmRentMonths\"'::regclass ORDER BY conname")).rows;
 assert.equal(constraints.filter(x=>x.contype==='f').length,3);assert.equal(constraints.filter(x=>x.contype==='c').length,9);
 evidence.installed={operations:statements.length,columns,constraints};
 const ids=Object.fromEntries(['tenantA','tenantB','facilityA','facilityA2','facilityB','stallA','stallA2','stallB','contractA','contractA2','contractOtherStall','contractB','month','actor'].map(x=>[x,crypto.randomUUID()]));
 const stamp=new Date().toISOString(),label='TASK2D2 synthetic rollback only';
 async function insert(table,values){assert.ok(['Municipalities','Facilities','Stalls','Contracts','NpmRentMonths'].includes(table));const keys=Object.keys(values);return c.query(`INSERT INTO public.${qid(table)} (${keys.map(qid).join(',')}) VALUES (${keys.map((_,i)=>'$'+(i+1)).join(',')})`,Object.values(values));}
 evidence.stage='synthetic_parent_fixtures';
 const base={CreatedAt:stamp,CreatedBy:label,IsDeleted:false};
 for(const t of ['tenantA','tenantB'])await insert('Municipalities',{Id:ids[t],Code:'T2D2-'+ids[t].slice(0,8),Name:label,Province:'Synthetic',OfficeName:label,Status:1,IsDefault:false,IsActive:true,TenantCode:'task2d2-'+ids[t],...base});
 for(const [f,t,code] of [['facilityA','tenantA',1],['facilityA2','tenantA',2],['facilityB','tenantB',1]])await insert('Facilities',{Id:ids[f],MunicipalityId:ids[t],Code:code,Archetype:1,Name:label,ShortName:'T2D2',IsActive:true,MonthBasis:1,...base});
 for(const [s,f,t] of [['stallA','facilityA','tenantA'],['stallA2','facilityA2','tenantA'],['stallB','facilityB','tenantB']])await insert('Stalls',{Id:ids[s],MunicipalityId:ids[t],FacilityId:ids[f],StallNo:'TASK2D2-'+ids[s].slice(0,8),Status:2,Fees:1,Type:1,MonthlyRate:'900.00',DailyRate:'30.00',...base});
 for(const [contract,stall,t] of [['contractA','stallA','tenantA'],['contractA2','stallA','tenantA'],['contractOtherStall','stallA2','tenantA'],['contractB','stallB','tenantB']])await insert('Contracts',{Id:ids[contract],MunicipalityId:ids[t],StallId:ids[stall],ActualOccupant:label,NameOnContract:label,EffectivityDate:'2026-10-01',DurationYears:3,MonthlyRentalRate:'900.00',IsActive:true,Arrangement:1,...base});
 const row={Id:ids.month,MunicipalityId:ids.tenantA,FacilityId:ids.facilityA,StallId:ids.stallA,ContractId:ids.contractA,PeriodStart:'2026-10-01',OwnershipAsOf:'2026-10-01',AssessedAmount:'900.00',CreditedAmount:'0.00',MonthBasis:null,RateAsOf:'2026-10-01',PolicyReference:label,RateEvidenceJson:JSON.stringify({synthetic:true,amount:900}),CreditEvidenceJson:JSON.stringify({synthetic:true,credit:0}),Status:1,SettlementVersion:'1',CreatedByUserId:ids.actor,CreatedBy:label,CreatedAtUtc:stamp};
 await insert('NpmRentMonths',row);
 const cases=[];
 async function expected(name,changes,code,constraint){
  evidence.stage=name;
  await c.query('SAVEPOINT expected_case');let error;
  try{await insert('NpmRentMonths',{...row,Id:crypto.randomUUID(),PeriodStart:'2026-11-01',...changes});}catch(e){error=e;}
  // Always restore this case, including an unexpected successful INSERT.
  await c.query('ROLLBACK TO SAVEPOINT expected_case');await c.query('RELEASE SAVEPOINT expected_case');
  assert.ok(error,name+' unexpectedly accepted');assert.equal(error.code,code,name);if(constraint)assert.equal(error.constraint,constraint,name);
  cases.push({name,sqlstate:error.code,constraint:error.constraint});
 }
 const check=(name,changes,constraint)=>expected(name,changes,'23514','CK_NpmRentMonths_'+constraint);
 await expected('Duplicate tenant/stall/month',{PeriodStart:'2026-10-01'},'23505','IX_NpmRentMonths_MunicipalityId_StallId_PeriodStart');
 await expected('Contract change cannot duplicate physical month',{PeriodStart:'2026-10-01',ContractId:ids.contractA2},'23505','IX_NpmRentMonths_MunicipalityId_StallId_PeriodStart');
 await expected('Wrong-tenant facility',{FacilityId:ids.facilityB},'23503','FK_NpmRentMonths_Facilities_MunicipalityId_FacilityId');
 await expected('Wrong-parent stall/facility',{FacilityId:ids.facilityA2},'23503','FK_NpmRentMonths_Stalls_MunicipalityId_StallId_FacilityId');
 await expected('Wrong-tenant stall',{StallId:ids.stallB,ContractId:ids.contractB},'23503');
 await expected('Wrong-stall contract',{ContractId:ids.contractOtherStall},'23503','FK_NpmRentMonths_Contracts_MunicipalityId_ContractId_StallId');
 await expected('Wrong-tenant contract',{ContractId:ids.contractB},'23503','FK_NpmRentMonths_Contracts_MunicipalityId_ContractId_StallId');
 await check('Not first of month',{PeriodStart:'2026-11-02'},'Period');
 await check('Negative assessment',{AssessedAmount:'-1.00'},'Money');
 await check('Negative credit',{CreditedAmount:'-1.00'},'Money');
 await check('Credit exceeds assessment',{CreditedAmount:'900.01'},'Money');
 await check('Unsupported review state',{Status:3},'Review');
 await check('Unsupported basis',{MonthBasis:3},'Basis');
 await check('Empty policy',{PolicyReference:' '},'Evidence');
 await check('Empty rate evidence',{RateEvidenceJson:'{}'},'Evidence');
 await check('Array rate evidence',{RateEvidenceJson:'[]'},'Evidence');
 await check('Empty credit evidence',{CreditEvidenceJson:'{}'},'Evidence');
 await check('Array credit evidence',{CreditEvidenceJson:'[]'},'Evidence');
 await expected('Malformed JSON evidence',{RateEvidenceJson:'{'},'22P02');
 await expected('Null required evidence',{RateEvidenceJson:null},'23502');
 await check('Unreconciled row cannot carry review evidence',{ReconciliationEvidence:'unexpected'},'Review');
 await check('Reconciled row requires review fields',{Status:2,MonthBasis:1},'Review');
 await check('Unknown basis cannot be reconciled',{Status:2,ReconciledByUserId:ids.actor,ReconciledAtUtc:stamp,ReconciliationEvidence:label},'Review');
 await check('Review cannot predate creation',{Status:2,MonthBasis:1,ReconciledByUserId:ids.actor,ReconciledAtUtc:'2020-01-01T00:00:00Z',ReconciliationEvidence:label},'Review');
 await check('Zero version',{SettlementVersion:0},'Version');
 await check('Empty actor identity',{CreatedByUserId:'00000000-0000-0000-0000-000000000000'},'Identity');
 await check('Missing revision reason',{UpdatedAtUtc:stamp,UpdatedByUserId:ids.actor,UpdatedBy:label},'RevisionReason');
 await check('Incomplete update actor',{UpdatedAtUtc:stamp,LastRevisionReason:label},'Updated');
 await expected('Numeric overflow',{AssessedAmount:'10000000000000000.00'},'22003');
 await c.query('SAVEPOINT fractional_cent');
 const fractionalId=crypto.randomUUID();await insert('NpmRentMonths',{...row,Id:fractionalId,PeriodStart:'2026-12-01',AssessedAmount:'900.001'});
 const rounded=(await c.query('SELECT "AssessedAmount"::text AS amount FROM public."NpmRentMonths" WHERE "Id"=$1',[fractionalId])).rows[0].amount;
 assert.equal(rounded,'900.00');await c.query('ROLLBACK TO SAVEPOINT fractional_cent');await c.query('RELEASE SAVEPOINT fractional_cent');
 evidence.fractionalCentBoundary={input:'900.001',storedByPostgres:rounded,requiresDomainRejection:true};
 await insert('NpmRentMonths',{...row,Id:crypto.randomUUID(),PeriodStart:'2026-11-01',MonthBasis:1,Status:2,ReconciledByUserId:ids.actor,ReconciledAtUtc:stamp,ReconciliationEvidence:label});
 await insert('NpmRentMonths',{...row,Id:crypto.randomUUID(),MunicipalityId:ids.tenantB,FacilityId:ids.facilityB,StallId:ids.stallB,ContractId:ids.contractB});
 await insert('NpmRentMonths',{...row,Id:crypto.randomUUID(),PeriodStart:'2026-12-01',MonthBasis:2,AssessedAmount:'1900.25',CreditedAmount:'25.25'});
 const after=await snapshot(c);const changed=after.data.filter(x=>before.data.find(t=>t.table===x.table)?.fingerprint!==x.fingerprint);
 assert.deepEqual(changed.map(x=>x.table).sort(),['Contracts','Facilities','Municipalities','NpmRentMonths','Stalls']);
 assert.deepEqual(after.history,before.history);assert.deepEqual(after.sequences,before.sequences);
 for(const t of retained)assert.deepEqual(after.data.find(x=>x.table===t),before.data.find(x=>x.table===t));
 for(const idx of before.indexes)assert.deepEqual(after.indexes.find(x=>x.name===idx.name),idx);
 const newIndexes=after.indexes.filter(x=>!before.indexes.some(b=>b.name===x.name));assert.equal(newIndexes.length,8); // 2 parent keys, PK/AK, 4 indexes.
 await noOthers(c);
 evidence.stage='exercise_complete';evidence.expectedFailures=cases;evidence.validRows=4;evidence.onlyChangedTables=changed.map(x=>x.table);evidence.allOriginalIndexesPreserved=true;evidence.noFinancialRowsChanged=true;evidence.noSequenceChanged=true;
 evidence.scopeLimits=['No payment workflow, SRC posting, remittance or concurrency execution','Database FK membership does not authorize owner/facility type or approved rate','Precision coercion requires existing domain validation','Preparation/reconciliation states do not permit collection','No generic tenant restore executed; retained-table guard remains mandatory'];
 return evidence;
}

async function main(){
 const mode=process.argv[2];assert.ok(['preflight','rehearse'].includes(mode),'Use preflight or rehearse');
 const cfg=config();apiInactive();verifyDdl(fs.readFileSync(DDL,'utf8'),fileHash(DDL));
 const checkpoint=await readOnlyCheckpoint(cfg);const runId=new Date().toISOString().replace(/[:.]/g,'-');
 const evidence={task:'2D.2',mode,startedAtUtc:new Date().toISOString(),scriptSha256:fileHash(__filename),ddlSha256:DDL_HASH,preflight:{server:checkpoint.server,...summarize(checkpoint.state)},apiInactive:true};
 if(mode==='preflight'){
  const c=client(cfg,true);try{await c.connect();await c.query('BEGIN READ ONLY');
   evidence.parentColumns=(await c.query("SELECT table_name,column_name,data_type,is_nullable,column_default FROM information_schema.columns WHERE table_schema='public' AND table_name IN ('Municipalities','Facilities','Stalls','Contracts') ORDER BY table_name,ordinal_position")).rows;
   await c.query('ROLLBACK');}finally{await c.end();}
  durable(path.join(DIR,'preflight-'+runId+'.json'),evidence);console.log(JSON.stringify({status:'read_only_preflight_passed',...evidence.preflight}));return;
 }
 assert.equal(process.env.STALLTRACK_TASK2D2_AUTHORIZATION,AUTH,'Explicit rollback-only authorization acknowledgement required');
 evidence.backup=backup(cfg);apiInactive();
 const c=client(cfg,false);let closed=false;
 const result=await rollbackOnly({
  prepare:async()=>{assert.equal(fileHash(evidence.backup.path),evidence.backup.sha256);await c.connect();await c.query('BEGIN READ ONLY');evidence.writer=await identity(c,true);await noOthers(c);const s=await snapshot(c);assertBaseline(s);sameState(checkpoint.state,s);await c.query('ROLLBACK');return s;},
  begin:async()=>{await c.query('BEGIN ISOLATION LEVEL REPEATABLE READ');evidence.mutationServer=await identity(c,false);await noOthers(c);
   assert.equal((await c.query('SELECT pg_try_advisory_xact_lock(8472113509001) AS acquired')).rows[0].acquired,true);
   await c.query('SET LOCAL search_path = public, pg_catalog');
   assert.equal((await c.query('SELECT current_schema() AS schema')).rows[0].schema,'public','DDL creation schema must be public');
   assert.equal((await c.query("SELECT count(*)::int AS n FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname IN ('length','btrim','jsonb_typeof')")).rows[0].n,0,'Public functions could shadow foundation checks');
   await c.query('LOCK TABLE '+checkpoint.state.tables.map(t=>'public.'+qid(t)).join(',')+' IN SHARE ROW EXCLUSIVE MODE NOWAIT');
   const locked=await snapshot(c);assertBaseline(locked);sameState(checkpoint.state,locked);
   durable(path.join('C:/dev/stalltrack-v2-worktrees/local-db-backups','task2d2-intent-'+runId+'.json'),{task:'2D.2',action:'seven operations and synthetic fixtures, final ROLLBACK only',scriptSha256:fileHash(__filename),ddlSha256:DDL_HASH,backup:evidence.backup,before:summarize(locked)});
  },
  exercise:async before=>exercise(c,before,evidence),
  rollback:async()=>c.query('ROLLBACK'),
  close:async()=>{if(!closed){closed=true;await c.end();}},
  verify:async before=>{apiInactive();const post=await readOnlyCheckpoint(cfg);assert.notEqual(post.server.pid,evidence.writer.pid);sameState(before,post.state);return {server:post.server,...summarize(post.state)};}
 });
 evidence.execution=result.state;evidence.finishedAtUtc=new Date().toISOString();
 durable(path.join(DIR,'result-'+runId+'.json'),evidence);
 console.log(JSON.stringify({execution:result.state,backup:evidence.backup,cases:evidence.expectedFailures?.length}));
 if(!result.state.success)process.exitCode=1;
}
// Read-only diagnostics are deliberately separate from the write lifecycle; never escalate or retry.
async function diagnosePermissions(){
 const c=client(config(),true);
 try{
  await c.connect();await c.query('BEGIN READ ONLY');const server=await identity(c,true);await noOthers(c);
  const tables=(await c.query(`SELECT c.relname AS table_name,pg_get_userbyid(c.relowner)=current_user AS owned_by_current_role,
   has_table_privilege(c.oid,'SELECT') AS can_select,has_table_privilege(c.oid,'INSERT') AS can_insert,
   has_table_privilege(c.oid,'UPDATE') AS can_update,has_table_privilege(c.oid,'DELETE') AS can_delete,
   has_table_privilege(c.oid,'TRUNCATE') AS can_truncate
   FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p') ORDER BY c.relname`)).rows;
  await c.query('SET LOCAL search_path = pg_catalog, public');
  const privileges=(await c.query(`SELECT current_schema() AS failed_harness_creation_schema,
   has_schema_privilege(current_schema(),'CREATE') AS can_create_in_failed_harness_schema,
   current_setting('allow_system_table_mods') AS allow_system_table_mods,
   has_schema_privilege('public','CREATE') AS can_create_in_public,
   has_function_privilege('pg_catalog.pg_try_advisory_xact_lock(bigint)','EXECUTE') AS can_acquire_migration_lock,
   current_setting('default_transaction_read_only') AS default_read_only`)).rows[0];
  await c.query('ROLLBACK');const evidence={task:'2D.2',scope:'read-only privilege diagnosis after 42501; no retry or role changes',server,privileges,
   tablesWithoutWriteLockPrivilege:tables.filter(t=>!t.can_update&&!t.can_delete&&!t.can_truncate),parentPrivileges:tables.filter(t=>['Facilities','Stalls','Contracts','Municipalities'].includes(t.table_name))};
  durable(path.join(DIR,'permissions-'+new Date().toISOString().replace(/[:.]/g,'-')+'.json'),evidence);return evidence;
 }finally{await c.end();}
}
module.exports={verifyDdl,rollbackOnly,sameState,DDL_HASH,diagnosePermissions};
if(require.main===module)main().catch(e=>{console.error(JSON.stringify({outcome:'blocked_or_unverified_do_not_retry_writes',code:e.code||e.name,check:e.name==='AssertionError'?e.message:undefined}));process.exitCode=1;});
