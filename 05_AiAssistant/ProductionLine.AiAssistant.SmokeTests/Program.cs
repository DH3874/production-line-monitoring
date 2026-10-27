using ProductionLine.AiAssistant;
using ProductionLine.Shared;

if (args.Contains("--live-gemini"))
{
    var reportIndex = Array.IndexOf(args, "--report");
    await GeminiIntegrationChecks.RunLiveAsync(reportIndex >= 0 && reportIndex + 1 < args.Length ? args[reportIndex + 1] : null);
    return;
}

var assistant = new ManualAssistant();
var passed = 0;
IssueDto Issue(string title = "상담", string description = "", string? action = null) =>
    new(Guid.NewGuid(), 1, IssueType.ProductionLine, title, description,
        IssueStatus.Occurred, "test", action, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
void Check(string name, bool condition)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

var gas = assistant.Consult(Issue(), "MQ-9 값이 500인데 정상인가요?");
Check("MQ-9 출처 선택", gas.ManualSource.Contains("Winsen"));
Check("ADC를 ppm으로 간주하지 않음", gas.Answer.Contains("ADC 원시값은 ppm 농도가 아닙니다"));
Check("미확정 기준은 관리자 확인", gas.NeedAdmin && gas.Answer.Contains("아직 등록되지"));
Check("센서 연결을 요구하지 않고 사용자 질문을 안내에 반영", gas.Answer.Contains("사용자 질문: MQ-9 값이 500인데 정상인가요?") && !gas.Answer.Contains("실시간 센서값은 현재 상담 API에 연결되어 있지 않습니다"));
Check("별칭 정규화", assistant.Consult(Issue(), "mq 9 측정값 문의").ManualSource.Contains("MQ-9"));
var alarm = assistant.Consult(Issue("MQ-9 경보"), "센서 케이블을 빼볼까요?");
Check("가스 경보 우선 대응", alarm.NeedAdmin && alarm.Answer.Contains("비상 대응 절차"));
Check("가스 경보에 일반 센서 거리 조절 미안내", !alarm.Answer.Contains("감지 거리"));
var leak = assistant.Consult(Issue("설비 문제"), "가스 냄새가 나요");
Check("새 질문의 가스 의심도 우선", leak.Answer.Contains("안전 담당자"));
var mixed = assistant.Consult(Issue(), "MQ-9 경보와 DHT11 오류가 같이 떠요");
Check("복합 문제의 가스 경보 우선", mixed.ManualSource.Contains("MQ-9") && mixed.NeedAdmin);
Check("실제 가스 시험 대신 가상 시연", assistant.Consult(Issue(), "MQ9에 라이터로 시험해도 되나요?").Answer.Contains("가상 센서값"));
Check("영문 연기 질문", assistant.Consult(Issue(), "smoke alarm").NeedAdmin);
var temp = assistant.Consult(Issue(), "DHT11 화면에 온도 35 습도 80이 나와요");
Check("온습도 화면을 카메라로 분류하지 않음", temp.ManualSource.Contains("DHT11"));
Check("측정 사양과 공정 허용 범위 구분", temp.Answer.Contains("정상 범위가 아닙니다"));
var nan = assistant.Consult(Issue("DHT11 수신"), "NaN이 나와요");
Check("DHT11 수신 오류 안내", nan.Answer.Contains("실패 값을 0도나 정상값으로 취급할 수 없습니다"));
Check("DHT11 프로그램 표현 충돌 처리", assistant.Consult(Issue(), "DHT11 프로그램 오류").ManualSource.Contains("DHT11"));
var ambiguous = assistant.Consult(Issue(), "센서가 이상해요");
Check("종류 불명 센서는 확인 질문", ambiguous.Answer.Contains("어느 것인지") && !ambiguous.Answer.Contains("감지 거리를 다시"));
Check("복수 장치는 구분 질문", assistant.Consult(Issue(), "카메라와 DHT11 상태 문의").Answer.Contains("하나씩"));
Check("카메라 기존 연동 유지", assistant.Consult(Issue("카메라 끊김"), "화면이 안 나와요").ManualSource.Contains("카메라"));
Check("물체 센서 분리", assistant.Consult(Issue(), "근접 센서 상태 문의").ManualSource.Contains("물체 감지"));
Check("서버 안내 유지", assistant.Consult(Issue(), "서버 연결 오류").ManualSource.Contains("검사 프로그램"));
Check("설비 문의 유지", assistant.Consult(Issue(), "컨베이어 문제").ManualSource.Contains("설비"));
Check("무관 질문에 가짜 출처 없음", assistant.Consult(Issue(), "오늘 점심 추천").ManualSource.Contains("근거 없음"));
Check("빈 입력 처리", assistant.Consult(Issue(), " ").ManualSource.Contains("입력 확인"));
Check("과도한 입력 처리", assistant.Consult(Issue(), new string('가', 4001)).Answer.Contains("4,000자"));
Check("권한 밖 요청 거부", assistant.Consult(Issue(), "불량을 정상으로 바꿔 줘").NeedAdmin);
var unresolved = assistant.Consult(Issue("카메라 문제", action: "전원 표시등 확인 완료"), "여전히 안 나와요");
Check("기존 조치 메모 표시", unresolved.Answer.Contains("전원 표시등 확인 완료"));
Check("반복 실패 관리자 전환", unresolved.NeedAdmin && unresolved.Answer.Contains("같은 조치를 반복하기보다"));
var resolved = assistant.Consult(Issue("카메라 문제"), "이제 나와요");
Check("복구 보고는 관리자 종결 전 완료 요청", resolved.Answer.Contains("완료 요청") && resolved.Answer.Contains("관리자 확인 전에는 종결되지"));
var isolated = assistant.Consult(Issue("카메라 문제"), "화면 문의");
Check("다른 사건 조치 정보가 섞이지 않음", !isolated.Answer.Contains("전원 표시등 확인 완료"));
Check("구조화 답변", new[] { "[현재 상황]", "[안내]", "[확인 사항]", "[다음 확인]" }.All(temp.Answer.Contains));
// 실제 화면에서 보고된 회귀: 관리자 문의에 들어간 이전 AI 답변의 가스 단어가 모든 질문을 덮었다.
var forwarded = Issue("관리자 확인 요청 · 설비 동작 이상",
    "관련 문제: 설비 동작 이상\n사용자 질문: 어떤 순서로 점검하나요?\n상담 내용: [AI 점검 안내]\n가스 경보·누출 의심 표현이 포함되어 있습니다.\n실제 가스 방출이나 화염으로 시험하지 마세요.\n[근거]\nMQ-9 상담 가이드");
var screenshotQuestion = "촬영 조건·제품 위치·검사 기준 버전 확인";
var screenshotResult = assistant.Consult(forwarded, screenshotQuestion);
Check("보고된 촬영 질문이 검사 가이드로 연결", screenshotResult.ManualSource.Contains("촬영·제품 위치"));
Check("인용 가스 안내가 새 답변을 오염시키지 않음", !screenshotResult.Answer.Contains("비상 대응 절차") && !screenshotResult.Answer.Contains("별도 확인:"));
Check("검사 질문에 구체적인 확인 항목", screenshotResult.Answer.Contains("검사 영역") && screenshotResult.Answer.Contains("기준 버전"));
var forwardedTemp = assistant.Consult(forwarded, "DHT11 값이 NaN으로 나옵니다");
Check("같은 문의의 온습도 질문은 온습도 답변", forwardedTemp.ManualSource.Contains("DHT11") && !forwardedTemp.Answer.Contains("가스 경보"));
Check("같은 문의의 카메라 질문은 카메라 답변", assistant.Consult(forwarded, "카메라 영상이 안 나와요").ManualSource.Contains("카메라"));
Check("같은 문의의 서버 질문은 서버 답변", assistant.Consult(forwarded, "서버 연결 오류").ManualSource.Contains("검사 프로그램"));
Check("새 질문에 주제가 없으면 원래 설비 문의 사용", assistant.Consult(forwarded, "어떤 순서로 점검하나요?").ManualSource.Contains("설비"));
Check("새 가스 의심은 인용 제외 후에도 대응", assistant.Consult(forwarded, "지금 가스 냄새가 나요").Answer.Contains("비상 대응 절차"));
Check("무관 질문을 원래 설비 답변으로 채우지 않음", assistant.Consult(forwarded, "오늘 점심 추천").ManualSource.Contains("근거 없음"));
var gasNormal = Issue("MQ-9 상태 문의");
Check("값과 숫자만 있는 후속 질문은 선택한 센서 문맥 사용", assistant.Consult(gasNormal, "값 500은 정상인가요?").ManualSource.Contains("MQ-9"));
Check("가스 사건에서도 명시적 새 질문 우선", assistant.Consult(gasNormal, "카메라 영상 문의").ManualSource.Contains("카메라"));
Check("정상 가스 문맥의 온습도 경보를 가스 경보로 오인하지 않음", !assistant.Consult(gasNormal, "DHT11 온도 경보 문의").Answer.Contains("비상 대응 절차"));
Check("설정 질문은 실제 경보로 취급하지 않음", !assistant.Consult(Issue(), "MQ-9 경보 기준은 무엇인가요?").Answer.Contains("비상 대응 절차"));
Check("누출 부정문과 온습도 질문 분리", assistant.Consult(Issue(), "가스 누출은 없고 DHT11 값이 NaN입니다").ManualSource.Contains("DHT11"));
var actualGas = assistant.Consult(Issue("MQ-9 경보 발생"), "DHT11 온도를 확인하고 싶어요");
Check("실제 가스 기록은 다른 질문 답변의 별도 주의로 유지", actualGas.ManualSource.Contains("DHT11") && actualGas.Answer.Contains("별도 확인:") && actualGas.NeedAdmin);
Check("반복 불량 질문 분류", assistant.Consult(Issue(), "정상 제품인데 불량이 갑자기 많이 나와요").ManualSource.Contains("검사 기준"));
Check("이송 적체 질문 분류", assistant.Consult(Issue(), "제품이 밀려서 쌓여요").ManualSource.Contains("설비"));
Check("제품 미감지 질문 분류", assistant.Consult(Issue(), "제품이 감지되지 않아요").ManualSource.Contains("물체 감지"));
var scenarios = new[]
{
    (Question: "가스 누출", Source: "가스 누출", Detail: "119"),
    (Question: "온도 높음", Source: "온도 높음", Detail: "냉각장치"),
    (Question: "온도 낮음", Source: "온도 낮음", Detail: "동결·누수"),
    (Question: "습도 높음", Source: "습도 높음", Detail: "제습기"),
    (Question: "컨베이어 밸트 멈춤", Source: "컨베이어 벨트 멈춤", Detail: "잠금·표지"),
    (Question: "제품 인식 오류", Source: "제품 인식 오류", Detail: "정상·불량 예시 제품")
};
foreach (var scenario in scenarios)
{
    var result = assistant.Consult(forwarded, scenario.Question);
    Check("여섯 상황별 자료와 해결 절차: " + scenario.Question,
        result.ManualSource.Contains(scenario.Source) && result.Answer.Contains(scenario.Detail));
    Check("조치 후 확인과 관리자 검토: " + scenario.Question,
        result.Answer.Contains("[조치 후 확인]") && result.NeedAdmin);
}
foreach (var (question, source) in new[]
{
    ("온도가 너무 높아요", "온도 높음"),
    ("온도가 계속 떨어져요", "온도 낮음"),
    ("습도가 많이 올라갔어요", "습도 높음"),
    ("컨베이어 벨트가 안 돌아요", "컨베이어 벨트 멈춤"),
    ("제품을 인식 못해요", "제품 인식 오류"),
    ("렌즈 문제인지 제품 인식이 안돼요", "제품 인식 오류")
})
    Check("일상 표현과 첫 문의의 상세 안내: " + question,
        assistant.Consult(forwarded, question).ManualSource.Contains(source));
var high = assistant.Consult(Issue(), "온도가 높아요");
Check("고온 답변이 임의 가열을 권하지 않음", !high.Answer.Contains("보온·가열 장치"));
var low = assistant.Consult(Issue(), "온도가 낮아요");
Check("저온 답변이 냉각 조치를 권하지 않음", !low.Answer.Contains("냉각 방법"));
Check("처음 안된다고 한 문의의 점검 절차 유지", assistant.Consult(Issue(), "제품 인식이 안돼요").Answer.Contains("센서·렌즈 오염"));
Check("고온과 NaN 동시 보고는 수신 오류 우선", assistant.Consult(Issue(), "DHT11 온도가 높다고 나오는데 NaN입니다").Answer.Contains("실패 값을 0도나 정상값으로 취급할 수 없습니다"));
Check("고온·저온을 함께 묻는 경우 먼저 구분", assistant.Consult(Issue(), "온도 높음과 온도 낮음 문의").Answer.Contains("하나씩"));
Check("온도와 습도 문제를 동시에 단정하지 않음", assistant.Consult(Issue(), "온도 높음 습도 높음").Answer.Contains("하나씩"));
Check("고온 사건의 짧은 후속 문의도 고온 안내", assistant.Consult(Issue("온도 높음"), "어떻게 점검하나요?").ManualSource.Contains("온도 높음"));
var noSensorHumidity = assistant.Consult(forwarded, "습도가 너무 높아요");
Check("보고된 습도 질문은 센서 없이 제습·결로 절차 안내", noSensorHumidity.ManualSource.Contains("습도 높음") && noSensorHumidity.Answer.Contains("제습기") && noSensorHumidity.Answer.Contains("결로"));
Check("습도 질문이 센서 읽기 오류 안내로 바뀌지 않음", !noSensorHumidity.Answer.Contains("NaN") && !noSensorHumidity.Answer.Contains("센서 유형 설정"));
var chatIssue = Issue("설비 상태 문의");
var firstTurn = assistant.ConsultForUser("chat-worker", chatIssue, "습도가 너무 높아요");
Check("서버 상담의 첫 후속 질문은 습도 하나", firstTurn.Answer.Contains("현재 상대습도는 몇 %"));
var secondTurn = assistant.ConsultForUser("chat-worker", chatIssue, "80%");
Check("숫자 답변을 직전 습도 질문에 연결", secondTurn.Answer.Contains("80%RH") && secondTurn.Answer.Contains("결로가 보이나요"));
var thirdTurn = assistant.ConsultForUser("chat-worker", chatIssue, "있어요");
Check("결로 답변은 해당 분기 안내", thirdTurn.Answer.Contains("결로가 보인다는") && thirdTurn.Answer.Contains("젖은 전기 장치"));
Check("이전 습도값도 다음 턴에 유지", thirdTurn.Answer.Contains("80%RH"));
var fourthTurn = assistant.ConsultForUser("chat-worker", chatIssue, "제습기를 켰지만 아직 개선되지 않았어요");
Check("수행 조치와 앞선 답변을 함께 요약", fourthTurn.Answer.Contains("제습기를 켰지만") && fourthTurn.Answer.Contains("80%RH") && fourthTurn.NeedAdmin);
var otherUser = assistant.ConsultForUser("another-worker", chatIssue, "80%");
Check("다른 사용자의 대화에 습도 문맥 노출 없음", !otherUser.Answer.Contains("80%RH") && !otherUser.Answer.Contains("제습기를 켰지만"));
var anotherIssue = Issue("다른 문제");
Check("다른 문제에 숫자 답변 문맥이 섞이지 않음", !assistant.ConsultForUser("chat-worker", anotherIssue, "80%").Answer.Contains("80%RH"));
assistant.ConsultForUser("reset-worker", chatIssue, "습도가 높아요");
var invalidNumeric = assistant.ConsultForUser("reset-worker", chatIssue, "네");
Check("숫자 질문에 네라고 답하면 재확인", invalidNumeric.Answer.Contains("직전 질문:") && invalidNumeric.Answer.Contains("현재 상대습도"));
Check("유효하지 않은 답은 다음 단계로 넘어가지 않음", assistant.ConsultForUser("reset-worker", chatIssue, "70%").Answer.Contains("결로가 보이나요"));
var missingValueIssue = Issue("습도 문의");
assistant.ConsultForUser("missing-worker", missingValueIssue, "습도가 높아요");
var unknownValue = assistant.ConsultForUser("missing-worker", missingValueIssue, "센서가 없어서 모르겠어요");
Check("센서값이 없어도 미확인으로 남기고 다음 점검", unknownValue.Answer.Contains("확인되지 않음") && unknownValue.Answer.Contains("결로가 보이나요"));
var changedTopic = assistant.ConsultForUser("reset-worker", chatIssue, "온도가 낮아요");
Check("명시적인 새 주제는 새 점검 흐름", changedTopic.ManualSource.Contains("온도 낮음") && changedTopic.Answer.Contains("현재 온도는 몇 도"));
var lowValue = assistant.ConsultForUser("reset-worker", chatIssue, "5도");
Check("온도 답변 뒤 액체 사용 질문으로 진행", lowValue.Answer.Contains("5°C") && lowValue.Answer.Contains("액체나 냉각수가 사용되나요"));
var noLiquid = assistant.ConsultForUser("reset-worker", chatIssue, "아니요");
Check("저온의 아니요는 액체 미사용 분기", noLiquid.Answer.Contains("액체를 사용하지 않는다는"));
assistant.ConsultForUser("reset-worker", chatIssue, "새 상담");
Check("새 상담은 이전 질문 문맥 초기화", !assistant.ConsultForUser("reset-worker", chatIssue, "5도").Answer.Contains("5°C"));
var beltChatIssue = Issue("컨베이어 정지");
assistant.ConsultForUser("belt-worker", beltChatIssue, "컨베이어 벨트가 멈췄어요");
var beltReply = assistant.ConsultForUser("belt-worker", beltChatIssue, "일부 구간만요");
Check("벨트 정지 범위 선택 후 오류 코드 질문", beltReply.Answer.Contains("일부 구간 정지") && beltReply.Answer.Contains("오류 코드나 메시지"));
var productChatIssue = Issue("제품 인식 오류");
assistant.ConsultForUser("product-worker", productChatIssue, "제품 인식이 안돼요");
var productReply = assistant.ConsultForUser("product-worker", productChatIssue, "오판정이에요");
Check("제품 인식 후 오판정 분기로 연결", productReply.Answer.Contains("정상 제품이 불량으로 표시된다는"));
var gasChatIssue = Issue("상담 문의");
assistant.ConsultForUser("gas-worker", gasChatIssue, "가스 냄새가 나요");
var unsafePlace = assistant.ConsultForUser("gas-worker", gasChatIssue, "아니요");
Check("가스 상황은 안전한 장소 응답 전 다음 단계 진행 금지", unsafePlace.Answer.Contains("채팅을 멈추고") && unsafePlace.Answer.Contains("현재 안전한 장소에 있나요"));
var safePlace = assistant.ConsultForUser("gas-worker", gasChatIssue, "네");
Check("안전 장소 응답은 재진입 승인 없이 신고 정보 확인", safePlace.Answer.Contains("현장 재진입") && safePlace.Answer.Contains("누출 의심 위치와 경보 시각"));
foreach (var unsupported in new[] { "오늘 날씨 어때요?", "오늘 점심 추천", "주말 여행 추천해줘" })
{
    var refused = assistant.Consult(Issue("온도 높음"), unsupported);
    Check("선택한 설비로 무관 질문을 채우지 않음: " + unsupported, refused.ManualSource.StartsWith("근거 없음")
        && refused.Answer.Contains("등록된 매뉴얼로 안내할 수 없는 내용") && !refused.Answer.Contains("냉각"));
}
var scopeIssue = Issue("습도 문의");
assistant.ConsultForUser("scope-worker", scopeIssue, "습도가 높아요");
Check("후속 상담 중 무관 질문도 범위 밖 안내", assistant.ConsultForUser("scope-worker", scopeIssue, "오늘 점심 추천").ManualSource.StartsWith("근거 없음"));
Check("범위 밖 질문이 기존 습도 확인 단계를 바꾸지 않음", assistant.ConsultForUser("scope-worker", scopeIssue, "80%").Answer.Contains("80%RH"));
var naturalScopeIssue = Issue("습도 문의");
assistant.ConsultForUser("scope-worker", naturalScopeIssue, "습도가 높아요");
Check("부사 포함 짧은 숫자 답변 유지", assistant.ConsultForUser("scope-worker", naturalScopeIssue, "아직 80%예요").Answer.Contains("80%RH"));
Check("확인 답변의 예/아니요와 상태 표현 유지", assistant.ConsultForUser("scope-worker", naturalScopeIssue, "네, 있어요").Answer.Contains("결로가 보인다는"));
Console.WriteLine($"Passed {passed} checks.");
await ApiIntegrationChecks.RunAsync();
await GeminiIntegrationChecks.RunAsync();
AiStatusChecks.Run();
