'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {verifyDdl,rollbackOnly,sameState,DDL_HASH}=require('./rehearsal.cjs');
function harness(fail){
 const events=[];const io={};
 for(const name of ['prepare','begin','exercise','rollback','close','verify'])io[name]=async()=>{
  events.push(name);if(fail===name)throw Error('simulated '+name);return name==='prepare'?{original:true}:name==='verify'?{unchanged:true}:undefined;
 };
 return {events,io};
}
test('exact reviewed DDL has seven operations, three restrictive FKs and nine checks',()=>{
 const file=path.join(__dirname,'../task2d/NpmRentMonthFoundation.Up.sql.review'),text=fs.readFileSync(file,'utf8');
 assert.equal(verifyDdl(text,crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')).length,7);
});
test('changed DDL is refused before execution',()=>assert.throws(()=>verifyDdl('DROP TABLE "Collections";','wrong')));
test('additional operation cannot masquerade as reviewed shape',()=>{
 const text=fs.readFileSync(path.join(__dirname,'../task2d/NpmRentMonthFoundation.Up.sql.review'),'utf8');
 assert.throws(()=>verifyDdl(text+'\nSELECT 1;',DDL_HASH));
});
test('acknowledged rollback closes original before independent verifier',async()=>{
 const {events,io}=harness();const r=await rollbackOnly(io);
 assert.equal(r.state.success,true);assert.deepEqual(events,['prepare','begin','exercise','rollback','close','verify']);
});
test('preflight or backup failure cannot begin transaction',async()=>{
 const {events,io}=harness('prepare');const r=await rollbackOnly(io);
 assert.equal(r.state.success,false);assert.deepEqual(events,['prepare','close']);assert.equal(r.state.transaction,'not_started');
});
test('failure during BEGIN or lock acquisition still requests explicit rollback',async()=>{
 const {events,io}=harness('begin');const r=await rollbackOnly(io);
 assert.equal(r.state.success,false);assert.deepEqual(events,['prepare','begin','rollback','close','verify']);
});
test('constraint scenario failure is rolled back and independently checked, never retried',async()=>{
 const {events,io}=harness('exercise');const r=await rollbackOnly(io);
 assert.equal(r.state.success,false);assert.equal(r.state.verification,'independently_verified_unchanged');
 assert.equal(events.filter(x=>x==='exercise').length,1);assert.deepEqual(events.slice(-3),['rollback','close','verify']);
});
test('lost rollback acknowledgement never reports success even if fresh check is unchanged',async()=>{
 const {events,io}=harness('rollback');const r=await rollbackOnly(io);
 assert.equal(r.state.success,false);assert.equal(r.state.transaction,'rollback_outcome_unknown');assert.equal(r.state.verification,'independently_verified_unchanged');
 assert.equal(events.filter(x=>x==='exercise').length,1);
});
test('independent verifier unavailable is unresolved',async()=>{
 const {io}=harness('verify');const r=await rollbackOnly(io);assert.equal(r.state.success,false);assert.equal(r.state.verification,'unresolved_or_changed');
});
test('schema/data/sequence differences cannot pass rollback verification',async()=>{
 const {io}=harness();io.verify=async()=>sameState({tables:[],data:[],schema:[],objects:[],triggers:[],sequences:[{last_value:'17'}],history:[],indexes:[]},{tables:[],data:[],schema:[],objects:[],triggers:[],sequences:[{last_value:'18'}],history:[],indexes:[]});
 const r=await rollbackOnly(io);assert.equal(r.state.success,false);assert.equal(r.state.verification,'unresolved_or_changed');
});
test('close failure is not silently treated as successful cleanup',async()=>{
 const {io}=harness('close');const r=await rollbackOnly(io);assert.equal(r.state.success,false);assert.equal(r.state.closeFailure,true);
});
test('runner contains no commit, sequence mutation, destructive or concurrent-index SQL',()=>{
 const source=fs.readFileSync(path.join(__dirname,'rehearsal.cjs'),'utf8');
 assert.doesNotMatch(source,/\.query\(\s*['"`]\s*(COMMIT|END\s+TRANSACTION|DROP\s|DELETE\s|TRUNCATE\s|ALTER\s+SEQUENCE|SELECT\s+(nextval|setval))/i);
 assert.doesNotMatch(source,/CONCURRENTLY/);
 assert.match(source,/assert\.equal\(process\.env\.STALLTRACK_TASK2D2_AUTHORIZATION,AUTH/);
});
test('unqualified review DDL is restricted to public creation schema',()=>{
 const source=fs.readFileSync(path.join(__dirname,'rehearsal.cjs'),'utf8');
 assert.match(source,/await c\.query\('SET LOCAL search_path = public, pg_catalog'\)/);
 assert.match(source,/current_schema\(\) AS schema/);
 assert.match(source,/DDL creation schema must be public/);
 assert.match(source,/Public functions could shadow foundation checks/);
});
