'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const {postflight,artifacts}=require('./install-foundation.cjs');
function fixture(){
 const before={tables:['Contracts','__EFMigrationsHistory'],history:[{MigrationId:'old',ProductVersion:'9.0.4'}],
  data:[{table:'Contracts',rows:'1',fingerprint:'unchanged'},{table:'__EFMigrationsHistory',rows:'1',fingerprint:'old'}],
  schema:[{kind:'column',name:'Contracts.Id',definition:'uuid'}],objects:[{relname:'Contracts'}],
  indexes:[{name:'lookup',valid:true,ready:true}],sequences:[{name:'SRC',last_value:'17',is_called:true}],triggers:[],eventTriggers:[],rules:[]};
 const after=structuredClone(before);after.tables.push('NpmRentMonths');after.tables.sort();
 after.history.push({MigrationId:'20261017040000_AddNpmRentMonthFoundation',ProductVersion:'9.0.4'});after.history.sort((a,b)=>a.MigrationId.localeCompare(b.MigrationId));
 after.data[1]={table:'__EFMigrationsHistory',rows:'2',fingerprint:'new'};after.data.push({table:'NpmRentMonths',rows:'0'});
 for(let i=0;i<26;i++)after.schema.push({kind:'column',name:'NpmRentMonths.c'+i,definition:'column'});
 const parents=['AK_Facilities_MunicipalityId_Id','AK_Stalls_MunicipalityId_Id_FacilityId'];
 const names=[...parents,...Array.from({length:6},(_,i)=>'IX_NpmRentMonths_'+i)];
 for(const name of names){after.schema.push({kind:'index',name,definition:'index'});after.objects.push({relname:name});}
 after.objects.push({relname:'NpmRentMonths'});
 for(const name of parents)after.schema.push({kind:'constraint',name,definition:'UNIQUE'});
 const recorded=require('../task2d2/result-2026-10-17T03-12-22-079Z.json').installed.constraints;
 for(const x of recorded)after.schema.push({kind:'constraint',name:x.conname,definition:x.definition});
 return {before,after};
}
test('exact promoted artifacts retain approved hashes',()=>artifacts());
test('expected additive state passes independent comparison',()=>{const {before,after}=fixture();assert.equal(postflight(before,after).originalDataUnchanged,true);});
for(const [name,alter] of [
 ['financial row change',s=>s.data[0].fingerprint='changed'],
 ['SRC sequence advancement',s=>s.sequences[0].last_value='18'],
 ['old history rewrite',s=>s.history.find(x=>x.MigrationId==='old').ProductVersion='changed'],
 ['extra schema object',s=>s.schema.push({kind:'index',name:'unapproved',definition:'index'})],
 ['retained index removal',s=>s.indexes=[]],
 ['unexpected user trigger',s=>s.triggers.push({name:'unexpected'})],
 ['nonempty month table',s=>s.data.find(x=>x.table==='NpmRentMonths').rows='1'],
 ['missing parent key',s=>s.schema=s.schema.filter(x=>x.name!=='AK_Facilities_MunicipalityId_Id')]
 ,['changed NOT NULL evidence',s=>s.schema.find(x=>x.name==='NpmRentMonths_AssessedAmount_not_null').definition='NOT NULL "Other"']
])test('rejects '+name,()=>{const {before,after}=fixture();alter(after);assert.throws(()=>postflight(before,after));});
