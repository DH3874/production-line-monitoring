using System.Globalization;
using System.Text.RegularExpressions;
using ProductionLine.Shared;

namespace ProductionLine.AiAssistant;

public sealed partial class ManualAssistant
{
    // 서버 수명 동안만 보관. 사용자·라인·문제별로 분리하며 최대 개수와 유효시간을 제한한다.
    private readonly object _conversationGate = new();
    private readonly Dictionary<(string User, int Line, Guid Issue), Conversation> _conversations = new();
    private const int MaxConversations = 500;
    private static readonly TimeSpan ConversationLifetime = TimeSpan.FromHours(2);

    public AiConsultResponse ConsultForUser(string userId, IssueDto issue, string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(issue);
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4000)
            return Consult(issue, question);

        lock (_conversationGate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _conversations.Where(x => now - x.Value.UpdatedAt > ConversationLifetime).Select(x => x.Key).ToArray())
                _conversations.Remove(key);
            var sessionKey = (userId, issue.LineId, issue.Id);
            _conversations.TryGetValue(sessionKey, out var previous);
            var reset = Regex.Match(question.Trim(), @"^(?:새\s*상담|처음부터)(?:\s*[:：]\s*|\s+|$)");
            if (reset.Success)
            {
                question = question.Trim()[reset.Length..].Trim();
                _conversations.Remove(sessionKey);
                if (question.Length == 0)
                    return new("이 문제의 상담 확인 목록을 처음부터 시작합니다. 상담할 상황을 입력해 주세요.", "상담 흐름 v1 · 새 상담", false);
            }

            var normalized = Normalize(question);
            var candidates = Candidates(normalized);
            var urgent = IsGasAlert(question, previous?.Manual.Id is "gas" or "gas_leak");
            var forbidden = HasAny(normalized, "인터록", "강제정상", "정상으로바꿔", "불량을정상", "자동종결", "종결해", "경보꺼", "경보해제", "재가동해");
            if (!reset.Success && previous is not null && !urgent && !forbidden && CanContinue(previous, candidates, normalized))
            {
                previous.UpdatedAt = now;
                return Continue(previous, question);
            }

