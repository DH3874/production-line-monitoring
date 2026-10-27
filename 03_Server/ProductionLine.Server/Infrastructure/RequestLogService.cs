using System.Text;
using Microsoft.EntityFrameworkCore;
using ProductionLine.Server.Data;

namespace ProductionLine.Server.Infrastructure;

// [통신 기록 담당] 이 파일에 4가지가 들어 있습니다.
// 1) RequestLogOptions        : 보관 기간(7일)과 백업 폴더 위치
// 2) RequestLogItems          : 각 API 창구가 "누가, 어느 문제, 메모"를 기록 장치에 넘길 때 쓰는 이름표
// 3) RequestLogService        : 기록 한 줄 저장(Record), 오래된 기록 CSV 백업 후 삭제(CleanupExpired)
// 4) RequestLogCleanupService : 서버가 켜질 때 한 번, 그 뒤 하루 한 번 CleanupExpired를 자동 실행
// 기록을 남기는 시점은 Program.cs의 "통신 기록 장치"(app.Use) 부분입니다.
// 백업 파일 위치: App_Data/통신기록_백업/request-logs_날짜_시각.csv (엑셀로 열림)

public sealed class RequestLogOptions
{
    // 이 기간이 지난 기록은 CSV로 백업한 뒤 DB에서 지웁니다. appsettings.json의 RequestLogRetentionDays로 바꿉니다.
    public int RetentionDays { get; init; } = 7;
    public string BackupDirectory { get; init; } = "";
}

// 각 창구가 기록에 남길 정보를 요청 처리 중에 넣어 두는 HttpContext.Items 열쇠입니다.
public static class RequestLogItems
{
    public const string UserId = "RequestLog.UserId";
    public const string IssueId = "RequestLog.IssueId";
    public const string Note = "RequestLog.Note";
    // 조회 요청이라도 기록할 때 넣습니다. 예: 새 메시지를 응답으로 보낸 경우
    public const string Force = "RequestLog.Force";
}

public sealed class RequestLogService
{
    private static readonly Dictionary<string, string> ActionNames = new()
    {
        ["POST /api/auth/login"] = "로그인",
        ["GET /api/lines"] = "라인 조회",
        ["GET /api/users"] = "사용자 목록 조회",
        ["POST /api/users"] = "사용자 추가",
        ["PUT /api/users/{id}"] = "사용자 수정",
        ["GET /api/inspections"] = "검사 이력 조회",
        ["POST /api/inspections/simulate"] = "검사 실행",
        ["GET /api/issues"] = "문제 목록 조회",
        ["POST /api/issues"] = "문제 등록",
        ["PUT /api/issues/{id:guid}/status"] = "문제 상태 변경",
        ["POST /api/ai/consult"] = "AI 상담",
        ["GET /api/issues/{id:guid}/messages"] = "메시지 읽기",
        ["POST /api/issues/{id:guid}/messages"] = "메시지 보내기",
        ["GET /api/reports/lines/{lineId:int}"] = "보고서 만들기"
    };

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<RequestLogService> _logger;

    public RequestLogService(IDbContextFactory<AppDbContext> contextFactory, ILogger<RequestLogService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public static string ActionName(string method, string? routePattern, string path) =>
        routePattern is not null && ActionNames.TryGetValue($"{method} {routePattern}", out var name)
            ? name
            : $"{method} {routePattern ?? path}";

    // 기록 저장에 실패해도 원래 요청은 정상 처리되도록 예외를 밖으로 내보내지 않습니다.
    public void Record(RequestLogEntity entry)
    {
        try
        {
            entry.UserId = Cut(entry.UserId, 100);
            entry.Path = Cut(entry.Path, 300) ?? "";
            entry.Note = Cut(entry.Note, 500);
            using var db = _contextFactory.CreateDbContext();
            db.RequestLogs.Add(entry);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "통신 기록 저장 실패: {Method} {Path} {StatusCode}",
                entry.Method, entry.Path, entry.StatusCode);
        }
    }

