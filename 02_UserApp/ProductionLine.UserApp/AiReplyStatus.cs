using ProductionLine.Shared;

namespace ProductionLine.UserApp;

public enum AiReplyKind { Success, Fallback, Safety, EvidenceMissing, Input, Reset, Manual }

public sealed record AiReplyStatus(AiReplyKind Kind, string Label)
{
    public static AiReplyStatus FromResponse(AiConsultResponse response)
    {
        if (response.ManualSource.StartsWith("입력 확인")) return new(AiReplyKind.Input, "질문을 확인해 주세요");
        if (response.ManualSource.StartsWith("상담 흐름")) return new(AiReplyKind.Reset, "새 상담 시작");
        if (response.Answer.StartsWith("[기본 안전") || response.Answer.StartsWith("[기본 권한"))
            return new(AiReplyKind.Safety, "안전·권한 안내");
        if (response.ManualSource.StartsWith("근거 없음")) return new(AiReplyKind.EvidenceMissing, "매뉴얼 근거 부족 · 관리자 문의");
        if (response.Answer.StartsWith("[AI 생성 안내]")) return new(AiReplyKind.Success, "AI 응답 성공");
        if (response.Answer.StartsWith("[AI ") || response.Answer.StartsWith("[현재 AI "))
            return new(AiReplyKind.Fallback, "AI 응답 대신 기본 매뉴얼 안내");
        return new(AiReplyKind.Manual, "기본 매뉴얼 안내");
    }

    public static string DisplayAnswer(string answer)
    {
        var body = answer.TrimStart();
        // The original response is classified first; only duplicate presentation banners are removed.
        while (body.StartsWith("[AI 생성 안내]", StringComparison.Ordinal) ||
               body.StartsWith("[AI 연결", StringComparison.Ordinal) ||
               body.StartsWith("[현재 AI ", StringComparison.Ordinal) ||
               body.StartsWith("[AI 응답 상태:", StringComparison.Ordinal) ||
               body.StartsWith("[AI 점검 안내]", StringComparison.Ordinal))
        {
            var end = body.IndexOf(']');
            if (end < 0 || end > 240) break;
            body = body[(end + 1)..].TrimStart();
        }
        return body;
    }
}
