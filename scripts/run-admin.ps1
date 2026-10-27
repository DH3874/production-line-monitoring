$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
dotnet run --project (Join-Path $root '01_AdminApp\ProductionLine.AdminApp\ProductionLine.AdminApp.csproj')
