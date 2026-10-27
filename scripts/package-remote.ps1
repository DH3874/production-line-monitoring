$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'output/remote-upgrade-20261021'
$package = Join-Path $root 'output/생산라인_원격운영_20261021'
foreach ($name in @('User','Admin','Server')) { New-Item -ItemType Directory -Path (Join-Path $package $name) -Force | Out-Null }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $build 'Checks') -File) {
    if ($file.Name -like 'Verification.*' -or $file.Name -like 'ProductionLine.AdminApp.*') { continue }
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $package ('User/'+$file.Name)) -Force
}
foreach ($name in @('Models','Hardware','Tools','runtimes')) {
    $path = Join-Path $build ('Checks/'+$name)
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $package 'User') -Recurse -Force }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $build 'Checks') -File) {
    if ($file.Name -like 'ProductionLine.AdminApp.*' -or $file.Name -like 'ProductionLine.Shared.*') {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $package ('Admin/'+$file.Name)) -Force
    }
}
foreach ($item in Get-ChildItem -LiteralPath (Join-Path $build 'Server')) {
    if ($item.Name -in 'App_Data','data') { continue }
    Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $package 'Server') -Recurse -Force
}
if (-not (Test-Path -LiteralPath (Join-Path $package '서버주소.txt'))) {
    'http://127.0.0.1:5050' | Set-Content -LiteralPath (Join-Path $package '서버주소.txt') -Encoding utf8
}
@'
@echo off
if exist "%~dp0..\..\03_Server\ProductionLine.Server\App_Data\productionline.db" (
  cd /d "%~dp0..\..\03_Server\ProductionLine.Server"
) else (
  cd /d "%~dp0Server"
)
dotnet "%~dp0Server\ProductionLine.Server.dll"
pause
'@ | Set-Content -LiteralPath (Join-Path $package '서버_실행.cmd') -Encoding ascii
@'
@echo off
cd /d "%~dp0User"
start "" "%~dp0User\ProductionLine.UserApp.exe"
'@ | Set-Content -LiteralPath (Join-Path $package '작업자_실행.cmd') -Encoding ascii
@'
@echo off
cd /d "%~dp0Admin"
start "" "%~dp0Admin\ProductionLine.AdminApp.exe"
'@ | Set-Content -LiteralPath (Join-Path $package '관리자_실행.cmd') -Encoding ascii
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $root '04_Vision/models/best.onnx')).Hash
$copyHash = (Get-FileHash -LiteralPath (Join-Path $package 'User/Models/best.onnx')).Hash
if ($sourceHash -ne $copyHash) { throw 'Model copy hash mismatch' }
Write-Output ('Package: '+$package)
Write-Output 'Original database remains in the source server folder; no data files overwritten.'
Write-Output ('ONNX model preserved: '+$copyHash)
