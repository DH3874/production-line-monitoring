# 3번 - C# 서버, 통신, 데이터 (김하균)

ASP.NET Core 서버가 `0.0.0.0:5050`에서 HTTP REST/JSON 요청을 받습니다.

- 로그인과 역할·라인 권한 검사 (요청마다 DB에서 다시 확인)
- SQLite DB(`App_Data/productionline.db`): 사용자, 라인, 검사, 문제, 상태 변경 이력, 1:1 문의, 통신 기록
- 문제 처리 순서 검사 (발생 → 확인 → 조치 중 → 완료 요청 → 종결/반려)
- 통신 기록 7일 보관 후 CSV 백업
- 단체 채팅 (`data/group-chat.json`, 새 계정 자동 참여)
- AI 상담: 외부 AI(Gemini/OpenAI) → 안 되면 매뉴얼 답변. 상담 내용은 저장하지 않음

자세한 설명은 `md파일/클로드 버전_서버_추가기능_정리_2026-10-19.md`, DB는 `md파일/DB_구조_정리_2026-10-19.md`를 보세요.

다른 PC에서 접속할 때는 앱 PC에서 `$env:PRODUCTIONLINE_SERVER_URL='http://<서버PC-IP>:5050'`을 설정하고, 서버 PC 방화벽에서 TCP 5050 인바운드를 허용합니다.