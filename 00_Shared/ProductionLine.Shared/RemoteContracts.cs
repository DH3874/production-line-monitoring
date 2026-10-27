namespace ProductionLine.Shared;

public enum RemoteAction
{
    ConnectCamera, DisconnectCamera, StartInspection, StopInspection,
    RefreshPorts, ConnectConveyor, DisconnectConveyor, RunConveyor, StopConveyor,
    SetLight, SetSorting, ResetEmergency, SelectModel, ChangePage, StopAll, SetBoostDuration
}
public enum RemoteCommandStatus { Pending, Executing, Succeeded, Failed, Expired, Unknown, Cancelled }

public sealed record WorkerStateDto(
    string MachineName, bool CameraConnected, int CameraIndex, bool Inspecting,
    bool ConveyorConnected, string? Port, bool BeltRunning, bool? LightOn,
    bool AutoSorting, int TravelMs, int HoldMs, string Model, int Page,
    string[] Ports, string[] Models, string Status,
    byte[]? LiveJpeg, byte[]? LastProductJpeg, DateTimeOffset? LiveCapturedAt, bool SpeedBoosted = false, int BoostDurationMs = 3000,
    bool EmergencyStopped = false, bool? EmergencySwitchReady = null);

public sealed record WorkerHeartbeatRequest(Guid ClientId, WorkerStateDto State, bool ReadyForCommand);
public sealed record WorkerHeartbeatResponse(RemoteCommandDto? Command);
public sealed record WorkerConnectionDto(Guid ClientId, string UserId, WorkerStateDto State, DateTimeOffset ReceivedAt)
{
    public bool IsOnline => DateTimeOffset.UtcNow - ReceivedAt < TimeSpan.FromSeconds(6);
}
public sealed record CreateRemoteCommandRequest(Guid RequestId, Guid ExpectedClientId, RemoteAction Action,
    int? CameraIndex = null, string? Port = null, string? Model = null, bool? Enabled = null,
    int? TravelMs = null, int? HoldMs = null, int? Page = null, int? BoostMs = null);
public sealed record RemoteCommandDto(Guid Id, int LineId, Guid ClientId, string Administrator,
    CreateRemoteCommandRequest Request, RemoteCommandStatus Status, string Message,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? CompletedAt = null);
public sealed record CompleteRemoteCommandRequest(Guid ClientId, bool Succeeded, string Message);
public sealed record RemoteLineSnapshotDto(WorkerConnectionDto? Worker, IReadOnlyList<RemoteCommandDto> Commands);

public static class RemoteActionText
{
    public static string Name(RemoteAction action) => action switch
    {
        RemoteAction.ConnectCamera => "카메라 연결", RemoteAction.DisconnectCamera => "카메라 해제",
        RemoteAction.StartInspection => "검사 시작", RemoteAction.StopInspection => "검사 중지",
        RemoteAction.RefreshPorts => "포트 갱신", RemoteAction.ConnectConveyor => "벨트 연결",
        RemoteAction.DisconnectConveyor => "벨트 연결 해제", RemoteAction.RunConveyor => "벨트 구동",
        RemoteAction.StopConveyor => "벨트 정지", RemoteAction.SetLight => "조명 설정",
        RemoteAction.SetSorting => "분류 설정", RemoteAction.ResetEmergency => "긴급 정지 잠금 해제",
        RemoteAction.SelectModel => "모델 선택", RemoteAction.ChangePage => "작업자 화면 이동",
        RemoteAction.SetBoostDuration => "가속 시간 설정", RemoteAction.StopAll => "검사·벨트 정지", _ => "알 수 없는 명령"
    };
    public static string Status(RemoteCommandStatus status) => status switch
    {
        RemoteCommandStatus.Pending => "전달 대기", RemoteCommandStatus.Executing => "실행 중",
        RemoteCommandStatus.Succeeded => "완료", RemoteCommandStatus.Failed => "실패",
        RemoteCommandStatus.Expired => "기한 만료", RemoteCommandStatus.Cancelled => "취소",
        _ => "실행 결과 미확인"
    };
}
