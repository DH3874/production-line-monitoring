using ProductionLine.Shared;

namespace ProductionLine.Server.Data;

// [DB 표] IssueHistories — 문제 상태가 바뀔 때마다 한 줄씩 쌓이는 변경 기록
// "누가, 언제, 무엇에서 무엇으로 바꿨는지"를 남깁니다. 서버가 거절한 요청은 남지 않습니다.
// 저장은 AppStore.UpdateIssue에서 Issues 표 변경과 함께 한 번에 합니다.
public sealed class IssueHistoryEntity
{
    public Guid Id { get; set; }                        // 기록 고유 번호
    public Guid IssueId { get; set; }                   // 어떤 문제의 기록인지
    public IssueStatus PreviousStatus { get; set; }     // 바뀌기 전 상태 (숫자: 0 발생 ~ 4 종결)
    public IssueStatus NewStatus { get; set; }          // 바뀐 뒤 상태
    public string ChangedBy { get; set; } = "";         // 바꾼 사람 아이디 (로그인한 계정으로 서버가 정함)
    public DateTimeOffset ChangedAt { get; set; }       // 바꾼 시각
    public string ActionNote { get; set; } = "";        // 조치 내용 또는 반려 사유
}
