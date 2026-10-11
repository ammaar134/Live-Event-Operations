param([switch]$Native)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
New-Item -ItemType Directory -Force .local | Out-Null
if (!(Test-Path .env)) {
    $dbPassword = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $deskPassword = 'Fr!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(18)) + 'z'
    "POSTGRES_PASSWORD=$dbPassword`nBOOTSTRAP_PASSWORD=$deskPassword" | Set-Content .env
}
Get-Content .env | ForEach-Object { $k,$v = $_.Split('=',2); if ($k -match '^[A-Z_]+$') { [Environment]::SetEnvironmentVariable($k,$v,'Process') } }
if (!$Native) {
    docker compose up --build -d
    if ($LASTEXITCODE) { throw 'Docker Compose failed. Start Docker Desktop or use the documented native option.' }
    Write-Output 'Fieldroom: http://localhost:5175. Manager: desk@fieldroom.example. Password is in ignored .env.'
    exit
}
$pg = Join-Path (Get-Location) '.tools\postgres\pgsql\bin'
if (!(Test-Path "$pg\pg_ctl.exe")) { throw 'Install PostgreSQL 18.6 or extract the official EDB binary distribution to .tools/postgres.' }
if (!(Test-Path .local\pgdata\PG_VERSION)) {
    $env:POSTGRES_PASSWORD | Set-Content .local\pg-init-password
    & "$pg\initdb.exe" -D .local\pgdata -U fieldroom --pwfile=.local\pg-init-password --auth=scram-sha-256 --encoding=UTF8 --locale=C
    Remove-Item -LiteralPath .local\pg-init-password
    if ($LASTEXITCODE) { throw 'PostgreSQL initialization failed' }
    Add-Content .local\pgdata\postgresql.conf "`nlisten_addresses = '127.0.0.1'`nport = 55435"
}
& "$pg\pg_ctl.exe" status -D .local\pgdata *> $null
if ($LASTEXITCODE) { & "$pg\pg_ctl.exe" start -D .local\pgdata -l .local\postgres.log -w }
if ($LASTEXITCODE) { throw 'PostgreSQL failed to start' }
$env:PGPASSWORD = $env:POSTGRES_PASSWORD
$exists = & "$pg\psql.exe" -h 127.0.0.1 -p 55435 -U fieldroom -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='fieldroom'"
if ($exists -ne '1') { & "$pg\createdb.exe" -h 127.0.0.1 -p 55435 -U fieldroom fieldroom }
$env:PGPASSWORD = $null
Write-Output 'Native database ready on 127.0.0.1:55435. Run scripts/dev.ps1 and npm run dev in web.'
