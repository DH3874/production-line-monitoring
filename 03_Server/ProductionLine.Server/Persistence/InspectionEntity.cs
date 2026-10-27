using ProductionLine.Shared;

namespace ProductionLine.Server.Data;

// [DB 표] Inspections — 검사 한 번에 한 줄씩 쌓이는 검사 결과 (불량인지 왜 불량인지 이런거 저장)
// 형태·색상 점수 칸(ShapeScore, ColorScore)은 YOLO로 바뀌며 안 써서 2026-10-22에 지웠습니다. (김하균)
public sealed class InspectionEntity
{
    public Guid Id { get; set; }                      // 검사 고유 번호
    public int LineId { get; set; }                   // 어느 라인에서 검사했는지
    public InspectionResultType Result { get; set; }  // 0 = 정상, 1 = 불량, 2 = 판정 불가
    public string Reason { get; set; } = "";          // 판정 이유
    public DateTimeOffset InspectedAt { get; set; }   // 검사 시각
    public double? DetectionConfidence { get; set; }  // YOLO 검출 신뢰도. 시뮬레이션 검사는 null
    public bool HasImage { get; set; }                 // 검사 당시 인식 사진 저장 여부
    public string? PhoneModel { get; set; }            // YOLO가 알아낸 휴대폰 기종 (모르면 비어 있음, 2026-10-22 김하균)
}
