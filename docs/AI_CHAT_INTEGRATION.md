# 상담 AI 연결 구조

사용자 앱은 기존 `AiConsultRequest(IssueId, Question)`을 `/api/ai/consult`로 보냅니다. 서버는 로그인과 해당 라인 접근 권한을 검사한 후 `AiConsultationService.ConsultAsync(session.UserId, issue, question)`을 호출합니다.

응답 계약은 `Answer`, `ManualSource`, `NeedAdmin`을 유지합니다. 생성형 모드는 매뉴얼 출처와 최근 대화를 참고하며, API 실패·시간 초과·출처 검증 실패 시 기본 매뉴얼 답변을 반환합니다. 사용자 화면의 상태 문구가 생성 답변·기본 안내·근거 부족을 구분합니다.

대화 문맥은 사용자·라인·문제별로 분리합니다. 서버 재시작 또는 장시간 미사용 시 초기화됩니다. API 키는 서버 설정에서만 읽으며 프롬프트·화면·로그에 출력하지 않습니다. 실제 외부 연결에는 키와 모델 접근 권한이 필요합니다.

`NeedAdmin`은 관리자 문의 권장 표시이며 자동 메시지 전송이 아닙니다. 사용자가 관리자 상담 탭에서 문의하면 기존 문제 등록 API를 통해 관리자가 조회할 수 있는 문의 항목이 생성됩니다. 관리자 답장을 주고받는 별도 실시간 채팅 API는 이번 압축파일에 포함되어 있지 않습니다.

실행은 [ADMIN_AI_INTEGRATION.md](ADMIN_AI_INTEGRATION.md), 키 설정은 [GEMINI_SETUP.md](GEMINI_SETUP.md)를 참고하세요.
