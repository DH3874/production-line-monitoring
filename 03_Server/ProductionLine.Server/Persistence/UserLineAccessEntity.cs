namespace ProductionLine.Server.Data;

// [DB 표] UserLineAccesses — 누가 어느 라인을 볼 수 있는지
// 한 줄 = "이 사용자는 이 라인을 볼 수 있다". 라인이 2개면 2줄이 저장됩니다.
// 같은 (사용자, 라인) 조합은 두 번 저장되지 않습니다 (AppDbContext에서 설정).
public sealed class UserLineAccessEntity
{
    public string UserId { get; set; } = "";  // Users 표의 아이디
    public int LineId { get; set; }           // Lines 표의 라인 번호
}
