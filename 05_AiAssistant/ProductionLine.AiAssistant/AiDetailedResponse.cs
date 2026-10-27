using System.Text.Json;

namespace ProductionLine.AiAssistant;

internal static class AiDetailedResponse
{
    internal const string Instructions = """
        답변을 situation, immediateActions, checks, verification, nextQuestion의 다섯 부분으로 작성하세요.
        situation은 사용자가 보고한 상태와 아직 미확인인 내용을 요약하세요. 원인 후보는 가능성으로만 설명하세요.
        immediateActions는 지금 우선할 조치 1~3개, checks는 확인 방법과 그 결과에 따른 다음 행동을 담은 2~5개 단계입니다.
        매뉴얼 범위 안에서 '어디를 확인하고, 무엇을 구분하고, 결과에 따라 누구에게 전달하는지' 구체적으로 설명하세요.
        온도가 이상하다는 모호한 질문에는 고온·저온·읽기 오류 가능성을 구분하며 임의의 정상 온도나 부품 수리를 만들지 마세요.
        이미 수행한 제습·청소·점검을 다시 처음부터 시키지 말고, 결과를 반영해 아직 확인하지 않은 항목으로 진행하세요.
        verification에는 조치 후 확인할 항목과 관리자 검토·완료 요청 기준을 넣으세요. 재가동을 승인하지 마세요.
        nextQuestion에는 사용자가 아직 말하지 않은 가장 중요한 사항을 묻는 질문을 정확히 한 개 넣으세요.
        이전 대화에 수치·결로·조치 결과가 있으면 그것을 요약에 반영하고 이미 받은 내용을 다시 묻지 마세요.
        모든 상황에 비상정지를 일괄 권하지 말고 해당 매뉴얼과 보고된 위험에 맞게 안내하세요.
        자료로 설명할 수 없으면 hasEvidence=false, manualIds=[], needAdmin=true로 반환하고 조치 배열은 비워 두세요.
        manualIds에는 실제 사용한 자료 ID만 넣으세요. 총 답변은 보통 500~900자이며 같은 문장을 항목마다 반복하지 마세요.
        """;

    internal static object Schema(IReadOnlyList<AiManualReference> references) => new
    {
        type = "object",
        properties = new
        {
            situation = new { type = "string", description = "사용자 보고·미확인 사항·가능성으로 구분한 현재 상황" },
            immediateActions = new { type = "array", items = new { type = "string" }, maxItems = 3 },
            checks = new { type = "array", items = new { type = "string" }, maxItems = 5 },
            verification = new { type = "string", description = "조치 후 확인·관리자 검토 기준. 재가동 승인 금지" },
            nextQuestion = new { type = "string", description = "이미 받은 사실을 되묻지 않는 후속 질문 한 개" },
            manualIds = new { type = "array", items = new Dictionary<string, object>
            { ["type"] = "string", ["enum"] = references.Select(x => x.Id).ToArray() } },
            needAdmin = new { type = "boolean" },
            hasEvidence = new { type = "boolean" }
        },
        required = new[] { "situation", "immediateActions", "checks", "verification", "nextQuestion", "manualIds", "needAdmin", "hasEvidence" },
        additionalProperties = false
    };

    internal static AiGeneratedAnswer Parse(string text, AiProviderOptions options, IReadOnlyList<AiManualReference> references)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var names = new[] { "situation", "immediateActions", "checks", "verification", "nextQuestion", "manualIds", "needAdmin", "hasEvidence" };
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != names.Length
            || names.Any(name => !root.TryGetProperty(name, out _))) throw new AiServiceException("invalid_answer_shape");
        foreach (var name in new[] { "situation", "verification", "nextQuestion" })
            if (root.GetProperty(name).ValueKind != JsonValueKind.String) throw new AiServiceException("invalid_answer_shape");
        foreach (var name in new[] { "immediateActions", "checks", "manualIds" })
            if (root.GetProperty(name).ValueKind != JsonValueKind.Array) throw new AiServiceException("invalid_answer_shape");
        foreach (var name in new[] { "needAdmin", "hasEvidence" })
            if (root.GetProperty(name).ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new AiServiceException("invalid_answer_shape");
        var detail = JsonSerializer.Deserialize<Details>(text, AiResponseProtocol.Json)!;
        if (detail.ImmediateActions.Length > 3 || detail.Checks.Length > 5
            || detail.ImmediateActions.Concat(detail.Checks).Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 900)
            || detail.HasEvidence && (string.IsNullOrWhiteSpace(detail.Situation) || string.IsNullOrWhiteSpace(detail.Verification)
                || string.IsNullOrWhiteSpace(detail.NextQuestion) || detail.ImmediateActions.Length == 0 || detail.Checks.Length < 2))
            throw new AiServiceException("invalid_answer_shape");
        string Numbered(IEnumerable<string> values) => string.Join("\r\n", values.Select((x, i) => $"{i + 1}. {x}"));
        var answer = $"[현재 상황]\r\n{detail.Situation}\r\n\r\n[우선 조치]\r\n{Numbered(detail.ImmediateActions)}"
            + $"\r\n\r\n[점검 순서]\r\n{Numbered(detail.Checks)}\r\n\r\n[조치 후 확인]\r\n{detail.Verification}"
            + $"\r\n\r\n[다음 확인]\r\n{detail.NextQuestion}";
        return AiResponseProtocol.Parse(JsonSerializer.Serialize(new AiGeneratedAnswer(answer, detail.ManualIds, detail.NeedAdmin, detail.HasEvidence),
            AiResponseProtocol.Json), options, references);
    }
    private sealed record Details(string Situation, string[] ImmediateActions, string[] Checks,
        string Verification, string NextQuestion, string[] ManualIds, bool NeedAdmin, bool HasEvidence);
}