            var response = ConsultCore(issue, question, out var id);
            if (id is null) return response;
            var manual = Manuals.Single(x => x.Id == id);
            var steps = manual.FollowUpSteps ?? [new FollowUpStep("detail", manual.NextQuestion, "text",
                "추가로 알려주신 내용을 확인했습니다. 등록된 점검 절차와 함께 관리자에게 전달해 주세요.")];
            var conversation = new Conversation(manual, steps, now)
            {
                GasConcern = id == "gas_leak" || previous?.GasConcern == true ||
                    IsGasAlert(IncidentContext(issue), Matches(Manuals.Single(x => x.Id == "gas"), Normalize(issue.Title)))
            };
            if (_conversations.Count >= MaxConversations)
                _conversations.Remove(_conversations.MinBy(x => x.Value.UpdatedAt).Key);
            _conversations[sessionKey] = conversation;
            if (steps.Length > 0) response = response with { Answer = ReplaceNextQuestion(response.Answer, steps[0].Question) };
            return AddGasNotice(response, conversation);
        }
    }

    private static bool CanContinue(Conversation conversation, List<ManualEntry> candidates, string text)
    {
        if (conversation.Step < conversation.Steps.Length && conversation.Steps[conversation.Step] is { Kind: "choice", Choices: { } choices } &&
            choices.Count(c => c.Keywords.Any(k => text.Contains(Normalize(k)))) == 1 &&
            candidates.All(x => x.Id == conversation.Manual.Id || conversation.Manual.Id == "product_recognition" &&
                x.Id is "inspection_quality" or "camera" or "object_sensor"))
            return true;
        if (candidates.Count == 0) return CanUseManualContext(text);
        if (candidates.Count != 1) return false;
        if (candidates[0].Id == conversation.Manual.Id) return true;
        // '습도 80%' 같은 답은 일반 DHT11 자료로 주제를 바꾸지 않는다.
        if (candidates[0].Id == "temperature" && !HasReadingFailure(text))
            return conversation.Manual.Id switch
            {
                "humidity_high" => text.Contains("습도") && !text.Contains("온도"),
                "temperature_high" or "temperature_low" => text.Contains("온도") && !text.Contains("습도"),
                _ => false
            };
        return conversation.Manual.Id == "gas_leak" && candidates[0].Id == "gas";
    }

    private static AiConsultResponse Continue(Conversation conversation, string question)
    {
        var source = Source(conversation.Manual);
        if (conversation.Step >= conversation.Steps.Length)
            return AddGasNotice(new("[지금까지 확인한 내용]\r\n" + Summary(conversation) +
                "\r\n\r\n확인 목록을 마쳤습니다. 조치 결과를 기록해 완료 요청을 보내거나, 미해결이면 단체 채팅에서 관리자에게 공유하세요. 다시 시작하려면 '새 상담'을 입력할 수 있습니다.", source, true), conversation);

        var step = conversation.Steps[conversation.Step];
        if (!TryAnswer(step, question, out var recorded, out var guidance))
            return AddGasNotice(new($"[이어서 확인]\r\n직전 질문: {step.Question}\r\n\r\n{guidance}\r\n\r\n[다음 확인]\r\n{step.Question}", source, conversation.Manual.NeedAdmin), conversation);

        conversation.Answers.RemoveAll(x => x.Key == step.Key);
        conversation.Answers.Add((step.Key, step.Question, recorded));
        if (!step.RequireYesToAdvance || recorded.StartsWith("네", StringComparison.Ordinal))
            conversation.Step++;
        var next = conversation.Step < conversation.Steps.Length
            ? conversation.Steps[conversation.Step].Question
            : "확인 목록을 마쳤습니다. 수행한 조치와 결과를 기록하고, 해결됐다면 완료 요청을 보내세요. 미해결이면 단체 채팅에서 관리자에게 공유해 주세요.";
        var answer = $"[이전 확인]\r\n{step.Question}\r\n\r\n[지금까지 확인한 내용 — 사용자 제공]\r\n{Summary(conversation)}\r\n\r\n[안내]\r\n{guidance}\r\n\r\n[다음 확인]\r\n{next}";
        if (conversation.Manual.Id == "gas_leak")
            answer = conversation.Manual.Notes + "\r\n대피·신고를 우선하고, 담당자가 확인하기 전까지 현장에 재진입하거나 작업을 재개하지 마세요.\r\n\r\n" + answer;
        return AddGasNotice(new(answer, source, conversation.Manual.NeedAdmin || conversation.Step >= conversation.Steps.Length), conversation);
    }

    private static bool TryAnswer(FollowUpStep step, string question, out string recorded, out string guidance)
    {
        recorded = OneLine(question);
        guidance = step.Guidance;
        var text = Normalize(question);
        var unknown = HasAny(text, "모르겠", "모름", "미정", "확인못", "측정못", "측정안", "값없", "센서없");
        if (text is "다음" or "다음확인" or "계속")
        {
            guidance = "직전 질문에 대한 확인 결과가 아직 없습니다. 확인한 내용을 알려주세요. 모르면 '모르겠어요'라고 답할 수 있습니다.";
            return false;
        }
        if (unknown)
        {
            recorded = "확인되지 않음 (사용자 답변: " + recorded + ")";
            guidance = step.RequireYesToAdvance
                ? "안전한 장소인지 확인되지 않았습니다. 채팅보다 대피·신고와 안전 담당자의 대응을 우선해 주세요. 안전한 장소에 도착한 뒤에만 답해 주세요."
                : "이 항목은 아직 확인되지 않은 것으로 남겼습니다. 값을 임의로 추정하지 않고 다음 확인으로 넘어가겠습니다. " + step.Guidance;
            return true;
        }
        if (step.Kind is "temperature" or "humidity")
        {
            var numbers = Regex.Matches(question, @"[+-]?\d+(?:[.,]\d+)?");
            var wrongUnit = step.Kind == "humidity" ? text.Contains("온도") || text.Contains("도씨") || text.Contains("℃")
                : text.Contains("습도") || text.Contains('%');
            if (numbers.Count != 1 || wrongUnit || !double.TryParse(numbers[0].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || (step.Kind == "humidity" && (value < 0 || value > 100)) || (step.Kind == "temperature" && value < -273.15))
            {
                guidance = step.Kind == "humidity" ? "현재 상대습도 하나를 0~100%RH 범위로 알려주세요. 예: 80%. 모르면 '모르겠어요'라고 답해도 됩니다."
                    : "현재 질문에 해당하는 온도 하나를 °C 단위로 알려주세요. 예: 35도. 모르면 '모르겠어요'라고 답해도 됩니다.";
                return false;
            }
            recorded = value.ToString(CultureInfo.InvariantCulture) + (step.Kind == "humidity" ? "%RH" : "°C") + " (사용자 제공, 실시간 측정·기준 확인 전)";
            return true;
        }
        if (step.Kind == "yes_no")
        {
            var no = text is "아니요" or "아니오" or "아뇨" or "아니" || HasAny(text, "없", "안보", "안켜", "사용안", "사용하지않");
            var yes = text is "네" or "예" or "응" or "맞아요" || HasAny(text, "있", "보여", "사용해", "사용중", "켜져", "켰");
            if (!no && !yes)
            {
                guidance = "직전 질문에 대해 '네/있어요' 또는 '아니요/없어요'로 알려주세요. 확인이 어렵다면 '모르겠어요'라고 답할 수 있습니다.";
                return false;
            }
            recorded = (no ? "아니요" : "네") + " (사용자 답변: " + recorded + ")";
            guidance = (no ? step.IfNo : step.IfYes) ?? step.Guidance;
            return true;
        }
        if (step.Kind == "choice")
        {
            var choices = (step.Choices ?? []).Where(x => x.Keywords.Any(k => text.Contains(Normalize(k)))).ToList();
            if (choices.Count != 1)
            {
                guidance = "직전 질문의 선택지 중 하나를 알려주세요: " + string.Join(" / ", (step.Choices ?? []).Select(x => x.Label)) + ". 확인이 어렵다면 '모르겠어요'라고 답할 수 있습니다.";
                return false;
            }
            recorded = choices[0].Label + " (사용자 답변: " + recorded + ")";
            guidance = choices[0].Guidance;
            return true;
        }
        if (text.Length < 2)
        {
            guidance = "확인한 상태나 조치 결과를 조금 더 구체적으로 알려주세요.";
            return false;
        }
        return true;
    }

    private static string Summary(Conversation conversation) => string.Join("\r\n",
        conversation.Answers.Select(x => $"- {x.Question} → {x.Answer}"));
    private static string Source(ManualEntry manual) => $"{manual.Title} · {manual.Version} · {manual.Section}\r\n{manual.SourceNote}";
    private static string ReplaceNextQuestion(string answer, string question) => Regex.Replace(answer,
        @"(?s)(\[다음 확인\]\r?\n).*?(?=\r?\n\r?\n\[|$)", match => match.Groups[1].Value + question);
    private static AiConsultResponse AddGasNotice(AiConsultResponse response, Conversation conversation) =>
        conversation.GasConcern && conversation.Manual.Id != "gas_leak"
            ? response with { Answer = response.Answer + "\r\n\r\n[별도 안전 확인]\r\n같은 문제의 이전 대화에 가스 경보·누출 의심이 있습니다. 실제 상황이면 현장 비상 절차와 안전 담당자의 대응을 우선해 주세요.", NeedAdmin = true }
            : response;

    private static bool ValidFollowUps(FollowUpStep[]? steps) => steps is null ||
        (steps.Length is > 0 and <= 8 && steps.Select(x => x.Key).Distinct().Count() == steps.Length &&
        steps.All(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Question) &&
            !string.IsNullOrWhiteSpace(x.Guidance) && x.Kind is "temperature" or "humidity" or "yes_no" or "choice" or "text" &&
            (x.Kind != "choice" || x.Choices is { Length: > 0 } && x.Choices.All(c =>
                !string.IsNullOrWhiteSpace(c.Label) && !string.IsNullOrWhiteSpace(c.Guidance) &&
                c.Keywords is { Length: > 0 } && c.Keywords.All(k => !string.IsNullOrWhiteSpace(k))))));

    private sealed class Conversation(ManualEntry manual, FollowUpStep[] steps, DateTimeOffset updatedAt)
    {
        public ManualEntry Manual { get; } = manual;
        public FollowUpStep[] Steps { get; } = steps;
        public int Step { get; set; }
        public bool GasConcern { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
        public List<(string Key, string Question, string Answer)> Answers { get; } = [];
    }
    private sealed record FollowUpStep(string Key, string Question, string Kind, string Guidance,
        string? IfYes = null, string? IfNo = null, FollowUpChoice[]? Choices = null, bool RequireYesToAdvance = false);
    private sealed record FollowUpChoice(string Label, string[] Keywords, string Guidance);
}
