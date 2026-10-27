using ProductionLine.Shared;

namespace ProductionLine.Server.Data;

// [DB 표] Users — 로그인할 수 있는 사용자 계정
// 이 클래스는 표의 "모양"이고, 실제 데이터는 App_Data/productionline.db 파일에 저장됩니다.
// 속성 하나가 표의 칸(열) 하나입니다.
public sealed class UserEntity
{
    public string Id { get; set; } = "";            // 로그인 아이디 (예: admin, worker1) — 이 표의 고유 번호 역할
    public string Name { get; set; } = "";          // 화면에 보일 이름
    public UserRole Role { get; set; }              // 0 = 관리자, 1 = 작업자 (DB에는 숫자로 저장)
    public string PasswordHash { get; set; } = "";  // 비밀번호를 암호화한 값. 원래 비밀번호는 저장하지 않음
    public bool IsActive { get; set; }              // false면 로그인·요청이 모두 막힘 (계정 비활성화)
}
