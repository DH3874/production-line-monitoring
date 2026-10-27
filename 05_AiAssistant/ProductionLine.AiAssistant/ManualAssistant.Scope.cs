using System.Text.RegularExpressions;
using ProductionLine.Shared;

namespace ProductionLine.AiAssistant;

public sealed partial class ManualAssistant
{
    internal static AiConsultResponse NoManualEvidence() => new(
        "등록된 매뉴얼로 안내할 수 없는 내용입니다. 매뉴얼에 없는 내용을 추측해서 답변하지 않습니다. 공정 문제라면 장치명·증상·이미 수행한 조치를 관리자에게 전달해 주세요.",
        "근거 없음 · 매뉴얼 범위 밖", true);

    // 문맥은 짧은 확인 답변이나 공정 점검 후속 질문에만 사용한다.
    // 관련 없는 질문 때문에 기존 상담을 초기화하거나 질문에 무관한 자료를 붙이지 않는다.
    private static bool CanUseManualContext(string question)
    {
        var text = Normalize(question).TrimEnd('.', '!', '?', '。', '？');
        if (Regex.IsMatch(text, @"^(?:지금|현재|아직|약|대략)?[+-]?\d+(?:\.\d+)?(?:%rh|%|°c|℃|도)?(?:정도)?(?:입니다|이에요|예요|요)?$")) return true;
        if (Regex.IsMatch(text, @"^[a-z]{1,5}-?\d{1,5}$")) return true;
        if (Regex.IsMatch(text, @"^(?:네[,，]?|아니요[,，]?|아직|현재는|지금은)?(?:있어요|없어요|보여요|안보여요|켰어요|꺼졌어요|켜져있어요)$")) return true;
        if (Regex.IsMatch(text, @"^(?:네|예|응|아니요|아니오|아뇨|아니|맞아요|있어요|없어요|보여요|안보여요|몰라요|모르겠어요|모름|미정|다음|다음은요|그다음은요|그대로예요)$")) return true;
        return HasAny(text, "점검", "확인", "조치", "청소", "닦았", "제습", "냉각", "가열", "결로", "오염", "렌즈", "읽기", "수신",
            "오류", "에러", "코드", "전원", "케이블", "화면", "값이", "수치", "경보", "냄새", "높아요", "낮아요", "높음", "낮음",
            "응답", "점등", "분류", "복구", "해결", "여전히", "그대로", "계속안", "해봤", "해도안", "개선", "구간", "전체", "오판정", "미검출", "모르겠", "값없", "센서없")
            || Regex.IsMatch(text, @"^(?:어떤순서|어떻게|무엇부터|뭐부터|다음|그다음|왜|무슨뜻|설명해).{0,10}(?:하나요|하죠|해야하나요|해야해요|해요|인가요|죠|줘|주세요)?$");
    }
}