    // 보관 기간이 지난 기록을 CSV 파일로 먼저 백업하고, 백업에 성공했을 때만 DB에서 지웁니다.
    public int CleanupExpired(RequestLogOptions options, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-options.RetentionDays);
        using var db = _contextFactory.CreateDbContext();
        var expired = db.RequestLogs.AsNoTracking().Where(x => x.ReceivedAt < cutoff)
            .OrderBy(x => x.Id).ToList();
        if (expired.Count == 0) return 0;

        Directory.CreateDirectory(options.BackupDirectory);
        var file = Path.Combine(options.BackupDirectory, $"request-logs_{now.LocalDateTime:yyyy-MM-dd_HHmmss}.csv");
        WriteCsv(file, expired);

        // 백업한 기록만 지웁니다. 백업 뒤에 들어온 기록은 시각이 기준보다 늦어서 지워지지 않습니다.
        var maxId = expired[^1].Id;
        var deleted = db.RequestLogs.Where(x => x.Id <= maxId && x.ReceivedAt < cutoff).ExecuteDelete();

        db.RequestLogs.Add(new RequestLogEntity
        {
            ReceivedAt = now,
            UserId = "system",
            Method = "-",
            Path = "-",
            Action = "통신 기록 자동 정리",
            StatusCode = 200,
            Note = $"{options.RetentionDays}일 지난 기록 {deleted}건을 {Path.GetFileName(file)}에 백업 후 삭제"
        });
        db.SaveChanges();
        return deleted;
    }

    private static void WriteCsv(string file, IReadOnlyList<RequestLogEntity> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("번호,시각,사용자,PC주소,요청,기능,문제번호,결과코드,걸린시간(ms),메모");
        foreach (var x in rows)
        {
            builder.AppendJoin(',',
                x.Id,
                Csv(x.ReceivedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(x.UserId),
                Csv(x.ClientIp),
                Csv($"{x.Method} {x.Path}"),
                Csv(x.Action),
                Csv(x.IssueId?.ToString()),
                x.StatusCode,
                x.DurationMs,
                Csv(x.Note));
            builder.AppendLine();
        }

        // 엑셀에서 한글이 깨지지 않도록 BOM이 있는 UTF-8로 저장하고, 다 쓴 뒤에 이름을 바꿔 완성본만 남깁니다.
        var temp = file + ".tmp";
        File.WriteAllText(temp, builder.ToString(), new UTF8Encoding(true));
        File.Move(temp, file, overwrite: true);
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // 로그인 실패 시 입력한 아이디처럼 사람이 넣은 값이 엑셀에서 수식으로 실행되지 않도록,
        // =, +, -, @, 탭, 줄바꿈으로 시작하면 앞에 '를 붙여 글자로 보이게 합니다. 따옴표만으로는 막히지 않습니다.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
            value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string? Cut(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

// 서버가 켜질 때 한 번, 그 뒤로 하루에 한 번 오래된 통신 기록을 정리합니다.
public sealed class RequestLogCleanupService : BackgroundService
{
    private readonly RequestLogService _logs;
    private readonly RequestLogOptions _options;
    private readonly ILogger<RequestLogCleanupService> _logger;

    public RequestLogCleanupService(RequestLogService logs, RequestLogOptions options,
        ILogger<RequestLogCleanupService> logger)
    {
        _logs = logs;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                var deleted = _logs.CleanupExpired(_options, DateTimeOffset.Now);
                if (deleted > 0)
                    _logger.LogInformation("통신 기록 {Count}건을 백업 후 정리했습니다.", deleted);
            }
            catch (Exception ex)
            {
                // 백업에 실패하면 지우지 않고 다음 정리 때 다시 시도합니다.
                _logger.LogWarning(ex, "통신 기록 자동 정리에 실패했습니다. 다음 정리 때 다시 시도합니다.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
