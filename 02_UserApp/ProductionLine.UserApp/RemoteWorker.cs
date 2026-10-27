using System.Drawing.Imaging;
using ProductionLine.ChatUI;
using ProductionLine.Shared;
using ProductionLine.Vision;

namespace ProductionLine.UserApp;

public sealed partial class UserMainForm
{
    private readonly SensorTrendPanel _sensorTrends = new() { Dock = DockStyle.Fill, DarkMode = true };
    private readonly Label _remoteNotice = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "관리자 원격 연결 대기", Padding = new Padding(6) };
    private readonly System.Windows.Forms.Timer _remoteTimer = new() { Interval = 1000 };
    private readonly Guid _remoteClientId = Guid.NewGuid();
    private bool _remotePolling, _remoteExecuting, _remoteStopping;
    private int _remoteStopRevision;
    private DateTimeOffset? _remoteVideoAt;
    // 원격 명령 실행 중에는 현장 조작 버튼만 잠급니다(화면 이동·채팅·상태 글자는 그대로).
    private readonly List<Control> _remoteControlAreas = new();
    private void SetRemoteControlsEnabled(bool enabled) { foreach (var area in _remoteControlAreas) area.Enabled = enabled; }
    private readonly Queue<(Guid Id, CompleteRemoteCommandRequest Result)> _remoteCompletions = new();
    private Dictionary<string,string> _remoteModels = new();
    private void StartRemoteWorker()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Models");
        if (Directory.Exists(root))
            _remoteModels = Directory.GetFiles(root, "*.onnx", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\','/'), path => path);
        _remoteTimer.Tick += async (_, _) => await PollRemoteAsync();
        FormClosed += (_, _) => _remoteTimer.Dispose();
        _remoteTimer.Start();
        _ = LoadSensorHistoryAsync();
        _ = PollRemoteAsync();
    }
    private async Task LoadSensorHistoryAsync()
    {
        try
        {
            var samples = await _api.GetHardwareHistoryAsync(HardwareLineId);
            if (!IsDisposed) _sensorTrends.MergeSamples(samples);
        }
        catch (Exception) { /* First connection has no history; local readings still work. */ }
    }
    private static byte[]? PreviewJpeg(Image? image)
    {
        if (image == null) return null;
        var scale = Math.Min(1, 640d / image.Width);
        using var preview = new Bitmap(Math.Max(1,(int)(image.Width*scale)), Math.Max(1,(int)(image.Height*scale)));
        using (var g = Graphics.FromImage(preview)) g.DrawImage(image,0,0,preview.Width,preview.Height);
        using var stream = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 55L);
        preview.Save(stream,encoder,quality);
        return stream.Length <= 180000 ? stream.ToArray() : null;
    }
    private WorkerStateDto BuildWorkerState()
    {
        var frame = _camera?.Latest;
        bool fresh = frame != null && DateTime.UtcNow-frame.ReceivedUtc < TimeSpan.FromSeconds(3) && _camera?.Error == null;
        var status = $"{_inspectionState.Text}\r\n{_conveyorState.Text}\r\n{_result.Text}";
        var model = _selectedYoloModelPath == null ? string.Empty : _remoteModels.FirstOrDefault(p =>
            string.Equals(p.Value,_selectedYoloModelPath,StringComparison.OrdinalIgnoreCase)).Key ?? Path.GetFileName(_selectedYoloModelPath);
        return new(Environment.MachineName, fresh, (int)_cameraIndex.Value, _runningInspection,
            _conveyor != null, _conveyorPort.SelectedItem as string, _conveyor?.IsRunning == true,
            (_conveyor as IConveyorLightingController)?.IsLightOn, _automaticSort.Checked,
            (int)_sortTravelMs.Value,(int)_sortHoldMs.Value,model,_pages.SelectedIndex == 4 ? 5 : _pages.SelectedIndex,
            _conveyorPort.Items.Cast<string>().ToArray(),_remoteModels.Keys.ToArray(),
            status[..Math.Min(status.Length,2000)], fresh && _remoteVideoAt.HasValue && DateTimeOffset.UtcNow-_remoteVideoAt.Value < TimeSpan.FromSeconds(4) ? PreviewJpeg(_video.Image) : null,
            PreviewJpeg(_recognizedVideo.Image),fresh ? _remoteVideoAt : null,
            _conveyor?.IsRunning == true && _latestHardware is { Connected: true, SpeedBoosted: true } && DateTimeOffset.UtcNow-_latestHardwareAt < TimeSpan.FromSeconds(2), (int)(_boostSeconds.Value * 1000),
            _conveyor != null && _latestHardware is { Connected: true, EmergencyStopped: true }, (_conveyor as ConveyorSerialController)?.EmergencySwitchReady);
    }
    private async Task PollRemoteAsync()
    {
        if (_remotePolling || IsDisposed || SessionExpired || _lineId != HardwareLineId) return;
        _remotePolling = true;
        try
        {
            if (_remoteCompletions.TryPeek(out var completion))
            {
                await _api.CompleteRemoteCommandAsync(HardwareLineId,completion.Id,completion.Result);
                _remoteCompletions.Dequeue(); // Only acknowledgments are retried, never hardware commands.
            }
            var reply = await _api.SendWorkerHeartbeatAsync(HardwareLineId,
                new(_remoteClientId,BuildWorkerState(),!_remoteExecuting && !_remoteStopping && !_arduinoSetupBusy));
            if (IsDisposed) return;
            if (reply.Command is { } command)
            {
                if (command.Request.Action == RemoteAction.StopAll) _ = ExecuteRemoteStopAsync(command);
                else _ = ExecuteRemoteCommandAsync(command);
            }
            else if (!_remoteExecuting && !_remoteStopping) _remoteNotice.Text = "관리자 원격 연결됨 · 관리자 명령 수신 가능";
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _remoteNotice.Text = "원격 연결 확인 필요: " + ex.Message;
                // Camera and physical E-stop remain local and independent of network polling.
                if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } || ex.Message == "로그인이 필요합니다.")
                    await HandleServerFailureAsync(ex,"원격 인증 만료");
            }
        }
        finally { _remotePolling = false; }
    }
    private async Task ExecuteRemoteCommandAsync(RemoteCommandDto command)
    {
        if (_remoteExecuting || command.ClientId != _remoteClientId) return;
        _remoteExecuting = true;
        SetRemoteControlsEnabled(false);
        _lines.Enabled = false;
        var success = false; string message;
        _remoteNotice.Text = $"관리자 {command.Administrator} · {RemoteActionText.Name(command.Request.Action)} 실행 중";
        try
        {
            if (DateTimeOffset.UtcNow >= command.ExpiresAt) throw new InvalidOperationException("전달 기한이 지난 명령입니다.");
            if (_arduinoSetupBusy || _conveyorCommandBusy || _lightCommandBusy)
                throw new InvalidOperationException("현장 연결·제어 작업 중입니다. 완료 후 다시 명령하세요.");
            await ApplyRemoteActionAsync(command.Request);
            success = true; message = "완료 · " + RemoteActionText.Name(command.Request.Action);
        }
        catch (Exception ex) { message = "실패 · " + ex.Message; }
        finally
        {
            _remoteExecuting = false;
            if (!IsDisposed) { SetRemoteControlsEnabled(!_remoteStopping); _lines.Enabled = !_loading && !_remoteStopping; }
        }
        if (IsDisposed) return;
        _remoteNotice.Text = $"관리자 {command.Administrator} · {message}";
        _remoteCompletions.Enqueue((command.Id,new(_remoteClientId,success,message)));
    }
    private async Task ExecuteRemoteStopAsync(RemoteCommandDto command)
    {
        _remoteStopping = true; _remoteStopRevision++;
        SetRemoteControlsEnabled(false);
        _lines.Enabled = false;
        var success = false; string message;
        try
        {
            _automaticSort.Checked = false;
            StopInspection("관리자 원격 명령으로 검사·벨트 정지");
            if (_conveyor == null) throw new IOException("검사는 중지했지만 벨트 미연결로 실제 정지 여부를 확인할 수 없습니다.");
            await StopConveyorAsync("관리자 원격 검사·벨트 정지 확인");
            success = true; message = "검사·벨트 정지 완료 · 대기 중 시작 취소";
        }
        catch (Exception ex) { message = "정지 응답 미확인 · 현장 확인 필요: " + ex.Message; }
        finally
        {
            _remoteStopping = false;
            if (!IsDisposed) { SetRemoteControlsEnabled(!_remoteExecuting); _lines.Enabled = !_loading && !_remoteExecuting; }
        }
        if (!IsDisposed)
        {
            _remoteNotice.Text = $"관리자 {command.Administrator} · {message}";
            _remoteCompletions.Enqueue((command.Id,new(_remoteClientId,success,message)));
        }
    }
    private async Task PrepareRemoteModelAsync(string? path)
    {
        var next = await Task.Run(() => new YoloOnnxInspector(path,PhoneConfidenceThreshold));
        if (IsDisposed) { next.Dispose(); throw new ObjectDisposedException(nameof(UserMainForm)); }
        var previous = _yoloInspector; _yoloInspector = next;
        _selectedYoloModelPath = next.ModelPath;
        previous?.Dispose(); _productPresent = false; _lastProductSeenUtc = default; _inspectedFrame = 0;
        ResetRecognizedResult(); CheckYoloModel();
    }
    private async Task ApplyRemoteActionAsync(CreateRemoteCommandRequest request)
    {
        switch (request.Action)
        {
            case RemoteAction.ConnectCamera:
                if (_runningInspection || _inspectionBusy) throw new InvalidOperationException("먼저 검사를 중지하세요.");
                if (_camera != null && (int)_cameraIndex.Value != request.CameraIndex) DisconnectCamera();
                await _cameraClosed;
                _cameraIndex.Value = request.CameraIndex!.Value;
                ConnectCamera();
                for (var i=0;i<60 && _camera?.Latest == null && _camera?.Error == null;i++) await Task.Delay(50);
                if (_camera?.Latest == null) throw new IOException(_camera?.Error ?? "카메라 응답 대기 시간 초과");
                break;
            case RemoteAction.DisconnectCamera: DisconnectCamera(); break;
            case RemoteAction.StartInspection:
                if (_inspectionBusy) throw new InvalidOperationException("이전 검사 처리 완료를 기다리세요.");
                if (_camera?.Latest == null) throw new InvalidOperationException("먼저 카메라를 연결하세요.");
                var revision = _remoteStopRevision;
                var inspectionRevision = _inspectionGeneration;
                if (_yoloInspector == null) await PrepareRemoteModelAsync(_selectedYoloModelPath);
                if (revision != _remoteStopRevision || inspectionRevision != _inspectionGeneration)
                    throw new InvalidOperationException("정지 명령으로 검사 시작이 취소됐습니다.");
                await StartInspectionAsync();
                if (!_runningInspection) throw new InvalidOperationException(_inspectionState.Text);
                break;
            case RemoteAction.StopInspection: StopInspection("관리자 원격 명령으로 검사 중지"); break;
            case RemoteAction.RefreshPorts: RefreshConveyorPorts(); break;
            case RemoteAction.ConnectConveyor:
                if (_conveyor != null)
                {
                    if (_conveyorPort.SelectedItem as string != request.Port) throw new InvalidOperationException("기존 벨트 연결을 먼저 해제하세요.");
                    break;
                }
                RefreshConveyorPorts();
                if (!_conveyorPort.Items.Contains(request.Port!)) throw new IOException("선택한 COM 포트가 없습니다.");
                _conveyorPort.SelectedItem = request.Port;
                await ConnectConveyorAsync();
                if (_conveyor == null) throw new IOException(_conveyorState.Text);
                break;
            case RemoteAction.DisconnectConveyor:
                StopInspection("관리자 원격 명령으로 장비 연결 해제");
                await DisconnectConveyorAsync();
                if (_conveyorState.Text.Contains("정지 응답 미확인")) throw new IOException(_conveyorState.Text);
                break;
            case RemoteAction.RunConveyor:
                if (_conveyor == null) throw new IOException("먼저 벨트를 연결하세요.");
                await RunConveyorAsync();
                if (_conveyor?.IsRunning != true) throw new IOException(_conveyorState.Text);
                break;
            case RemoteAction.StopConveyor:
                if (_conveyor == null) throw new IOException("벨트가 연결되지 않았습니다.");
                await StopConveyorAsync("관리자 원격 벨트 정지"); break;
            case RemoteAction.StopAll:
                _automaticSort.Checked = false;
                StopInspection("관리자 원격 명령으로 검사·벨트 정지");
                if (_conveyor != null) await StopConveyorAsync("관리자 원격 검사·벨트 정지 확인");
                break;
            case RemoteAction.SetLight:
                await SetInspectionLightAsync(request.Enabled!.Value);
                if ((_conveyor as IConveyorLightingController)?.IsLightOn != request.Enabled)
                    throw new IOException(_lightState.Text + " · " + _conveyorState.Text);
                break;
            case RemoteAction.SetBoostDuration:
                if (request.BoostMs is not (>= 500 and <= 60000)) throw new ArgumentOutOfRangeException(nameof(request.BoostMs));
                _boostSeconds.Value = request.BoostMs.Value / 1000m; break;
            case RemoteAction.SetSorting:
                if (request.TravelMs is not (>= 500 and <= 60000) || request.HoldMs is not (>= 200 and <= 60000) || request.Enabled == null)
                    throw new InvalidOperationException("이동 시간은 500~60,000ms, 유지 시간은 200~60,000ms로 설정하세요.");
                if (_inspectionBusy) throw new InvalidOperationException("현재 검사 처리 완료 후 설정하세요.");
                if (request.Enabled == true)
                {
                    if (_conveyor is not IHardwareConveyorController { SupportsHardware: true } ||
                        _latestHardware is not { Connected: true, EmergencyStopped: false } ||
                        DateTimeOffset.UtcNow-_latestHardwareAt > TimeSpan.FromSeconds(2))
                        throw new InvalidOperationException("최신 V4 장비 연결과 긴급 정지 상태를 확인하세요.");
                }
                _automaticSort.Checked = false;
                _sortTravelMs.Value=request.TravelMs!.Value; _sortHoldMs.Value=request.HoldMs!.Value;
                UpdateSortTimingSummary();
                _automaticSort.Checked=request.Enabled!.Value; break;
            case RemoteAction.ResetEmergency:
                if (_conveyor is not IHardwareConveyorController { SupportsHardware: true } hardware)
                    throw new IOException("V4 장비 연결이 필요합니다.");
                // Firmware refuses this until the physical NC contact is restored. Never auto-RUN.
                if ((_conveyor as ConveyorSerialController)?.EmergencySwitchReady == false)
                    throw new InvalidOperationException("현장 긴급 정지 버튼이 아직 눌려 있습니다. 버튼을 원위치한 뒤 다시 해제하세요.");
                await hardware.ResetEmergencyAsync(); await PollHardwareAsync(_conveyor); break;
            case RemoteAction.SelectModel:
                if (_runningInspection || _inspectionBusy) throw new InvalidOperationException("먼저 검사를 중지하세요.");
                if (request.Model != null && _remoteModels.TryGetValue(request.Model,out var path)) await PrepareRemoteModelAsync(path);
                else throw new IOException("허용된 모델 목록에 없습니다.");
                break;
            case RemoteAction.ChangePage:
                _pages.SelectedIndex = request.Page == 4 ? 2 : request.Page == 5 ? 4 : request.Page!.Value; break;
            default: throw new InvalidOperationException("허용되지 않은 명령입니다.");
        }
    }
}
