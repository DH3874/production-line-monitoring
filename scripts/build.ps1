$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
dotnet restore (Join-Path $root 'ProductionLineSystem.sln')
dotnet build (Join-Path $root 'ProductionLineSystem.sln') --no-restore
