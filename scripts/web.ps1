$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$localNode = Join-Path $PWD '.tools\node\node-v24.21.0-win-x64'
if (Test-Path "$localNode\node.exe") { $env:PATH = "$localNode;$env:PATH" }
npm run dev --prefix web
exit $LASTEXITCODE
