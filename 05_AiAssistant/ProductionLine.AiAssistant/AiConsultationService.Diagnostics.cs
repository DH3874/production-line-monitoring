using ProductionLine.Shared;

namespace ProductionLine.AiAssistant;

public sealed partial class AiConsultationService
{
    private static DeviceDiagnosticsSnapshot CurrentDiagnostics(DeviceDiagnosticsSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        return snapshot with
        {
            Devices = snapshot.Devices.Select(d => d.State is not ("unknown" or "unavailable" or "stale") && !d.IsCurrent(now)
                ? d with { State = "stale", Detail = "수신 시각이 오래되어 현재 관측으로 사용하지 않습니다." } : d).ToArray(),
            AppStatus = now - snapshot.CapturedAt < TimeSpan.FromSeconds(6) ? snapshot.AppStatus : "오래된 앱 기록은 현재 진단에 사용하지 않습니다."
        };
    }

    public async Task<AiConsultResponse> ConsultWithDiagnosticsAsync(string userId, IssueDto issue, string question,
        DeviceDiagnosticsSnapshot diagnostics, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        if (diagnostics.LineId != issue.LineId) throw new ArgumentException("상담과 장비 상태의 라인이 다릅니다.");
        AiConsultResponse reply;
        if (!string.IsNullOrWhiteSpace(question) && question.Length <= 4000 &&
            diagnostics.Devices.Any(d => d.Id == "gas" && d.State == "alert" && d.IsCurrent(DateTimeOffset.UtcNow)))
        {
            var session = Acquire((userId, issue.LineId, issue.Id));
            if (session != null) { session.GasConcern = true; Release(session); }
            reply = Banner(_local.ConsultForUser(userId, issue, "가스 경보 신호가 수신되었습니다. " + question), "기본 안전 안내");
        }
        else reply = await ConsultCoreAsync(userId, issue, question, cancellationToken, diagnostics);
        if (reply.ManualSource.StartsWith("근거 없음") || reply.ManualSource.StartsWith("입력 확인") ||
            reply.ManualSource.StartsWith("상담 흐름")) return reply;
        var now = DateTimeOffset.UtcNow;
        diagnostics = CurrentDiagnostics(diagnostics);
        var block = $"[장비 확인 결과 · 조회 {diagnostics.CapturedAt.ToLocalTime():MM-dd HH:mm:ss}]\r\n" +
            string.Join("\r\n", diagnostics.Devices.Select(d => $"- {d.Name} ({d.Connection}): {DeviceDiagnosticsText.Describe(d, now)} · 보고 {d.ObservedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "미수신"}")) +
            (string.IsNullOrWhiteSpace(diagnostics.AppStatus) ? "" : "\r\n앱 상태 기록: " + diagnostics.AppStatus[..Math.Min(diagnostics.AppStatus.Length, 600)]) +
            "\r\n작업자 앱·보드가 보고한 상태입니다. 개별 핀의 연결·실물 동작과 현재 현장 안전을 보증하지 않습니다.\r\n\r\n";
        if (reply.Answer.StartsWith("[기본 안전"))
            return reply with { Answer = reply.Answer + "\r\n\r\n" + block };
        // Keep the AI/fallback/safety banner first so existing UI classification remains valid.
        var split = reply.Answer.IndexOf('\n');
        var banner = reply.Answer.StartsWith("[AI ") || reply.Answer.StartsWith("[현재 AI ") || reply.Answer.StartsWith("[기본 ");
        var answer = banner && split >= 0 ? reply.Answer[..(split + 1)] + block + reply.Answer[(split + 1)..] : block + reply.Answer;
        return reply with { Answer = answer };
    }
}
