using ProductionLine.Shared;

namespace ProductionLine.Server.Data;

// [DB 표] Issues — 라인에서 생긴 문제와 "지금" 상태
// 상태가 바뀌면 이 표의 Status는 새 값으로 바뀌고, 바뀐 과정은 IssueHistories 표에 따로 쌓입니다.
public sealed class IssueEntity
{
    public Guid Id { get; set; }                       // 문제 고유 번호
    public int LineId { get; set; }                    // 어느 라인의 문제인지
    public IssueType Type { get; set; }                // 0 = 생산라인 문제, 1 = 검사 시스템 문제
    public string Title { get; set; } = "";            // 제목
    public string Description { get; set; } = "";      // 설명
    public IssueStatus Status { get; set; }            // 0 발생, 1 확인, 2 조치 중, 3 완료 요청, 4 종결
    public string CreatedBy { get; set; } = "";        // 등록한 사람 아이디
    public string? ActionNote { get; set; }            // 마지막 조치 메모 (반려하면 반려 사유가 들어감)
    public DateTimeOffset CreatedAt { get; set; }      // 등록 시각
    public DateTimeOffset UpdatedAt { get; set; }      // 마지막으로 바뀐 시각
}
