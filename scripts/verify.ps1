$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
if (Test-Path .tools\dotnet\dotnet.exe) { $env:DOTNET_ROOT = "$PWD\.tools\dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH" }
$localNode = Join-Path $PWD '.tools\node\node-v24.21.0-win-x64'
if (Test-Path "$localNode\node.exe") { $env:PATH = "$localNode;$env:PATH" }
if (Test-Path .env) {
    Get-Content .env | ForEach-Object { $k,$v = $_.Split('=',2); if ($k -eq 'POSTGRES_PASSWORD' -and !$env:TEST_POSTGRES) { $env:TEST_POSTGRES = "Host=127.0.0.1;Port=55435;Database=postgres;Username=fieldroom;Password=$v" } }
}
node scripts/verify.mjs
exit $LASTEXITCODE
