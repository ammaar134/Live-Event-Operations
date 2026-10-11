import { spawnSync } from 'node:child_process';
import { resolve, dirname } from 'node:path';
const npm = resolve(dirname(process.execPath), process.platform === 'win32' ? 'node_modules/npm/bin/npm-cli.js' : '../lib/node_modules/npm/bin/npm-cli.js');
const audit=spawnSync(process.execPath,[npm,'audit','--audit-level=low'],{cwd:resolve('web'),stdio:'inherit',windowsHide:true});
if(audit.status) process.exit(audit.status);
for(const project of ['src/Api','tests/Api.Tests']) {
  const r=spawnSync(process.env.DOTNET || 'dotnet',['list',project,'package','--vulnerable','--include-transitive','--format','json'],{encoding:'utf8',windowsHide:true});
  if(r.status) { console.error(r.stderr); process.exit(r.status); }
  const data=JSON.parse(r.stdout.slice(r.stdout.indexOf('{')));
  const issues=(data.projects || []).flatMap(p=>(p.frameworks || []).flatMap(f=>[...(f.topLevelPackages || []),...(f.transitivePackages || [])]));
  if(issues.length) { console.error(JSON.stringify(issues)); process.exit(1); }
  console.log(`${project}: no reported vulnerable NuGet packages, including transitive dependencies.`);
}
