namespace ProductionLine.Server.Data;

// [DB 표] Lines — 생산라인 1~4의 현재 상태와 검사 누적 개수
// 처음 서버를 켤 때 DatabaseInitializer가 4개 라인을 넣습니다 (1번만 실제, 2~4번은 가상).
// 검사를 할 때마다 AppStore.AddInspection이 개수를 1씩 올립니다.
public sealed class LineEntity
{
    public int Id { get; set; }                            // 라인 번호 (1~4)
    public string Name { get; set; } = "";                 // 화면에 보일 이름
    public bool IsReal { get; set; }                       // true = 실제 라인(검사 가능), false = 가상 데이터
    public string OperationStatus { get; set; } = "";      // 가동 상태 (예: 가동, 공정 정지)
    public string InspectionStatus { get; set; } = "";     // 검사 상태 (예: 검사 중, 검사 확인 필요)
    public int NormalCount { get; set; }                   // 정상 누적 개수
    public int DefectCount { get; set; }                   // 불량 누적 개수
    public int UndeterminedCount { get; set; }             // 판정 불가 누적 개수
    public DateTimeOffset UpdatedAt { get; set; }          // 마지막으로 바뀐 시각
}
