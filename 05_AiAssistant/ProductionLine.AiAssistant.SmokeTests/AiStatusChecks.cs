using ProductionLine.Shared;
using ProductionLine.UserApp;

internal static class AiStatusChecks
{
    internal static void Run()
    {
        var tests = new (string Answer, string Source, bool Admin, AiReplyKind Expected)[]
        {
            ("[AI 생성 안내]\n답변", "매뉴얼", true, AiReplyKind.Success),
            ("[AI 응답이 지연되어 기본 매뉴얼 안내를 제공합니다]\n답변", "매뉴얼", true, AiReplyKind.Fallback),
            ("[AI 연결이 설정되지 않아 기본 매뉴얼 안내를 제공합니다]\n답변", "매뉴얼", true, AiReplyKind.Fallback),
            ("[기본 안전·권한 안내]\n답변", "매뉴얼", true, AiReplyKind.Safety),
            ("자료 부족", "근거 없음 · 관리자 확인", true, AiReplyKind.EvidenceMissing),
            ("질문 필요", "입력 확인", false, AiReplyKind.Input),
            ("초기화", "상담 흐름 · 새 상담", false, AiReplyKind.Reset),
            ("[현재 상황]\n답변", "매뉴얼", true, AiReplyKind.Manual)
        };
        foreach (var t in tests)
        {
            var status = AiReplyStatus.FromResponse(new(t.Answer, t.Source, t.Admin));
            if (status.Kind != t.Expected) throw new Exception("AI reply status mismatch: " + t.Expected);
        }
        Console.WriteLine($"Passed {tests.Length} AI status checks (admin review is independent from AI success).");
    }
}
