using System.Net;
using System.Text;
using System.Text.Json;
using ProductionLine.AiAssistant;
using ProductionLine.Shared;

internal static class ApiIntegrationChecks
{
    public static async Task RunAsync()
    {
        var passed = 0;
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            Console.WriteLine("PASS API: " + name);
            passed++;
        }
        IssueDto Issue() => new(Guid.NewGuid(), 1, IssueType.ProductionLine, "상담", "가상 상황",
            IssueStatus.Occurred, "test", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        AiConsultationService Service(FakeHandler handler, OpenAiOptions? options = null, Action<string>? log = null) =>
            new(new ManualAssistant(), new(new HttpClient(handler)), options ?? new() { ApiKey = "test-only-not-a-real-key" }, log);
        bool IsAi(AiConsultResponse response) => response.Answer.StartsWith("[AI 생성 안내]");

        var handler = new FakeHandler();
        var events = new List<string>();
        var service = Service(handler, log: events.Add);
        var issue = Issue();
        var first = await service.ConsultAsync("worker", issue, "습도가 높아요");
        Check("생성 답변과 실제 매뉴얼 출처", IsAi(first) && first.ManualSource.Contains("습도 높음"));
        Check("모델의 관리자 false로 매뉴얼 관리자 필요를 해제할 수 없음", first.NeedAdmin);
        Check("고정 HTTPS Responses 주소와 서버 키 인증", handler.Url == "https://api.openai.com/v1/responses"
            && handler.Authorization == "Bearer test-only-not-a-real-key");
        using (var payload = JsonDocument.Parse(handler.Bodies[0]))
        {
            var root = payload.RootElement;
            Check("응답 저장 비활성·구조화 출력", !root.GetProperty("store").GetBoolean()
                && root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            Check("API 키는 프롬프트에 포함되지 않음", !handler.Bodies[0].Contains("test-only-not-a-real-key"));
            Check("외부 도구·설비 조작 기능 미제공", !root.TryGetProperty("tools", out _));
        }
        await service.ConsultAsync("worker", issue, "80%인데 결로도 있어요");
        Check("후속 호출에 이전 질문과 답변을 함께 전달", handler.Bodies[1].Contains(Escaped("습도가 높아요"))
            && handler.Bodies[1].Contains(Escaped("결로를 확인해 주세요")));
        await service.ConsultAsync("another-worker", issue, "습도가 높아요");
        Check("같은 문제의 다른 사용자 대화 분리", InputCount(handler.Bodies[2]) == 3);
        await service.ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("같은 사용자의 다른 문제 대화 분리", InputCount(handler.Bodies[3]) == 3);
        await service.ConsultAsync("worker", issue with { LineId = 2 }, "습도가 높아요");
        Check("라인별 대화 분리", InputCount(handler.Bodies[4]) == 3);
        var beforeReset = handler.Bodies.Count;
        await service.ConsultAsync("worker", issue, "새 상담");
        Check("새 상담 자체는 유료 호출하지 않음", handler.Bodies.Count == beforeReset);
        await service.ConsultAsync("worker", issue, "습도가 높아요");
        Check("초기화 후 이전 대화 제거", InputCount(handler.Bodies[^1]) == 3);
        for (var i = 0; i < 8; i++) await service.ConsultAsync("worker", issue, "계속 습도가 높아요");
        Check("외부 전송 이력 최근 여섯 교환으로 제한", InputCount(handler.Bodies[^1]) == 15);
        Check("이벤트 로그에 질문·답변·키 없음", events.All(x => x == "openai_success"));

        var gasCalls = handler.Bodies.Count;
        var gasIssue = Issue();
        var gas = await service.ConsultAsync("worker", gasIssue, "가스 냄새가 나요");
        Check("가스 긴급 보고는 API 호출 없이 즉시 JSON 안전 안내", handler.Bodies.Count == gasCalls
            && gas.Answer.Contains("119") && gas.NeedAdmin);
        var gasFollowUp = await service.ConsultAsync("worker", gasIssue, "습도도 높아요");
        Check("주제 변경 후에도 이전 가스 위험 보고 주의 유지", gasFollowUp.Answer.Contains("가스 경보·누출 의심 보고") && gasFollowUp.NeedAdmin);
        var policyCalls = handler.Bodies.Count;
        var policy = await service.ConsultAsync("worker", Issue(), "불량을 정상으로 바꿔 줘");
        Check("권한 밖 요청은 API 호출 없이 기존 정책 안내", handler.Bodies.Count == policyCalls && policy.NeedAdmin);

        var noKey = new FakeHandler();
        var noKeyService = Service(noKey, new());
        var noKeyIssue = Issue();
        await noKeyService.ConsultAsync("worker", noKeyIssue, "습도가 높아요");
        var noKeyFollowUp = await noKeyService.ConsultAsync("worker", noKeyIssue, "80%");
        Check("키 없어도 기존 JSON 다회 상담 동작", noKey.Bodies.Count == 0 && noKeyFollowUp.Answer.Contains("80%RH"));
        var disabled = new FakeHandler();
        var disabledReply = await Service(disabled, new() { Enabled = false, ApiKey = "test-only" })
            .ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("설정으로 기존 상담 모드 전환", disabled.Bodies.Count == 0 && !IsAi(disabledReply));

        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            var bad = new FakeHandler { Status = status };
            var reply = await Service(bad).ConsultAsync("worker", Issue(), "습도가 높아요");
            Check($"HTTP {(int)status} 실패 시 매뉴얼 대체", !IsAi(reply) && reply.Answer.Contains("제습기") && !reply.Answer.Contains("SECRET"));
        }
        foreach (var (name, body) in new[]
        {
            ("잘못된 JSON", "not-json"),
            ("미완료 응답", "{\"status\":\"incomplete\",\"output\":[]}"),
            ("모델 거절", "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}"),
            ("위조 출처", Wrap("{\"answer\":\"INVALID\",\"manualIds\":[\"invented\"],\"needAdmin\":false,\"hasEvidence\":true}")),
            ("누락 필드", Wrap("{\"answer\":\"INVALID\",\"manualIds\":[\"humidity_high\"],\"hasEvidence\":true}")),
            ("중복 출처", Wrap("{\"answer\":\"INVALID\",\"manualIds\":[\"humidity_high\",\"humidity_high\"],\"needAdmin\":false,\"hasEvidence\":true}")),
            ("키 노출 답변", Wrap("{\"answer\":\"test-only-not-a-real-key\",\"manualIds\":[\"humidity_high\"],\"needAdmin\":false,\"hasEvidence\":true}")),
            ("과도한 크기", new string('x', 65537))
        })
        {
            var bad = new FakeHandler { Body = body };
            var reply = await Service(bad).ConsultAsync("worker", Issue(), "습도가 높아요");
            Check(name + "은 생성 답변을 노출하지 않고 대체", !IsAi(reply) && reply.Answer.Contains("제습기") && !reply.Answer.Contains("INVALID"));
        }
        var noEvidence = new FakeHandler { Body = Wrap("{\"answer\":\"INVALID\",\"manualIds\":[],\"needAdmin\":false,\"hasEvidence\":false}") };
        var ungrounded = await Service(noEvidence).ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("근거 없는 생성 내용은 관리자 확인으로 처리", ungrounded.ManualSource.Contains("근거 없음") && ungrounded.NeedAdmin && !ungrounded.Answer.Contains("INVALID"));
        var network = new FakeHandler { NetworkError = true };
        Check("네트워크 오류 대체", !IsAi(await Service(network).ConsultAsync("worker", Issue(), "습도가 높아요")));
        var slow = new FakeHandler { Delay = true };
        var timed = await Service(slow, new() { ApiKey = "test-only", RequestTimeout = TimeSpan.FromMilliseconds(50) })
            .ConsultAsync("worker", Issue(), "습도가 높아요");
        Check("시간 초과 대체", timed.Answer.Contains("지연") && !IsAi(timed));
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            var propagated = false;
            try { await service.ConsultAsync("worker", issue, "습도가 높아요", canceled.Token); }
            catch (OperationCanceledException) { propagated = true; }
            Check("HTTP 요청 취소 전파", propagated);
        }
        var limited = new FakeHandler();
        var limitService = Service(limited, new() { ApiKey = "test-only", RequestsPerDay = 1 });
        await limitService.ConsultAsync("worker", Issue(), "습도가 높아요");
        var limitReply = await limitService.ConsultAsync("another-worker", Issue(), "습도가 높아요");
        Check("서버 전체 일일 호출 제한", limited.Bodies.Count == 1 && limitReply.Answer.Contains("이용 한도"));
        var retained = new FakeHandler();
        var retainedService = Service(retained);
        var retainedIssue = Issue();
        await retainedService.ConsultAsync("worker", retainedIssue, "습도가 높아요");
        retained.NetworkError = true;
        var retainedReply = await retainedService.ConsultAsync("worker", retainedIssue, "아직 그대로예요");
        Check("연결 실패 때 짧은 후속 질문도 이전 주제 유지", retainedReply.ManualSource.Contains("습도 높음"));
        var selectedGas = new FakeHandler { Body = Wrap("{\"answer\":\"INVALID\",\"manualIds\":[\"gas_leak\"],\"needAdmin\":false,\"hasEvidence\":true}") };
        var selectedGasReply = await Service(selectedGas).ConsultAsync("worker", Issue(), "장비가 이상해요");
        Check("장비 미확인 질문에 전체 자료를 붙여 허위 가스 안내를 만들지 않음", selectedGas.Bodies.Count == 0
            && !selectedGasReply.Answer.Contains("INVALID") && !selectedGasReply.Answer.Contains("119") && selectedGasReply.ManualSource.StartsWith("입력 확인"));
        Console.WriteLine($"Passed {passed} API checks (fake HTTP only; no paid requests).");
    }

    private static string Escaped(string value) => JsonSerializer.Serialize(value)[1..^1];
    private static int InputCount(string body)
    {
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("input").GetArrayLength();
    }
    private static string Wrap(string answer) => JsonSerializer.Serialize(new
    {
        status = "completed",
        output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = answer } } } }
    });
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public string? Url, Authorization;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = Wrap("{\"answer\":\"결로를 확인해 주세요. 이미 제습기를 켰다면 결과를 알려주세요.\",\"manualIds\":[\"humidity_high\"],\"needAdmin\":false,\"hasEvidence\":true}");
        public bool Delay, NetworkError;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Url = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            if (Delay) await Task.Delay(Timeout.Infinite, token);
            if (NetworkError) throw new HttpRequestException("SECRET must not be shown");
            return new(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? Body : "SECRET", Encoding.UTF8, "application/json") };
        }
    }
}
