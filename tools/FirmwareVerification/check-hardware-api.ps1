$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$run = Join-Path $root ('output/hardware-expansion-verification/api-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$url = 'http://127.0.0.1:' + $port
$dll = Join-Path $root 'output/hardware-expansion-verification/Server/ProductionLine.Server.dll'
$process = Start-Process dotnet -ArgumentList @(('"' + $dll + '"'),'--ServerUrl',$url) -WorkingDirectory $run -WindowStyle Hidden -RedirectStandardOutput (Join-Path $run 'server.log') -RedirectStandardError (Join-Path $run 'error.log') -PassThru
function Check($condition,$name) { if(-not $condition) { throw $name }; Write-Output ('PASS: ' + $name) }
function Post($path,$body,$headers) { Invoke-RestMethod -Uri ($url+$path) -Method Post -ContentType 'application/json' -Body ($body | ConvertTo-Json) -Headers $headers }
function Reject($path,$body,$headers,$code) {
  try { Post $path $body $headers | Out-Null; throw 'unexpected success' }
  catch { if(-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne $code) { throw }; Write-Output "PASS: endpoint rejects $code" }
}
try {
  for($i=0;$i -lt 100;$i++) { try { Invoke-RestMethod $url | Out-Null; break } catch { Start-Sleep -Milliseconds 100 } }
  $worker = Post '/api/auth/login' @{userId='worker1';password='worker123!'} @{}
  $admin = Post '/api/auth/login' @{userId='admin';password='admin123!'} @{}
  $wh = @{Authorization='Bearer '+$worker.token}; $ah = @{Authorization='Bearer '+$admin.token}
  $state = @{connected=$true;temperatureCelsius=24.5;humidityPercent=53;gasDetected=$false;emergencyStopped=$false;beltRunning=$true;sorterAngle=45;sorting=$true}
  Reject '/api/lines/1/hardware' $state @{} 401
  Reject '/api/lines/2/hardware' $state $wh 403
  Reject '/api/lines/2/hardware' $state $ah 400
  $saved = Post '/api/lines/1/hardware' $state $wh
  $line = (Invoke-RestMethod ($url+'/api/lines') -Headers $ah) | ForEach-Object { $_ } | Where-Object id -eq 1
  Check ($line.hardware.temperatureCelsius -eq 24.5 -and $line.hardware.sorterAngle -eq 45 -and $line.operationStatus -eq '가동') 'worker hardware reaches administrator dashboard'
  $state.emergencyStopped=$true
  Reject '/api/lines/1/hardware' $state $wh 400
  $state.beltRunning=$false
  Post '/api/lines/1/hardware' $state $wh | Out-Null
  $line = (Invoke-RestMethod ($url+'/api/lines') -Headers $ah) | ForEach-Object { $_ } | Where-Object id -eq 1
  Check ($line.hardware.emergencyStopped -and $line.operationStatus -eq '긴급 정지') 'emergency status reaches administrator'
  Start-Sleep -Seconds 6
  $line = (Invoke-RestMethod ($url+'/api/lines') -Headers $ah) | ForEach-Object { $_ } | Where-Object id -eq 1
  Check (-not $line.hardware.isFresh -and $line.operationStatus -eq '장비 통신 미확인') 'lost sensor publisher expires instead of showing normal'
} finally { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }

