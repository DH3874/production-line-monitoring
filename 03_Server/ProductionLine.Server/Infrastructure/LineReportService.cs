using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProductionLine.Server.Data;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// [보고서 담당] 생산 검사 보고서를 CSV 글자로 만듭니다. (김하균, 2026-10-23)
// 관리자 앱이 GET /api/reports/lines/{lineId}로 기간을 보내면 DB를 읽어 아래 6칸을 채웁니다.
//   1. 검사 요약   2. 기종별 분석   3. 센서 기록(5분 단위)   4. 문제·조치   5. 검사 이력   6. 이벤트 기록
// 정한 내용과 예시: final/md파일/report_from/README.md
public sealed class LineReportService
{
    private static readonly TimeSpan SensorBucket = TimeSpan.FromMinutes(5);
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public LineReportService(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public string BuildCsv(int lineId, DateTimeOffset from, DateTimeOffset to, string author, DateTimeOffset now)
    {
        using var db = _contextFactory.CreateDbContext();
        var csv = new StringBuilder();
        void Row(params string[] cells) => csv.AppendLine(string.Join(',', cells));

        // 검사 시각(InspectedAt)은 SQLite에서 날짜 비교가 안 되는 형식이라 라인 기록을 읽은 뒤 기간으로 거릅니다.
        var inspections = db.Inspections.AsNoTracking().Where(x => x.LineId == lineId).AsEnumerable()
            .Where(x => x.InspectedAt >= from && x.InspectedAt <= to)
            .OrderBy(x => x.InspectedAt).ToList();

        // ===== 맨 위 =====
        Row(Text($"{lineId}번 라인 - 생산 검사 보고서"));
        Row("검사 기간", $"{Local(from):yyyy-MM-dd HH:mm} ~ {Local(to):yyyy-MM-dd HH:mm}");
        Row("작성자", Text(author));
        Row("작성 시각", Time(now, "yyyy-MM-dd HH:mm"));
        Row();

        // ===== 1. 검사 요약 =====
        var normal = inspections.Count(x => x.Result == InspectionResultType.Normal);
        var defect = inspections.Count(x => x.Result == InspectionResultType.Defect);
        var undetermined = inspections.Count(x => x.Result == InspectionResultType.Undetermined);
        Row("[1. 검사 요약]");
        Row("총 검사", "정상", "불량", "판정 불가", "불량률(%)");
        Row(N(inspections.Count), N(normal), N(defect), N(undetermined), Rate(defect, normal + defect));
        Row();

        // ===== 2. 기종별 분석 ===== 기종을 모르는 검사(판정 불가 포함)는 넣지 않음
        var byModel = inspections
            .Where(x => !string.IsNullOrWhiteSpace(x.PhoneModel) && x.Result != InspectionResultType.Undetermined)
            .GroupBy(x => x.PhoneModel!)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var excluded = inspections.Count - byModel.Sum(g => g.Count());
        Row($"[2. 기종별 분석] 기종을 알아낸 검사만{(excluded > 0 ? $" (기종 미확인·판정 불가 {excluded}건 제외)" : "")}");
        Row("기종", "검사", "정상", "불량");
        foreach (var g in byModel)
            Row(Text(g.Key), N(g.Count()), N(g.Count(x => x.Result == InspectionResultType.Normal)),
                N(g.Count(x => x.Result == InspectionResultType.Defect)));
        Row();

        // ===== 3. 센서 기록 ===== 벨트 보드가 연결돼 값이 들어온 5분 칸만
        // 끝 시각 그 순간의 기록은 빼서, 끝에 기록 한 개짜리 5분 칸이 생기지 않게 합니다.
        var logs = db.LineHardwareLogs.AsNoTracking()
            .Where(x => x.LineId == lineId && x.RecordedAt >= from && x.RecordedAt < to)
            .OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).ToList();
        var connected = logs.Where(x => x.Connected).ToList();
        Row("[3. 센서 기록] 5분 단위 (벨트 보드가 연결된 시간만)");
        Row("시간대", "평균 온도(℃)", "최고 온도(℃)", "평균 습도(%)", "최고 습도(%)", "평균 가스(A0)", "최고 가스(A0)");
        foreach (var bucket in connected.GroupBy(x => BucketStart(Local(x.RecordedAt))).OrderBy(g => g.Key))
            Row(new[] { $"{bucket.Key:MM-dd HH:mm}~{bucket.Key + SensorBucket:HH:mm}" }.Concat(SensorCells(bucket.ToList())).ToArray());
        if (connected.Count > 0) Row(new[] { "전체" }.Concat(SensorCells(connected)).ToArray());
        Row();

        // ===== 4. 문제·조치 ===== 기간 안에 등록된 문제와 지금 상태
        var issues = db.Issues.AsNoTracking().Where(x => x.LineId == lineId).AsEnumerable()
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to).OrderBy(x => x.CreatedAt).ToList();
        var issueIds = db.Issues.AsNoTracking().Where(x => x.LineId == lineId).Select(x => x.Id).ToList();
        var histories = db.IssueHistories.AsNoTracking().Where(x => issueIds.Contains(x.IssueId)).ToList();
        var titles = db.Issues.AsNoTracking().Where(x => x.LineId == lineId).ToDictionary(x => x.Id, x => x.Title);
        var closedCount = issues.Count(x => x.Status == IssueStatus.Closed);
        Row($"[4. 문제·조치] 발생 {issues.Count} / 처리 중 {issues.Count - closedCount} / 종결 {closedCount}");
        Row("등록 시각", "문제", "상태", "종결 시각");
        foreach (var issue in issues)
        {
            var closedAt = issue.Status == IssueStatus.Closed
                ? histories.Where(h => h.IssueId == issue.Id && h.NewStatus == IssueStatus.Closed).Select(h => (DateTimeOffset?)h.ChangedAt).Max()
                : null;
            Row(Time(issue.CreatedAt), Text(issue.Title), StatusText(issue.Status), closedAt is { } c ? Time(c) : "");
        }
        Row();

