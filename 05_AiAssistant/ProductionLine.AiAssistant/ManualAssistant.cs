using System.Text.Json;
using System.Text.RegularExpressions;
using ProductionLine.Shared;

namespace ProductionLine.AiAssistant;

/// <summary>
/// 매뉴얼 기반 로컬 상담. 외부 LLM, 실시간 센서 수집, 대화의 영구 저장은 수행하지 않는다.
/// 공용 API 계약을 유지하며 문제/질문/조치 메모를 답변에 반영한다.
/// </summary>
public sealed partial class ManualAssistant
{
    private static readonly IReadOnlyList<ManualEntry> Manuals = LoadManuals();

    public AiConsultResponse Consult(IssueDto issue, string question)
        => ConsultCore(issue, question, out _);

    private AiConsultResponse ConsultCore(IssueDto issue, string question, out string? manualId)
    {
        manualId = null;
        ArgumentNullException.ThrowIfNull(issue);
        if (string.IsNullOrWhiteSpace(question))
            return new("상담할 증상이나 질문을 입력해 주세요.", "입력 확인 · 매뉴얼 검색 전", false);
        if (question.Length > 4000)
            return new("질문을 4,000자 이내로 나누어 입력해 주세요.", "입력 확인 · 매뉴얼 검색 전", false);

        var incident = IncidentContext(issue);
        var issueText = Normalize(incident);
        var questionText = Normalize(question);
        var gas = Manuals.Single(x => x.Id == "gas");
        var questionMatches = Candidates(questionText);
        var issueMatches = Candidates(issueText);
        var followupQuestion = IsContextFollowup(questionText);
        var onlyGasContext = issueMatches.Count == 1 && issueMatches[0].Id == "gas";
        var questionGasAlert = IsGasAlert(question,
            questionMatches.Count == 0 && followupQuestion && onlyGasContext);
        var incidentGasAlert = IsGasAlert(incident, Matches(gas, Normalize(issue.Title)));
        // 현재 질문을 먼저 분류한다. 과거 AI 답변을 사건의 관측 정보로 취급하지 않는다.
        var candidates = questionMatches.Count > 0 ? questionMatches
            : followupQuestion ? issueMatches : new List<ManualEntry>();
        var contextualGasAlert = questionMatches.Count == 0 && followupQuestion &&
            onlyGasContext && incidentGasAlert;
        if (questionGasAlert || contextualGasAlert)
        {
            var leak = Manuals.Single(x => x.Id == "gas_leak");
            manualId = leak.Id;
            return Reply(issue, question, leak, leak.Notes, leak.Checks, leak.NextQuestion, leak.NeedAdmin);
        }

        if (HasAny(questionText, "인터록", "강제정상", "정상으로바꿔", "불량을정상", "자동종결", "종결해", "경보꺼", "경보해제", "재가동해"))
            return new("검사 결과 변경, 설비 조작, 경보 해제, 문제 종결은 상담 기능에서 수행하지 않습니다. 현재 문제와 요청 내용을 관리자에게 전달해 주세요.",
                "프로젝트 상담 정책 v1 · 권한과 책임", true);

        if (candidates.Count != 1)
        {
            if (candidates.Count == 0 && Regex.IsMatch(questionText, @"^(?:센서|장치|장비|공정)(?:가|이|는)?(?:이상해요|문제|이상|문의|문제예요)[.!?]*$"))
                return new("상담할 센서·장치가 어느 것인지와 증상을 알려주세요. 장치가 확인되면 등록된 매뉴얼 범위에서 안내합니다.", "입력 확인 · 장치와 증상 확인 필요", false);
            var message = candidates.Count > 1
                ? "여러 문제 상황이 함께 언급되었습니다. 먼저 상담할 상황과 증상을 하나씩 알려주세요."
                : NoManualEvidence().Answer;
            return candidates.Count == 0 ? NoManualEvidence() : new(message, "근거 미선택 · 장치와 증상 확인 필요", false);
        }

        var manual = candidates[0];
        manualId = manual.Id;
        var followup = manual.NextQuestion;
        var notes = manual.Notes;
        var steps = manual.Checks;
        var needAdmin = manual.NeedAdmin;
        var combined = questionText + (questionMatches.Count == 0 ? " " + issueText : "");
        if (manual.Id == "temperature" && HasReadingFailure(combined))
        {
            notes = "DHT11 읽기 실패는 실제 온습도 상승과 구분해야 합니다. 실패 값을 0도나 정상값으로 취급할 수 없습니다.";
            steps = ["오류 메시지, 마지막 정상 수신 시각과 읽기 주기를 확인해 주세요.",
                "프로그램의 센서 유형 설정이 실제 DHT11과 일치하는지 확인해 주세요.",
                "통신·체크섬 오류 여부를 기록하고, 배선 변경이 필요하면 장비별 절차를 확인할 담당자에게 문의해 주세요."];
            followup = "화면에는 NaN, 타임아웃, 체크섬 오류 중 어떤 오류가 표시되나요?";
        }
        if (HasAny(questionText, "해결됐", "해결되었", "복구됐", "정상으로돌아", "이제돼", "이제나와"))
        {
            steps = ["복구 사실을 확인했다면 수행한 조치와 확인 결과를 조치 기록에 남겨 주세요.",
                "사용자 앱에서 확인 결과와 조치 내용을 입력해 완료 요청을 보내세요. 관리자 확인 전에는 종결되지 않습니다."];
            followup = "어떤 조치 후 복구됐고, 어떤 결과로 확인했나요?";
        }
        else if (HasAny(questionText, "여전히", "계속안", "해봤", "해도안"))
        {
            needAdmin = true;
            steps = ["이미 수행한 점검과 결과를 조치 메모에 기록해 주세요.",
                "같은 조치를 반복하기보다 현재 오류, 마지막 수신 시각, 조치 기록을 단체 채팅에 공유해 주세요."];
            followup = "이미 확인한 항목과 그 결과를 알려주세요.";
        }
        if (manual.Id is not ("gas" or "gas_leak") && incidentGasAlert)
        {
            notes += "\r\n별도 확인: 선택한 문제에는 가스 경보·누출 의심 내용도 기록되어 있습니다. 실제 상황이면 현장 비상 절차와 안전 담당자 대응을 우선해 주세요.";
            needAdmin = true;
        }
        return Reply(issue, question, manual, notes, steps, followup, needAdmin);
    }

