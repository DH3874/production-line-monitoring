namespace ProductionLine.Server.Data;

// [DB 표] LineHardwareLogs — 센서·벨트 상태 기록 (김하균)
// 실시간 그래프는 LineHardwareStore의 메모리(최근 10분)를 쓰고, 이 표는 서버를 꺼도 남는 기록용입니다.
// 10초에 한 번, 그리고 긴급 정지·벨트 구동 같은 상태가 바뀐 순간에 한 줄씩 저장합니다.
// 7일 지난 기록은 LineHardwareStore가 하루 한 번 지웁니다. (appsettings.json의 HardwareLogRetentionDays)
public sealed class LineHardwareLogEntity
{
    public long Id { get; set; }                           // 기록 번호
    public int LineId { get; set; }                        // 라인 번호 (실제 라인 1)
    public DateTimeOffset RecordedAt { get; set; }         // 서버가 받은 시각 (DB에는 큰 숫자로 저장됨)
    public string Reason { get; set; } = "";               // 저장 이유: 주기 / 상태 변경
    public bool Connected { get; set; }                    // 보드 연결 여부
    public double? TemperatureCelsius { get; set; }        // 온도 (측정 실패면 비어 있음)
    public double? HumidityPercent { get; set; }           // 습도
    public bool? GasDetected { get; set; }                 // 가스 감지 여부
    public int? GasAnalogValue { get; set; }               // 가스 센서 A0 값 (0~1023)
    public bool GasWarmingUp { get; set; }                 // 가스 센서 예열 중
    public bool EmergencyStopped { get; set; }             // 긴급 정지 작동
    public bool BeltRunning { get; set; }                  // 벨트 구동
    public bool SpeedBoosted { get; set; }                 // 벨트 가속
    public int SorterAngle { get; set; }                   // 분류 모터 각도
    public bool Sorting { get; set; }                      // 분류 중
}
