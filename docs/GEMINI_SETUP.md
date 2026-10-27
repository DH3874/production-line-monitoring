# 생성형 AI 연결 설정

기본 제공자는 `Gemini`입니다. 키가 없으면 기본 매뉴얼 상담을 제공합니다. API 키는 서버를 실행하는 환경에서만 설정하고 사용자 앱이나 소스 파일에 넣지 않습니다.

## Gemini

서버를 실행할 PowerShell에서 설정합니다.

```powershell
$env:AI_PROVIDER = 'Gemini'
$env:GEMINI_API_KEY = '<발급받은 키>'
$env:GEMINI_MODEL = 'gemini-3.5-flash-lite'
.\scripts\run-server.ps1
```

모델 기본값은 전달받은 팀원 코드의 설정을 유지했습니다. 계정에서 사용할 수 있는 모델 이름이 다르면 `GEMINI_MODEL`로 지정합니다. 키·모델·한도·통신 오류가 있으면 기본 매뉴얼 답변과 함께 원인에 맞는 안내가 표시됩니다.

## OpenAI

```powershell
$env:AI_PROVIDER = 'OpenAi'
$env:OPENAI_API_KEY = '<발급받은 키>'
$env:OPENAI_MODEL = 'gpt-5.4-mini'
.\scripts\run-server.ps1
```

팀원 코드의 Responses API 구조화 응답을 사용하며 답변의 출처 ID를 실제 매뉴얼과 검증합니다. API 형식은 [OpenAI 공식 구조화 응답 문서](https://developers.openai.com/api/docs/guides/structured-outputs)를 참고합니다.

## 공통 설정

- `Ai__Enabled=false`: 생성형 연결을 끄고 기본 매뉴얼만 사용
- `Ai__TimeoutSeconds`: 기본 7초, 1~8초 범위. 사용자 앱의 10초 제한 전에 대체 답변을 반환
- `Ai__RequestsPerMinute`: 기본 20회
- `Ai__RequestsPerDay`: 기본 100회
- `Ai__MaxOutputTokens`: 기본 2048, 100~2048 범위

설정 후 서버를 재시작해야 합니다. `[AI 생성 안내]`는 생성 답변 수신을 뜻합니다. 관리자 확인 권장은 답변 생성 성공 여부와 별개로 표시합니다. 호출 횟수 제한은 메모리 기준이며 서버 재시작 시 초기화됩니다.