    private static AiConsultResponse Reply(IssueDto issue, string question, ManualEntry manual,
        string notes, IReadOnlyList<string> checks, string nextQuestion, bool needAdmin)
    {
        var action = string.IsNullOrWhiteSpace(issue.ActionNote) ? "등록된 조치 메모 없음" : OneLine(issue.ActionNote);
        var answer = $"[현재 상황]\r\n선택한 문제: {OneLine(issue.Title)}\r\n사용자 질문: {OneLine(question)}\r\n기록된 조치: {action}\r\n\r\n" +
            $"[안내]\r\n{notes}\r\n\r\n[확인 사항]\r\n" +
            string.Join("\r\n", checks.Select((value, index) => $"{index + 1}. {value}")) +
            $"\r\n\r\n[다음 확인]\r\n{nextQuestion}";
        if (!string.IsNullOrWhiteSpace(manual.CompletionNote))
            answer += $"\r\n\r\n[조치 후 확인]\r\n{manual.CompletionNote}";
        if (needAdmin) answer += "\r\n\r\n[관리자 문의]\r\n기준 확인 또는 관리자 검토가 필요합니다. 단체 채팅에서 관리자에게 공유해 주세요.";
        return new(answer, $"{manual.Title} · {manual.Version} · {manual.Section}\r\n{manual.SourceNote}", needAdmin);
    }

