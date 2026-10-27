using System.Net;
using System.Text;
using System.Text.Json;
using ProductionLine.AiAssistant;
using ProductionLine.Shared;

internal static class GeminiIntegrationChecks
{
    public static async Task RunLiveAsync(string? reportPath)
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("GEMINI_API_KEY is not set.");
        var model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.5-flash-lite";
        var events = new List<string>();
        using var diagnostics = new LiveDiagnosticsHandler(key);
        using var http = new HttpClient(diagnostics) { Timeout = Timeout.InfiniteTimeSpan };
        var service = new AiConsultationService(new(), new(http), new()
        { Provider = AiProvider.Gemini, ApiKey = key, Model = model, MaxOutputTokens = 2048, RequestTimeout = TimeSpan.FromSeconds(30) }, events.Add);
        var now = DateTimeOffset.UtcNow;
        var issue = new IssueDto(Guid.NewGuid(), 1, IssueType.ProductionLine, "가상 습도 상담 테스트", "실제 현장 사건이 아닌 가상 상황",
            IssueStatus.Occurred, "test", null, now, now);
        var tests = new List<object>();
        foreach (var question in new[] { "습도가 높아요. 무엇부터 확인하나요?", "습도는 80%이고 결로가 보여요.", "제습기를 켰는데 아직 80%예요. 다음은 무엇을 확인하나요?" })
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var reply = await service.ConsultAsync("synthetic-test-user", issue, question);
            var generated = reply.Answer.StartsWith("[AI 생성 안내]");
            tests.Add(new { question, generated, elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 2), response = reply });
            Console.WriteLine($"Live Gemini turn {tests.Count}: {(generated ? "AI response received" : "manual fallback")}, {timer.Elapsed.TotalSeconds:F2}s");
            var report = JsonSerializer.Serialize(new { model, testedAt = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)), tests, events, providerErrors = diagnostics.Errors },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }).Replace(key, "[REDACTED]", StringComparison.Ordinal);
            if (reportPath is not null) File.WriteAllText(reportPath, report, Encoding.UTF8);
            if (!generated) break;
        }
        Console.WriteLine("Live test finished. The key was not saved to a file.");
        foreach (var code in events) Console.WriteLine(code);
    }

    private sealed class LiveDiagnosticsHandler(string key) : DelegatingHandler(new HttpClientHandler())
    {
        public List<object> Errors { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (response.IsSuccessStatusCode) return response;
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[65536];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(count), token);
                    if (read == 0) break;
                    count += read;
                }
                using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
                var error = json.RootElement.GetProperty("error");
                var message = error.TryGetProperty("message", out var text) ? text.GetString() ?? string.Empty : string.Empty;
                message = message.Replace(key, "[REDACTED]", StringComparison.Ordinal);
                message = System.Text.RegularExpressions.Regex.Replace(message, @"[A-Za-z0-9_-]{30,}", "[REDACTED]");
                Errors.Add(new { httpStatus = (int)response.StatusCode, message = message[..Math.Min(message.Length, 600)] });
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IOException)
            {
                Errors.Add(new { httpStatus = (int)response.StatusCode, message = "Provider error details unavailable." });
            }
            return response;
        }
    }

    public static async Task RunAsync()
    {
        var passed = 0;
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException("FAIL Gemini: " + name);
            Console.WriteLine("PASS Gemini: " + name);
            passed++;
        }
        IssueDto Issue() => new(Guid.NewGuid(), 1, IssueType.ProductionLine, "가상 상담", "가상 습도 상황",
            IssueStatus.Occurred, "test", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        AiConsultationService Service(Handler handler, AiProviderOptions? options = null, Action<string>? log = null) =>
            new(new ManualAssistant(), new(new HttpClient(handler)), options ?? new()
            { Provider = AiProvider.Gemini, Model = "gemini-3.5-flash-lite", ApiKey = "test-only-gemini-key" }, log);
        bool Generated(AiConsultResponse reply) => reply.Answer.StartsWith("[AI 생성 안내]");
        var scopeHandler = new Handler();
        var scopeService = Service(scopeHandler);
        var scopeIssue = Issue();
        var firstRefusal = await scopeService.ConsultAsync("scope-worker", scopeIssue, "오늘 날씨 어때요?");
        Check("매뉴얼 범위 밖 첫 질문은 API 호출 없이 안내", scopeHandler.Bodies.Count == 0
            && firstRefusal.Answer.Contains("등록된 매뉴얼로 안내할 수 없는 내용") && firstRefusal.ManualSource.StartsWith("근거 없음"));
        await scopeService.ConsultAsync("scope-worker", scopeIssue, "습도가 높아요");
        var duringRefusal = await scopeService.ConsultAsync("scope-worker", scopeIssue, "오늘 점심 추천");
        Check("기존 상담 중 무관 질문에 이전 매뉴얼을 억지로 적용하지 않음", scopeHandler.Bodies.Count == 1
            && duringRefusal.ManualSource.StartsWith("근거 없음"));
        await scopeService.ConsultAsync("scope-worker", scopeIssue, "80%");
        Check("거절 뒤 짧은 후속 답변과 이전 대화 유지", scopeHandler.Bodies.Count == 2 && Count(scopeHandler.Bodies[^1]) == 3);
        using (var scopeRequest = JsonDocument.Parse(scopeHandler.Bodies[0]))
        {
            var policy = scopeRequest.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
            Check("같은 장치라도 매뉴얼 없는 세부 수리법은 근거 부족으로 반환하도록 지시", policy.Contains("주제나 장치명이 같다는 이유만으로")
                && policy.Contains("hasEvidence=false"));
        }
        var handler = new Handler();
        var temperatureHandler = new Handler();
        await Service(temperatureHandler).ConsultAsync("worker", Issue(), "온도가 이상해요");
        using (var temperatureRequest = JsonDocument.Parse(temperatureHandler.Bodies.Single()))
        {
            var materials = temperatureRequest.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
            Check("온도 일반 질문에도 고온·저온·수신 오류 대응 자료 포함", new[] { "temperature_high", "temperature_low", "temperature" }.All(materials.Contains));
        }
        var events = new List<string>();
        var service = Service(handler, log: events.Add);
        var issue = Issue();
        var first = await service.ConsultAsync("worker", issue, "습도가 높아요");
        Check("생성 답변·원본 출처·관리자 표시", Generated(first) && first.ManualSource.Contains("습도 높음") && first.NeedAdmin);
        Check("상세 답변 다섯 항목과 두 단계 이상 점검", new[] { "[현재 상황]", "[우선 조치]", "[점검 순서]", "[조치 후 확인]", "[다음 확인]" }
            .All(first.Answer.Contains) && first.Answer.Contains("2. "));
        Check("Google 공식 모델 주소 사용", handler.Url == "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent");
        Check("키는 x-goog-api-key 헤더에만 전달", handler.Key == "test-only-gemini-key" && handler.Authorization is null
            && !handler.Bodies[0].Contains("test-only-gemini-key") && handler.Url?.Contains("key=") == false);
        using (var json = JsonDocument.Parse(handler.Bodies[0]))
        {
            var root = json.RootElement;
            var format = root.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text");
            Check("REST 열거형 JSON 응답 형식과 제한된 출처 목록", format.GetProperty("mimeType").GetString() == "APPLICATION_JSON"
                && format.GetProperty("schema").GetProperty("properties").GetProperty("manualIds").GetProperty("items").GetProperty("enum").GetArrayLength() > 0);
            Check("정책과 사용자 자료 분리·조작 도구 없음", root.TryGetProperty("systemInstruction", out _)
                && !root.TryGetProperty("tools", out _) && root.GetProperty("contents").GetArrayLength() == 1);
        }
        using (var json = JsonDocument.Parse(handler.Bodies[0]))
        {
            var root = json.RootElement;
            Check("상세 응답 필수 여덟 항목과 완료 기준 전달", root.GetProperty("generationConfig").GetProperty("responseFormat")
                .GetProperty("text").GetProperty("schema").GetProperty("required").GetArrayLength() == 8
                && root.GetProperty("contents")[0].GetRawText().Contains("completionNote"));
        }
        foreach (var mutation in new[] { "missing", "empty", "wrongtype", "nullitem" })
        {
            var detail = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Detailed(ValidAnswer))!;
            if (mutation == "missing") detail.Remove("verification");
            else detail["checks"] = JsonSerializer.SerializeToElement(mutation switch
            {
                "empty" => (object)Array.Empty<string>(), "wrongtype" => "invalid", _ => new string?[] { null, "x" }
            });
            var invalid = await Service(new Handler { Body = WrapDetailed(JsonSerializer.Serialize(detail)) })
                .ConsultAsync("worker", Issue(), "습도가 높아요");
            Check("상세 필드 검증 " + mutation, !Generated(invalid));
        }
        await service.ConsultAsync("worker", issue, "80%이고 결로가 있어요");
        using (var json = JsonDocument.Parse(handler.Bodies[1]))
        {
            var contents = json.RootElement.GetProperty("contents");
            Check("후속 질문에 user/model 대화 순서 유지", contents.GetArrayLength() == 3
                && contents[0].GetProperty("role").GetString() == "user"
                && contents[1].GetProperty("role").GetString() == "model"
                && contents[2].GetProperty("role").GetString() == "user");
            Check("이전 질문과 답변을 후속 질문에 전달", contents[0].GetRawText().Contains(Escaped("습도가 높아요"))
                && contents[1].GetRawText().Contains(Escaped("결로를 확인")));
        }
        await service.ConsultAsync("another-worker", issue, "습도가 높아요");
        Check("사용자별 문맥 분리", Count(handler.Bodies[^1]) == 1);
        await service.ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("문제별 문맥 분리", Count(handler.Bodies[^1]) == 1);
        await service.ConsultAsync("worker", issue with { LineId = 2 }, "습도가 높아요");
        Check("라인별 문맥 분리", Count(handler.Bodies[^1]) == 1);
        await service.ConsultAsync("worker", issue, "새 상담");
        await service.ConsultAsync("worker", issue, "습도가 높아요");
        Check("새 상담으로 대화 초기화", Count(handler.Bodies[^1]) == 1);
        Check("로그에는 제공자 이벤트 코드만 기록", events.All(x => x == "gemini_success"));
        var beforeGas = handler.Bodies.Count;
        var gas = await service.ConsultAsync("worker", issue, "가스 냄새가 나요");
        Check("가스 위험은 Gemini 대기 없이 기존 안전 안내", handler.Bodies.Count == beforeGas && gas.Answer.Contains("119") && gas.NeedAdmin);
        var afterGas = await service.ConsultAsync("worker", issue, "습도도 높아요");
        Check("후속 질문의 이전 가스 위험 보고 주의 유지", afterGas.Answer.Contains("가스 경보·누출 의심 보고"));
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            var bad = new Handler { Status = status };
            var reply = await Service(bad).ConsultAsync("worker", Issue(), "습도가 높아요");
            Check($"HTTP {(int)status} 대체·자동 재시도 및 OpenAI 전환 없음", !Generated(reply) && reply.Answer.Contains("제습기") && bad.Bodies.Count == 1);
            if (status == HttpStatusCode.TooManyRequests) Check("무료 한도 오류 설명", reply.Answer.Contains("호출 한도 또는 사용량 제한"));
            if (status == HttpStatusCode.ServiceUnavailable) Check("제공자 일시 장애 설명", reply.Answer.Contains("일시적인 장애"));
        }
        foreach (var (name, body) in new[]
        {
            ("차단", "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}"),
            ("빈 후보", "{\"candidates\":[]}"),
            ("출력 잘림", Wrap(ValidAnswer, "MAX_TOKENS")),
            ("안전 거절", Wrap(ValidAnswer, "SAFETY")),
            ("잘못된 JSON", "bad-json"),
            ("위조 출처", Wrap("{\"answer\":\"INVALID\",\"manualIds\":[\"invented\"],\"needAdmin\":false,\"hasEvidence\":true}")),
            ("누락 필드", Wrap("{\"answer\":\"INVALID\",\"manualIds\":[\"humidity_high\"],\"hasEvidence\":true}")),
            ("키 노출 답변", Wrap("{\"answer\":\"test-only-gemini-key\",\"manualIds\":[\"humidity_high\"],\"needAdmin\":false,\"hasEvidence\":true}")),
            ("과도한 응답 크기", new string('x', 65537))
        })
        {
            var bad = new Handler { Body = body };
            var reply = await Service(bad).ConsultAsync("worker", Issue(), "습도가 높아요");
            Check(name + " 응답은 매뉴얼로 대체", !Generated(reply) && reply.Answer.Contains("제습기") && !reply.Answer.Contains("INVALID"));
        }
        var thought = new Handler { Body = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP",
            content = new { role = "model", parts = new object[] { new { thought = true, text = "INVALID" }, new { text = Detailed(ValidAnswer) } } } } } }) };
        Check("thought 파트는 표시 답변에서 제외", Generated(await Service(thought).ConsultAsync("worker", Issue(), "습도가 높아요")));
        var noEvidence = new Handler { Body = Wrap("{\"answer\":\"INVALID\",\"manualIds\":[],\"needAdmin\":false,\"hasEvidence\":false}") };
        var noEvidenceReply = await Service(noEvidence).ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("근거 없는 답변은 관리자 확인", noEvidenceReply.NeedAdmin && noEvidenceReply.ManualSource.Contains("근거 없음")
            && noEvidenceReply.Answer.Contains("등록된 매뉴얼로 안내할 수 없는 내용") && !noEvidenceReply.Answer.Contains("INVALID"));
        var noKey = new Handler();
        Check("Gemini 키 없으면 호출하지 않음", !Generated(await Service(noKey, new() { Provider = AiProvider.Gemini }).ConsultAsync("worker", Issue(), "습도가 높아요")) && noKey.Bodies.Count == 0);
        var slow = new Handler { Slow = true };
        var timed = await Service(slow, new() { Provider = AiProvider.Gemini, ApiKey = "test-only", RequestTimeout = TimeSpan.FromMilliseconds(50) })
            .ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("시간 초과 시 대체", timed.Answer.Contains("지연"));
        var retryHandler = new Handler { Status = HttpStatusCode.ServiceUnavailable };
        var retryService = Service(retryHandler);
        var retryIssue = Issue();
        await retryService.ConsultAsync("worker", retryIssue, "습도가 높아요");
        retryHandler.Status = HttpStatusCode.OK;
        await retryService.ConsultAsync("worker", retryIssue, "80%");
        using (var json = JsonDocument.Parse(retryHandler.Bodies[^1]))
        {
            var turns = json.RootElement.GetProperty("contents");
            var previous = turns[1].GetProperty("parts")[0].GetProperty("text").GetString()!;
            Check("장애 뒤 후속 요청에 긴 매뉴얼을 중복 전송하지 않음", previous.Length < 500 && !previous.Contains("[확인 사항]"));
            Check("장애 뒤에도 직전 확인 질문과 사용자 답변 유지", previous.Contains("직전 확인 질문:")
                && turns[2].GetProperty("parts")[0].GetProperty("text").GetString() == "80%");
        }
        var truncatedEvents = new List<string>();
        var truncated = await Service(new Handler { Body = Wrap(ValidAnswer, "MAX_TOKENS") }, log: truncatedEvents.Add)
            .ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("출력 잘림은 원인별 안내·진단 코드", truncated.Answer.Contains("끝까지 생성되지 않아")
            && truncatedEvents.Single() == "gemini_fallback_output_limit");
        var blocked = await Service(new Handler { Body = Wrap(ValidAnswer, "SAFETY") }).ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("제공자 차단 안내를 연결 오류와 구분", blocked.Answer.Contains("답변 생성을 제한"));
        Console.WriteLine($"Passed {passed} Gemini checks (fake HTTP only; no external requests).");
    }
    private const string ValidAnswer = "{\"answer\":\"결로를 확인해 주세요. 현재 습도는 얼마인가요?\",\"manualIds\":[\"humidity_high\"],\"needAdmin\":false,\"hasEvidence\":true}";
    private static string Escaped(string value) => JsonSerializer.Serialize(value)[1..^1];
    private static int Count(string body)
    {
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("contents").GetArrayLength();
    }
    private static string Detailed(string legacy)
    {
        using var json = JsonDocument.Parse(legacy);
        var values = new Dictionary<string, object?>
        {
            ["situation"] = json.RootElement.GetProperty("answer").GetString(),
            ["immediateActions"] = new[] { "승인된 절차에 따라 제습기를 가동해 주세요." },
            ["checks"] = new[] { "결로가 보이는 위치를 확인해 담당자에게 전달해 주세요.", "제습 후 습도 추세를 확인해 주세요." },
            ["verification"] = "관리자가 정한 정상 범위와 결로 여부를 확인하고 완료 검토를 요청해 주세요.",
            ["nextQuestion"] = "제습 후 습도가 내려가고 있나요?"
        };
        foreach (var name in new[] { "manualIds", "needAdmin", "hasEvidence" })
            if (json.RootElement.TryGetProperty(name, out var value)) values[name] = value.Clone();
        return JsonSerializer.Serialize(values);
    }
    private static string Wrap(string text, string finish = "STOP") => WrapDetailed(Detailed(text), finish);
    private static string WrapDetailed(string text, string finish = "STOP") => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { finishReason = finish, content = new { role = "model", parts = new[] { new { text } } } } }
    });
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public string? Url, Key, Authorization;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = Wrap(ValidAnswer);
        public bool Slow;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Url = request.RequestUri?.ToString();
            Key = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            if (Slow) await Task.Delay(Timeout.Infinite, token);
            return new(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
