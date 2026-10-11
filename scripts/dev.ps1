$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
if (Test-Path .tools\dotnet\dotnet.exe) { $env:DOTNET_ROOT = "$PWD\.tools\dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH" }
Get-Content .env | ForEach-Object { $k,$v = $_.Split('=',2); if ($k -match '^[A-Z_]+$') { [Environment]::SetEnvironmentVariable($k,$v,'Process') } }
$env:ConnectionStrings__Library = "Host=127.0.0.1;Port=55435;Database=fieldroom;Username=fieldroom;Password=$env:POSTGRES_PASSWORD"
$env:BootstrapPassword = $env:BOOTSTRAP_PASSWORD
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5085'
$env:AutoMigrate = 'true'
$env:DataProtectionPath = "$PWD\.local\keys"
dotnet run --project src/Api --no-launch-profile
