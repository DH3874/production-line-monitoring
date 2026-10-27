using ProductionLine.Shared;

namespace ProductionLine.Server.Data;

// [DB 표] IssueMessages — 문제 한 건에 대한 작업자 문의와 관리자 답변 (사람끼리의 대화)
// AI 상담 기록과는 별개입니다. 보내기·읽기는 Program.cs의 /api/issues/{id}/messages 창구가 처리합니다.
public sealed class IssueMessageEntity
{
    public long Id { get; set; }                  // 메시지 번호. 저장 순서대로 1, 2, 3… 커져서 이 번호순 = 보낸 순서
    public Guid IssueId { get; set; }             // 어떤 문제에 대한 메시지인지
    public string SenderId { get; set; } = "";    // 보낸 사람 아이디
    public string SenderName { get; set; } = "";  // 보낸 당시 이름 (나중에 이름이 바뀌어도 그대로 남김)
    public UserRole SenderRole { get; set; }      // 보낸 당시 역할 (0 관리자, 1 작업자)
    public string Message { get; set; } = "";     // 내용 (1~2,000자)
    public DateTimeOffset CreatedAt { get; set; } // 보낸 시각 (DB에는 정렬을 위해 큰 숫자로 저장됨)
}
