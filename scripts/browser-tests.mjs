// Self-contained browser suite against a fresh database and dedicated loopback ports.
import { spawn, spawnSync } from 'node:child_process';
import { readFileSync, mkdirSync, createWriteStream } from 'node:fs';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import { createServer } from 'node:net';
import { randomBytes } from 'node:crypto';
const require = createRequire(resolve('web/package.json')); const { Client } = require('pg');
const local = {};
try { for (const line of readFileSync('.env','utf8').split(/\r?\n/)) { const i=line.indexOf('='); if(i>0) local[line.slice(0,i)]=line.slice(i+1); } } catch { /* CI passes credentials via environment */ }
const adminUrl = process.env.E2E_ADMIN_URL || `postgres://fieldroom:${local.POSTGRES_PASSWORD}@127.0.0.1:55435/postgres`;
const admin = new Client({ connectionString: adminUrl }); await admin.connect();
const name = `fieldroom_e2e_${randomBytes(8).toString('hex')}`; const dbUrl = new URL(adminUrl); dbUrl.pathname = `/${name}`;
const deskPassword = `Fr!${randomBytes(24).toString('hex')}a`;
const apiPort = 5095, webPort = 5185;
for (const port of [apiPort,webPort]) await new Promise((ok,no) => { const server=createServer(); server.once('error',no); server.listen(port,'127.0.0.1',()=>server.close(ok)); });
await admin.query(`CREATE DATABASE ${name}`);
mkdirSync('.local',{recursive:true}); const log = createWriteStream('.local/browser-services.log');
const env = { ...process.env, ASPNETCORE_ENVIRONMENT:'Development', ASPNETCORE_URLS:`http://127.0.0.1:${apiPort}`, AutoMigrate:'true', BootstrapPassword:deskPassword,
  ConnectionStrings__Library:`Host=${dbUrl.hostname};Port=${dbUrl.port || 5432};Database=${name};Username=${decodeURIComponent(dbUrl.username)};Password=${decodeURIComponent(dbUrl.password)}`, DataProtectionPath:resolve('.local/e2e-keys') };
const api = spawn(process.env.DOTNET || 'dotnet',[resolve('src/Api/bin/Debug/net10.0/Api.dll')],{env,cwd:resolve('src/Api'),windowsHide:true,stdio:['ignore','pipe','pipe']}); api.stdout.pipe(log);api.stderr.pipe(log);
const web = spawn(process.execPath,[resolve('web/node_modules/vite/bin/vite.js'),'--host','127.0.0.1','--port',String(webPort),'--strictPort'],{env:{...process.env,VITE_API_TARGET:`http://127.0.0.1:${apiPort}`},cwd:resolve('web'),windowsHide:true,stdio:['ignore','pipe','pipe']}); web.stdout.pipe(log);web.stderr.pipe(log);
async function ready(url) { for(let i=0;i<100;i++){ try { if((await fetch(url)).ok) return; }catch{} await new Promise(r=>setTimeout(r,200)); } throw new Error(`Not ready: ${url}; inspect .local/browser-services.log`); }
let code=1;
try {
  await ready(`http://127.0.0.1:${apiPort}/health/ready`); await ready(`http://127.0.0.1:${webPort}`);
  code = await new Promise(resolveExit=>{const test=spawn(process.execPath,[resolve('web/node_modules/@playwright/test/cli.js'),'test'],{cwd:resolve('web'),env:{...process.env,BASE_URL:`http://127.0.0.1:${webPort}`,E2E_DATABASE_URL:dbUrl.href,E2E_DESK_PASSWORD:deskPassword},windowsHide:true,stdio:'inherit'});test.on('exit',resolveExit);});
} finally {
  for(const child of [web,api]) { if(process.platform==='win32') spawnSync('taskkill',['/pid',String(child.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'}); else child.kill('SIGTERM'); }
  await admin.query(`DROP DATABASE ${name} WITH (FORCE)`); await admin.end(); log.end();
}
process.exitCode=code || 0;
