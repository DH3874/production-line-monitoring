namespace ProductionLine.Shared;

// Additional read-only contract; existing login, hardware and AI DTOs stay unchanged.
public sealed record DeviceDiagnosticsSnapshot(int LineId, bool IsRealLine, DateTimeOffset CapturedAt,
    IReadOnlyList<DeviceDiagnostic> Devices, string AppStatus);

public sealed record DeviceDiagnostic(string Id, string Name, string Connection, string State,
    string Detail, DateTimeOffset? ObservedAt, int FreshForSeconds = 5)
{
    public string StateLabel => State switch
    {
        "received" => "응답 수신", "reading" => "측정값 수신", "reported" => "제어 상태 보고",
        "read_error" => "읽기 실패", "alert" => "경보 신호", "stale" => "수신 지연",
        "unavailable" => "사용 안 함", _ => "미확인"
    };
    public bool IsCurrent(DateTimeOffset now) => ObservedAt is { } at && now >= at &&
        now - at < TimeSpan.FromSeconds(FreshForSeconds);
}

public static class DeviceDiagnosticsText
{
    public static string Describe(DeviceDiagnostic device, DateTimeOffset now)
    {
        if (device.State is not ("unknown" or "unavailable" or "stale") && !device.IsCurrent(now))
            return "수신 지연 · 이전 보고를 현재 상태로 판단하지 않습니다.";
        return device.StateLabel + " · " + device.Detail;
    }
}
