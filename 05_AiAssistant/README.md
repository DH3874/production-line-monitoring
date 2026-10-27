# 5번 - 상담 AI와 매뉴얼 (공도현)

- `Manuals.json`: 점검 매뉴얼 (DLL에 포함됨)
- `ManualAssistant`: 매뉴얼에서 근거를 찾아 기본 답변을 만듦
- `AiConsultationService`: 외부 AI(Gemini 기본, OpenAI 가능)에 근거와 함께 물어보고, 키가 없거나 실패·한도 초과면 매뉴얼 답변으로 대신함
- 근거가 없으면 `NeedAdmin = true` (관리자 상담 권장)
- `ProductionLine.AiAssistant.SmokeTests`: 인터넷 없이 도는 매뉴얼 답변 시험 (192개)

API 키는 서버 PC에만 둡니다: `scripts/set-gemini-key.cmd`로 저장하거나 환경변수 `GEMINI_API_KEY`.
AI는 검사 판정, 설비 제어, 문제 종결을 하지 않습니다. 상담 내용은 저장하지 않습니다.