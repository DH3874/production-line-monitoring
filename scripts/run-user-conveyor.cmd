@echo off
cd /d "%~dp0.."
echo Close any running user app before rebuilding.
dotnet build "02_UserApp\ProductionLine.UserApp\ProductionLine.UserApp.csproj" --nologo -v:minimal
if errorlevel 1 goto failed
start "" "02_UserApp\ProductionLine.UserApp\bin\Debug\net8.0-windows\ProductionLine.UserApp.exe"
exit /b 0
:failed
echo Build failed. See messages above.
pause
exit /b 1