    private static bool Matches(ManualEntry entry, string normalized) =>
        entry.Keywords.Any(keyword => normalized.Contains(Normalize(keyword), StringComparison.Ordinal));
    private static List<ManualEntry> Candidates(string text)
    {
        text = RemoveNegatedGas(text);
        // 가스 긴급 대응은 IsGasAlert에서만 선택한다. 일반 측정 질문과 혼합하지 않는다.
        var candidates = Manuals.Where(x => x.Id != "gas_leak" && Matches(x, text)).ToList();
        var environmental = HasAny(text, "온도", "습도", "DHT11", "고온", "저온", "과열", "동결", "결로");
        if (environmental && HasReadingFailure(text))
        {
            candidates.RemoveAll(x => x.Id is "temperature_high" or "temperature_low" or "humidity_high");
            AddCandidate(candidates, "temperature");
        }
        else
        {
            if (HasAny(text, "고온", "과열") || Regex.IsMatch(text, @"온도(?:가|는|이)?(?:너무|매우|계속|많이|급격히|비정상적으로)?(?:높|상승|올라|올랐|초과)"))
                AddCandidate(candidates, "temperature_high");
            if (HasAny(text, "저온", "동결", "동파") || Regex.IsMatch(text, @"온도(?:가|는|이)?(?:너무|매우|계속|많이|급격히|비정상적으로)?(?:낮|하락|내려|떨어)"))
                AddCandidate(candidates, "temperature_low");
            if (HasAny(text, "고습", "결로") || Regex.IsMatch(text, @"습도(?:가|는|이)?(?:너무|매우|계속|많이|급격히|비정상적으로)?(?:높|상승|올라|올랐|초과)"))
                AddCandidate(candidates, "humidity_high");
            if (candidates.Any(x => x.Id is "temperature_high" or "temperature_low" or "humidity_high"))
                candidates.RemoveAll(x => x.Id == "temperature");
        }
        if (Regex.IsMatch(text, @"(?:컨베이어|컨베어|벨트|밸트).{0,12}(?:멈|정지|안돌|안움직|작동(?:이)?안|가동(?:이)?안)"))
            AddCandidate(candidates, "conveyor_stop");
        if (Regex.IsMatch(text, @"(?:제품|품목).{0,8}(?:인식|검출).{0,8}(?:안|못|오류|실패|불가)"))
            AddCandidate(candidates, "product_recognition");
        if (HasAny(text, "yolo") && Regex.IsMatch(text, @"(?:못찾|못잡|검출.{0,6}(?:안|못|오류|실패))"))
            AddCandidate(candidates, "product_recognition");
        var lightFailure = candidates.Any(x => x.Id == "inspection_light_off") ||
            Regex.IsMatch(text, @"(?<![a-z])led(?:on|off|status)?(?![a-z])") ||
            Regex.IsMatch(text, @"(?:조명|검사등|전등).{0,10}(?:꺼|안켜|켜지지|안들어|고장|불량|미점등|작동안)");
        if (lightFailure)
        {
            AddCandidate(candidates, "inspection_light_off");
            candidates.RemoveAll(x => x.Id is "inspection_quality" or "equipment" or "program");
        }
        if (candidates.Any(x => x.Id == "sorter_motor_failure"))
        {
            candidates.RemoveAll(x => x.Id is "equipment" or "program");
            // A moving belt mentioned before the sorter symptom is not a belt stop.
            if (Regex.IsMatch(text, @"(?:벨트|밸트|컨베이어|컨베어)(?:는|가)?(?:정상|돌|도는데|도는|회전|움직|운전중)"))
                candidates.RemoveAll(x => x.Id == "conveyor_stop");
        }
        var cameraDeviceFailure = Regex.IsMatch(text, @"(?:카메라|camera).{0,10}(?:장치|usb).{0,10}(?:인식|연결).{0,8}(?:안|못|오류|실패)") ||
            Regex.IsMatch(text, @"(?:카메라|영상|화면).{0,6}(?:안나|나오지않|검은|검정|미수신|없어요)");
        cameraDeviceFailure |= !HasAny(text, "제품", "품목", "yolo") &&
            Regex.IsMatch(text, @"(?:카메라|camera).{0,8}인식.{0,6}(?:안|못|오류|실패)");
        if (cameraDeviceFailure)
        {
            AddCandidate(candidates, "camera");
            candidates.RemoveAll(x => x.Id is "product_recognition" or "inspection_quality");
        }
        else if (lightFailure)
            candidates.RemoveAll(x => x.Id == "camera");
        if (candidates.Any(x => x.Id == "conveyor_stop"))
            candidates.RemoveAll(x => x.Id == "equipment");
        if (candidates.Any(x => x.Id == "product_recognition"))
            candidates.RemoveAll(x => x.Id is "camera" or "object_sensor" or "inspection_quality");
        // 특정 공정 질문이 '카메라/프로그램' 같은 일반 장치 표현보다 우선한다.
        if (candidates.Any(x => x.Id == "inspection_quality"))
            candidates.RemoveAll(x => x.Id is "camera" or "program");
        else if (candidates.Count > 1 && candidates.Any(x => x.Id != "program"))
            candidates.RemoveAll(x => x.Id == "program");
        if (candidates.Count > 1 && candidates.Any(x => x.Id != "device_connection"))
            candidates.RemoveAll(x => x.Id == "device_connection");
        return candidates;
    }

    private static void AddCandidate(List<ManualEntry> candidates, string id)
    {
        if (candidates.All(x => x.Id != id)) candidates.Add(Manuals.Single(x => x.Id == id));
    }
    private static bool HasReadingFailure(string text) => HasAny(text,
        "nan", "미수신", "읽기실패", "수신실패", "측정실패", "체크섬", "checksum", "timeout", "타임아웃") ||
        Regex.IsMatch(text, @"(?:온도|습도|온습도|dht11).{0,12}(?:안나|안떠|끊|읽기오류|수신오류)");

