namespace ProductionLine.Server.Data;

// [DB 표] RequestLogs — 통신 기록 (나중에 "보냈다/못 받았다"를 확인하는 근거)
// 앱과 서버가 주고받은 요청 한 건의 기록입니다. 서버가 요청을 받았는지, 어떻게 응답했는지 확인하는 근거로 씁니다.
// 서버에 도착하지 못한 요청이나 앱의 실제 수신·화면 표시는 이 기록으로 증명할 수 없습니다.
// 비밀번호·토큰·메시지 내용은 저장하지 않습니다.
// 기록은 Program.cs의 통신 기록 장치가 남기고, 7일 지난 기록은 RequestLogService가 CSV로 백업한 뒤 지웁니다.
public sealed class RequestLogEntity
{
    public long Id { get; set; }                   // 기록 번호
    public DateTimeOffset ReceivedAt { get; set; } // 서버가 요청을 받은 시각 (DB에는 큰 숫자로 저장됨)
    public string? UserId { get; set; }            // 요청한 사람 (로그인 전이면 비어 있음, 로그인 실패면 입력한 아이디)
    public string? ClientIp { get; set; }          // 요청을 보낸 PC 주소
    public string Method { get; set; } = "";       // GET(조회), POST(등록), PUT(변경)
    public string Path { get; set; } = "";         // 요청 주소 (예: /api/issues/.../messages)
    public string Action { get; set; } = "";       // 사람이 읽기 쉬운 기능 이름 (예: 메시지 보내기)
    public Guid? IssueId { get; set; }             // 관련 문제 번호 (없으면 비어 있음)
    public int StatusCode { get; set; }            // 결과 번호: 200 성공, 400번대 보낸 쪽 문제, 500번대 서버 문제
    public int DurationMs { get; set; }            // 처리에 걸린 시간 (밀리초)
    public string? Note { get; set; }              // 메모 (예: "메시지 12번 저장", "Occurred → InProgress")
}
