if (process.env.STALLTRACK_TASK2C3_AUTHORIZATION !== 'rollback-only-local-development') throw Error('Explicit Task 2C.3 rollback-only acknowledgement required');
const fs=require('fs'),path=require('path');
const {Client}=require('C:/dev/stalltrack/eemo/tools/postgres-dev-mcp/node_modules/pg');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const root='C:/dev/stalltrack/eemo';
const configs=['EEMOCantilanSDS.Api/appsettings.json','EEMOCantilanSDS.Api/appsettings.Development.json'].map(p=>read(path.join(root,p)));
const secrets=path.join(process.env.APPDATA,'Microsoft/UserSecrets/9c52f74a-9378-4de3-b669-9ac14e61ef44/secrets.json');
if(fs.existsSync(secrets))configs.push(read(secrets));
let value;for(const c of configs)value=c.ConnectionStrings?.DefaultConnection||c['ConnectionStrings:DefaultConnection']||value;
value=process.env.ConnectionStrings__DefaultConnection||value;
const f={};for(const m of value.matchAll(/(?:^|;)\s*([^=;]+)\s*=\s*("(?:[^"]|"")*"|'(?:[^']|'')*'|[^;]*)/g)){let v=m[2].trim();if((v.startsWith('"')&&v.endsWith('"'))||(v.startsWith("'")&&v.endsWith("'")))v=v.slice(1,-1);f[m[1].trim().toLowerCase()]=v;}
if(!['localhost','127.0.0.1','::1'].includes(f.host)||f.database!=='EEMOCantilanSDS.DB'||Number(f.port||5432)!==5432)throw Error('Authorized local target mismatch');
const cfg={host:f.host,port:Number(f.port||5432),database:f.database,user:f.username||f['user id'],password:f.password};
module.exports={cfg,client:(readonly=true)=>new Client({...cfg,connectionTimeoutMillis:5000,application_name:'stalltrack_task2c3_'+(readonly?'readonly':'rollback_rehearsal'),options:`-c default_transaction_read_only=${readonly?'on':'off'} -c statement_timeout=15000 -c lock_timeout=1500 -c idle_in_transaction_session_timeout=30000`})};
