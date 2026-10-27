namespace ProductionLine.Shared;

public sealed record UpdateLineHardwareRequest(
    bool Connected, double? TemperatureCelsius, double? HumidityPercent,
    bool? GasDetected, bool EmergencyStopped, bool BeltRunning,
    int SorterAngle, bool Sorting, bool SpeedBoosted = false, int? GasAnalogValue = null, bool GasWarmingUp = false);

public sealed record LineHardwareStatusDto(
    bool Connected, double? TemperatureCelsius, double? HumidityPercent,
    bool? GasDetected, bool EmergencyStopped, bool BeltRunning,
    int SorterAngle, bool Sorting, DateTimeOffset ReceivedAt, bool SpeedBoosted = false, int? GasAnalogValue = null, bool GasWarmingUp = false)
{
    public bool IsFresh => Connected && DateTimeOffset.UtcNow - ReceivedAt < TimeSpan.FromSeconds(5);
}

public static class HardwareStatusText
{
    public static string Format(LineHardwareStatusDto? status)
    {
        if (status == null) return "센서 미연결 · 온도 --℃ / 습도 --% / 가스 상태 미확인";
        if (!status.IsFresh)
            return $"센서 통신 끊김 · 현재 상태 미확인 · 마지막 수신 {status.ReceivedAt.ToLocalTime():HH:mm:ss}";
        var temperature = status.TemperatureCelsius is double t ? $"{t:F1}℃" : "측정 실패";
        var humidity = status.HumidityPercent is double h ? $"{h:F1}%" : "측정 실패";
        var gas = status.GasAnalogValue is int raw ? $"A0 입력 {raw}/1023{(status.GasWarmingUp ? " · 초기 예열 중" : " · 감지 기준 미설정")}" :
            status.GasDetected is bool detected ? detected ? "감지" : "미감지" : "미확인";
        return $"온도 {temperature} / 습도 {humidity} / 가스 {gas}\r\n" +
               $"긴급 정지 {(status.EmergencyStopped ? "작동·해제 필요" : "해제됨")} / 벨트 {(status.BeltRunning ? status.SpeedBoosted ? "가속 중" : "구동" : "정지")} / 분류 모터 {status.SorterAngle}°{(status.Sorting ? " (분류 중)" : "")}";
    }
}