    private static string IncidentContext(IssueDto issue)
    {
        var title = Regex.Replace(issue.Title, @"^(?:관리자 확인 요청\s*·\s*)+", "");
        var description = issue.Description ?? string.Empty;
        // 기존 관리자 문의에도 적용: 상담 본문/인용 질문은 보존하되 분류 입력에서 제외한다.
        var quoted = Regex.Match(description,
            @"(?m)^\s*(?:사용자\s*질문\s*:|상담\s*내용\s*:|\[(?:AI 점검 안내|질문|안내|근거)\])");
        if (quoted.Success) description = description[..quoted.Index];
        return title + "\n" + description;
    }

    private static bool IsContextFollowup(string text) => HasAny(text,
        "어떤순서", "어떻게", "무엇부터", "뭐부터", "점검", "다음", "여전히", "계속안",
        "해봤", "해도안", "이제", "복구", "해결", "화면", "nan", "timeout", "체크섬", "타임아웃",
        "센서", "전원", "케이블", "값이", "수치", "경보", "냄새", "높아요", "낮아요", "높음", "낮음") ||
        Regex.IsMatch(text, @"(?:값(?:이|은|는)?|수치(?:가|는)?)[+-]?\d");

    private static bool IsGasAlert(string text, bool gasContext = false)
    {
        var gas = Manuals.Single(x => x.Id == "gas");
        // 안내 문구나 부정된 경보를 '실제 경보'로 승격하지 않는다. 자연어 판단의 대체물은 아니다.
        foreach (var clause in Regex.Split(text, @"[\r\n.!?;]+|그리고|이고|지만|반면"))
        {
            var value = Normalize(clause);
            value = RemoveNegatedGas(value);
            value = Regex.Replace(value, @"(?:경보|누출|누설)(?:기준|임계값|설정값|설정|값|해제조건|대응절차|대응방법|매뉴얼)", "");
            var otherDevice = Candidates(value).Any(x => x.Id != "gas");
            if ((Matches(gas, value) || (gasContext && !otherDevice)) &&
                HasAny(value, "경보", "누출", "누설", "냄새", "대피", "연기", "alarm", "leak", "smoke", "라이터", "화염", "불꽃"))
                return true;
        }
        return false;
    }
    private static string RemoveNegatedGas(string value) => Regex.Replace(value,
        @"(?:가스)?(?:누출|누설|경보|냄새)(?:은|는|이|가|도)?(?:없(?:어요|습니다|음|고|는데|다)?|아니(?:에요|다|고|라)?|아닙니다|안울(?:려요|림|려))", "");
    private static bool HasAny(string normalized, params string[] words) =>
        words.Any(word => normalized.Contains(Normalize(word), StringComparison.Ordinal));
    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[\s\-_]", "");
    private static string OneLine(string value)
    {
        var text = Regex.Replace(value, @"\s+", " ").Trim();
        return text.Length > 500 ? text[..500] + "…" : text;
    }
    private static IReadOnlyList<ManualEntry> LoadManuals()
    {
        using var stream = typeof(ManualAssistant).Assembly.GetManifestResourceStream("ProductionLine.AiAssistant.Manuals.json")
            ?? throw new InvalidOperationException("상담 자료 리소스가 없습니다.");
        var entries = JsonSerializer.Deserialize<ManualEntry[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("상담 자료가 비어 있습니다.");
        if (entries.Length == 0 || entries.Select(x => x.Id).Distinct().Count() != entries.Length ||
            entries.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Title) ||
                string.IsNullOrWhiteSpace(x.Version) || string.IsNullOrWhiteSpace(x.Section) ||
                string.IsNullOrWhiteSpace(x.SourceNote) || string.IsNullOrWhiteSpace(x.Notes) ||
                string.IsNullOrWhiteSpace(x.NextQuestion) || x.Keywords is not { Length: > 0 } ||
                x.Checks is not { Length: > 0 } || x.Keywords.Any(string.IsNullOrWhiteSpace) ||
                x.Checks.Any(string.IsNullOrWhiteSpace)) ||
            new[] { "gas", "gas_leak", "temperature", "temperature_high", "temperature_low", "humidity_high", "conveyor_stop", "product_recognition" }
                .Any(id => !entries.Any(x => x.Id == id)) || entries.Any(x => !ValidFollowUps(x.FollowUpSteps)))
            throw new InvalidOperationException("상담 자료의 필수 항목 또는 고유 ID를 확인하세요.");
        return entries;
    }
    private sealed record ManualEntry(string Id, string Title, string Version, string Section,
        string SourceNote, string[] Keywords, string Notes, string[] Checks, string NextQuestion, bool NeedAdmin,
        string? CompletionNote = null, FollowUpStep[]? FollowUpSteps = null);
}
