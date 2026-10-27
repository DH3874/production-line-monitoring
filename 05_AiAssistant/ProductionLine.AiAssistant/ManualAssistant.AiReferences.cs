using ProductionLine.Shared;

namespace ProductionLine.AiAssistant;

public sealed partial class ManualAssistant
{
    internal static AiManualReference[] ReferencesForAi(IssueDto issue, string question, IReadOnlyList<string> previousIds)
    {
        // 온습도 일반 질문에도 수신 오류 자료와 실제 고온·저온·고습 대응 자료를 함께 제공한다.
        IEnumerable<string> Expand(IEnumerable<string> values) => values.SelectMany(id => id == "temperature"
            ? new[] { id, "temperature_high", "temperature_low", "humidity_high" } : new[] { id });
        var current = Candidates(Normalize(question));
        if (current.Count == 0 && !CanUseManualContext(question)) return [];
        var ids = Expand(current.Select(x => x.Id))
            .Concat(Expand(previousIds)).Concat(Expand(Candidates(Normalize(IncidentContext(issue))).Select(x => x.Id)))
            .Distinct().Take(6).ToArray();

        return ids.Select(id => Manuals.Single(x => x.Id == id)).Select(x =>
            new AiManualReference(x.Id, x.Title, Source(x), x.Notes, x.Checks, x.NextQuestion, x.NeedAdmin, x.CompletionNote ?? string.Empty)).ToArray();
    }

    internal static object IssueForAi(IssueDto issue, bool sensorReadingsAvailable = false) => new
    {
        title = OneLine(issue.Title),
        context = OneLine(IncidentContext(issue)),
        status = issue.Status.ToString(),
        actionNote = OneLine(issue.ActionNote ?? string.Empty),
        sensorReadingsAvailable
    };

    internal bool TryImmediateReply(IssueDto issue, string question, bool previousGasConcern, out AiConsultResponse reply)
    {
        reply = ConsultCore(issue, question, out var id);
        if (id == "gas_leak" || reply.ManualSource.StartsWith("프로젝트 상담 정책", StringComparison.Ordinal)) return true;
        if (previousGasConcern && IsGasAlert(question, true))
        {
            var gas = Manuals.Single(x => x.Id == "gas_leak");
            reply = Reply(issue, question, gas, gas.Notes, gas.Checks, gas.NextQuestion, true);
            return true;
        }
        return false;
    }

    internal static AiConsultResponse BasicReply(IssueDto issue, string question, string referenceId)
    {
        var manual = Manuals.Single(x => x.Id == referenceId);
        return Reply(issue, question, manual, manual.Notes, manual.Checks, manual.NextQuestion, manual.NeedAdmin);
    }
}

internal sealed record AiManualReference(string Id, string Title, string Source, string Notes,
    string[] Checks, string NextQuestion, bool NeedAdmin, string CompletionNote);
