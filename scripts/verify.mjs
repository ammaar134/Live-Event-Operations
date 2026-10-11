import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';
function run(program,args,cwd=process.cwd()) { const r=spawnSync(program,args,{cwd,stdio:'inherit',env:process.env,windowsHide:true}); if(r.error) throw r.error; if(r.status) process.exit(r.status); }
const dotnet=process.env.DOTNET || 'dotnet';
run(dotnet,['restore','tests/Api.Tests','--locked-mode']);
run(dotnet,['format','src/Api/Api.csproj','--no-restore','--verify-no-changes']);
run(dotnet,['format','tests/Api.Tests/Api.Tests.csproj','--no-restore','--verify-no-changes']);
run(dotnet,['test','tests/Api.Tests','--no-restore','--logger','console;verbosity=minimal']);
for(const [command,args] of [['prettier',['--check','.']],['eslint',['.']],['tsc',['-b']],['vite',['build']],['vitest',['run']]]) {
  const entry={prettier:'prettier/bin/prettier.cjs',eslint:'eslint/bin/eslint.js',tsc:'typescript/bin/tsc',vite:'vite/bin/vite.js',vitest:'vitest/vitest.mjs'}[command];
  run(process.execPath,[resolve('web/node_modules',entry),...args],resolve('web'));
}
run(process.execPath,['scripts/browser-tests.mjs']);
run(process.execPath,['scripts/scan-secrets.mjs','--history']);
run(process.execPath,['scripts/dependency-audit.mjs']);
console.log('Aggregate verification passed. Docker image/Compose and remote CI are recorded separately.');
