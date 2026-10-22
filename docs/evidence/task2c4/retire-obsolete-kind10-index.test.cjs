'use strict';
// Deterministic offline fault injection. Import does not load pg, resolve secrets or connect.
const {test}=require('node:test');const assert=require('node:assert/strict');
const {executeCorrection,assertSingleIndexDelta,assertServerIdentity,assertPriorAttemptSafe}=require('./retire-obsolete-kind10-index.cjs');
function fixture(fail={}){
 const calls={drop:0,commit:0,verify:0,abort:0,intent:0,close:0,result:0};let server='original',intentDurable=false,closed=false;
 const error=code=>Object.assign(new Error('Synthetic offline error'),{code});
 const io={
  prepare:async()=>{if(fail.preflight)throw error(fail.preflight);return {baseline:{dataHash:'same'},backup:{verified:true}};},
  mutate:async()=>{calls.drop++;server='uncommitted';if(fail.beforeCommit)throw error('BEFORE_COMMIT');return {schemaHash:'only-index-removed'};},
  persistIntent:async()=>{calls.intent++;if(fail.intent)throw error('ENOSPC');intentDurable=true;},
  commit:async()=>{calls.commit++;assert.equal(intentDurable,true);if(fail.commitNotApplied){server='original';throw error('ECONNRESET');}server='committed';if(fail.lostAck)throw error('ECONNRESET');},
  abort:async()=>{calls.abort++;server='original';},
  closeWriter:async()=>{calls.close++;closed=true;},
  verify:async()=>{calls.verify++;assert.equal(closed,true);if(fail.reconnect)throw error('ECONNREFUSED');if(fail.dataMismatch)assert.fail('Synthetic data mismatch');return {state:fail.unexpected?'unexpected':server==='committed'?'committed':'unchanged',evidence:{freshConnection:true}};},
  persistResult:async state=>{calls.result++;if(fail.evidence)throw error('ENOSPC');}
 };return {io,calls,getServer:()=>server};
}
for(const reason of ['PREFLIGHT_FAILURE','INDEX_DEFINITION_MISMATCH','BACKUP_VERIFICATION_FAILURE'])test(reason+' aborts before mutation',async()=>{
 const f=fixture({preflight:reason}),r=await executeCorrection(f.io);assert.equal(r.commit,'not_attempted');assert.equal(r.success,false);assert.equal(f.calls.drop,0);assert.equal(f.calls.commit,0);
});
test('failure before COMMIT rolls back without attempting COMMIT',async()=>{const f=fixture({beforeCommit:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'not_attempted');assert.equal(f.calls.abort,1);assert.equal(f.getServer(),'original');assert.equal(f.calls.commit,0);});
test('failed durable intent prevents COMMIT and restores uncommitted DROP',async()=>{const f=fixture({intent:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'not_attempted');assert.equal(f.calls.commit,0);assert.equal(f.getServer(),'original');});
test('acknowledged COMMIT is successful only after independent verification',async()=>{const f=fixture(),r=await executeCorrection(f.io);assert.equal(r.commit,'acknowledged');assert.equal(r.outcome,'independently_verified_committed');assert.equal(r.success,true);assert.equal(f.calls.verify,1);});
test('server COMMIT with lost acknowledgement is verified without retry',async()=>{const f=fixture({lostAck:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'attempted_outcome_unknown');assert.equal(r.outcome,'independently_verified_committed');assert.equal(r.success,true);assert.equal(f.calls.drop,1);assert.equal(f.calls.commit,1);assert.equal(f.calls.abort,0);});
test('failed COMMIT not applied is independently verified unchanged',async()=>{const f=fixture({commitNotApplied:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'attempted_outcome_unknown');assert.equal(r.outcome,'independently_verified_unchanged');assert.equal(r.success,false);assert.equal(f.calls.drop,1);assert.equal(f.calls.commit,1);assert.equal(f.calls.abort,0);});
test('unavailable verifier preserves acknowledged COMMIT and requires reconciliation',async()=>{const f=fixture({reconnect:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'acknowledged');assert.equal(r.verification,'unavailable');assert.equal(r.success,false);assert.equal(f.calls.abort,0);});
test('lost acknowledgement plus unavailable verifier remains explicitly unknown',async()=>{const f=fixture({lostAck:true,reconnect:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'attempted_outcome_unknown');assert.equal(r.outcome,'unresolved_outcome_manual_reconciliation');assert.equal(r.success,false);assert.equal(f.calls.commit,1);});
test('evidence write failure cannot relabel verified COMMIT as rollback',async()=>{const f=fixture({evidence:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'acknowledged');assert.equal(r.outcome,'independently_verified_committed');assert.equal(r.evidenceWriteFailed,true);assert.equal(r.success,false);assert.equal(f.getServer(),'committed');assert.equal(f.calls.abort,0);});
test('unexpected schema fails verification without compensating DDL',async()=>{const f=fixture({unexpected:true}),r=await executeCorrection(f.io);assert.equal(r.commit,'acknowledged');assert.equal(r.outcome,'manual_reconciliation_required');assert.equal(r.success,false);assert.equal(f.calls.drop,1);assert.equal(f.calls.abort,0);});
test('unexpected data difference retains acknowledged COMMIT and fails closed',async()=>{const f=fixture({dataMismatch:true}),r=await executeCorrection(f.io);assert.equal(r.verification,'unexpected');assert.equal(r.commit,'acknowledged');assert.equal(r.success,false);});
const expected={kind:'index',name:'IX_CollectionAllocations_MunicipalityId_SourceKind_SourceId',definition:'reviewed-definition'};
test('exact single-index removal passes delta guard',()=>assertSingleIndexDelta({schema:[expected]},{schema:[]},expected.definition));
test('mismatched index definition fails delta guard',()=>assert.throws(()=>assertSingleIndexDelta({schema:[{...expected,definition:'different'}]},{schema:[]},expected.definition)));
test('unrelated schema removal fails delta guard',()=>assert.throws(()=>assertSingleIndexDelta({schema:[expected,{kind:'table',name:'retained',definition:'keep'}]},{schema:[]},expected.definition)));
test('unexpected added schema fails delta guard',()=>assert.throws(()=>assertSingleIndexDelta({schema:[expected]},{schema:[{kind:'index',name:'other',definition:'unexpected'}]},expected.definition)));
test('normalized IPv6 loopback identity is accepted',()=>assertServerIdentity({db:'EEMOCantilanSDS.DB',address:'::1',port:5432,version:180004}));
test('normalized IPv4 loopback identity is accepted',()=>assertServerIdentity({db:'EEMOCantilanSDS.DB',address:'127.0.0.1',port:5432,version:180004}));
test('nonloopback identity is refused',()=>assert.throws(()=>assertServerIdentity({db:'EEMOCantilanSDS.DB',address:'192.0.2.1',port:5432,version:180004})));
test('wrong database identity is refused',()=>assert.throws(()=>assertServerIdentity({db:'other',address:'::1',port:5432,version:180004})));
const safePrior={execution:{commit:'not_attempted',outcome:'not_committed_aborted',error:{stage:'preflight'}}};
test('reviewed preflight-only failure can be resumed without overwriting evidence',()=>assertPriorAttemptSafe(safePrior,[{event:'result'}]));
test('prior durable COMMIT intent prevents repeat execution',()=>assert.throws(()=>assertPriorAttemptSafe(safePrior,[{event:'commit_intent'}])));
test('uncertain prior COMMIT prevents repeat execution',()=>assert.throws(()=>assertPriorAttemptSafe({execution:{commit:'attempted_outcome_unknown'}},[])));
test('acknowledged prior COMMIT prevents repeat execution',()=>assert.throws(()=>assertPriorAttemptSafe({execution:{commit:'acknowledged'}},[])));
