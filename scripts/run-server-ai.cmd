@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; $env:DOTNET_CLI_HOME=Join-Path (Get-Location) '.dotnet-home'; $env:AI_PROVIDER='OpenAi'; $env:OpenAi__Enabled='true'; if (-not $env:OPENAI_API_KEY) { $secureKey=Read-Host 'OpenAI API key (hidden; Enter = manual mode)' -AsSecureString; $env:OPENAI_API_KEY=[System.Net.NetworkCredential]::new('', $secureKey).Password }; $project=Join-Path (Get-Location) '03_Server\ProductionLine.Server\ProductionLine.Server.csproj'; try { & dotnet run --project $project --no-launch-profile; $result=$LASTEXITCODE } finally { Remove-Item Env:OPENAI_API_KEY -ErrorAction SilentlyContinue }; exit $result"
pause