        // ===== 5. 검사 이력 =====
        Row("[5. 검사 이력]");
        Row("검사 시각", "판정", "기종", "신뢰도(%)");
        foreach (var x in inspections)
            Row(Time(x.InspectedAt), ResultText(x.Result), Text(x.PhoneModel ?? ""),
                x.DetectionConfidence is double d ? (d * 100).ToString("0", CultureInfo.InvariantCulture) : "");
        Row();

        // ===== 6. 이벤트 기록 ===== 장비 상태가 바뀐 순간 + 문제 등록·상태 변경
        var events = new List<(DateTimeOffset At, string Kind, string Text)>();
        var previous = db.LineHardwareLogs.AsNoTracking()
            .Where(x => x.LineId == lineId && x.RecordedAt < from)
            .OrderByDescending(x => x.RecordedAt).ThenByDescending(x => x.Id).FirstOrDefault();
        foreach (var log in logs)
        {
            if (previous != null)
            {
                if (previous.Connected != log.Connected)
                    events.Add((log.RecordedAt, "보드", log.Connected ? "벨트 보드 연결" : "벨트 보드 연결 끊김"));
                // 연결이 끊긴 기록은 값이 비어 있어서, 연결된 기록끼리만 비교합니다.
                if (previous.Connected && log.Connected)
                {
                    if (previous.EmergencyStopped != log.EmergencyStopped)
                        events.Add((log.RecordedAt, "긴급 정지", log.EmergencyStopped ? "긴급 정지 버튼 작동" : "긴급 정지 해제"));
                    if (previous.BeltRunning != log.BeltRunning)
                        events.Add((log.RecordedAt, "벨트", log.BeltRunning ? "벨트 구동" : "벨트 정지"));
                }
            }
            previous = log;
        }
        foreach (var issue in issues)
            events.Add((issue.CreatedAt, "문제", $"{issue.Title} 등록"));
        foreach (var h in histories.Where(h => h.ChangedAt >= from && h.ChangedAt <= to))
            events.Add((h.ChangedAt, "문제", $"{titles.GetValueOrDefault(h.IssueId, "문제")} {StatusText(h.PreviousStatus)} → {StatusText(h.NewStatus)}"));
        Row("[6. 이벤트 기록]");
        Row("시각", "구분", "내용");
        foreach (var e in events.OrderBy(e => e.At))
            Row(Time(e.At), e.Kind, Text(e.Text));
        return csv.ToString();
    }

    private static string[] SensorCells(IReadOnlyList<LineHardwareLogEntity> samples)
    {
        var temperature = samples.Where(x => x.TemperatureCelsius.HasValue).Select(x => x.TemperatureCelsius!.Value).ToList();
        var humidity = samples.Where(x => x.HumidityPercent.HasValue).Select(x => x.HumidityPercent!.Value).ToList();
        var gas = samples.Where(x => x.GasAnalogValue.HasValue).Select(x => (double)x.GasAnalogValue!.Value).ToList();
        return [Avg(temperature, "0.0"), Max(temperature, "0.0"), Avg(humidity, "0.0"), Max(humidity, "0.0"), Avg(gas, "0"), Max(gas, "0")];
        static string Avg(List<double> v, string f) => v.Count == 0 ? "" : v.Average().ToString(f, CultureInfo.InvariantCulture);
        static string Max(List<double> v, string f) => v.Count == 0 ? "" : v.Max().ToString(f, CultureInfo.InvariantCulture);
    }

    private static DateTime BucketStart(DateTime local) => new(local.Ticks - local.Ticks % SensorBucket.Ticks, local.Kind);
    private static DateTime Local(DateTimeOffset value) => value.ToLocalTime().DateTime;
    // 시각 칸: 그냥 쓰면 한셀·엑셀이 날짜 숫자로 바꿔서 칸이 좁을 때 ####으로 보이고 초·24시간 표기가 사라집니다.
    // ="..." 로 감싸면 적은 글자 그대로 보입니다. (2026-10-23, 한셀에서 확인)
    private static string Time(DateTimeOffset value, string format = "yyyy-MM-dd HH:mm:ss") =>
        $"=\"{Local(value).ToString(format, CultureInfo.InvariantCulture)}\"";
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Rate(int part, int total) => total == 0 ? "-" : (part * 100.0 / total).ToString("0.0", CultureInfo.InvariantCulture);

    private static string ResultText(InspectionResultType result) => result switch
    {
        InspectionResultType.Normal => "정상", InspectionResultType.Defect => "불량", _ => "판정 불가"
    };

    private static string StatusText(IssueStatus status) => status switch
    {
        IssueStatus.Occurred => "발생", IssueStatus.Acknowledged => "확인", IssueStatus.InProgress => "조치 중",
        IssueStatus.CompletionRequested => "완료 요청", IssueStatus.Closed => "종결", _ => status.ToString()
    };

    // 사람이 입력한 글자(문제 제목·작성자 등)용: 쉼표·따옴표·줄바꿈은 따옴표로 감싸고,
    // =, +, -, @로 시작하면 엑셀에서 수식으로 실행되지 않도록 앞에 '를 붙입니다. (RequestLogService와 같은 규칙)
    private static string Text(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
