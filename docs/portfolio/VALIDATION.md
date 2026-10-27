# 업로드 준비본 검증 기록

확인일: 2026-10-27. 대상은 원본의 소스를 바이트 단위로 복사한 별도 준비본이다. `.NET SDK 9.0.315`에서 기존 `net8.0`/`net8.0-windows` 타깃을 빌드했다. NuGet은 PC에 이미 설치된 패키지를 이용해 오프라인 복원했다.

| 항목 | 실행·범위 | 결과 |
|---|---|---|
| 솔루션 빌드 | `dotnet build ProductionLineSystem.sln -c Release --no-restore` | 오류 0, 경고 0 |
| AI 검증 | `dotnet run --project 05_AiAssistant/ProductionLine.AiAssistant.SmokeTests -c Release` | 매뉴얼 100 + API 38 + Gemini 50 + 상태 8 = 196개 통과 |
| 원격/타이밍 검증 | `dotnet run --project tools/RemoteVerification -c Release -- --timing` | PASS 66개 |
| 독립 API 확인 | `python tools/PortfolioVerification/check_api.py` · 새 SQLite·임의 loopback 포트 | 모두 통과 |
| 전체 원격 UI 검증 | 기존 `tools/RemoteVerification`에 서버 DLL·별도 출력 폴더 전달 | 중단 · 아래 참조 |
| 소스 보존 | C#/Python/Arduino/C++ 및 프로젝트 파일 원본·준비본 SHA-256 비교 | 변경 없음 |

## 격리 API 확인 항목

- 익명 요청 거부, 관리자·작업자 라인 권한 분리, 작업자의 사용자 관리 접근 거부
- 단체 채팅 전송·관리자 수신·동일 전송 ID 재시도·내용 충돌 거부·증분 조회·조회 수 제한
- 문제 등록, 순서대로 상태 변경, 조치 중 작업자 채팅, 관리자 답변, 완료 요청 후 작업자 전송 제한
- 관리자 전용 종결, 반려 후 재요청, 서버 재시작 후 SQLite 상담·JSON 단체 채팅 보존
- API 키 없이 매뉴얼 상담, 일반 상담 시 문제 DB 미생성, 설정 상태 조회, 장비 미연결 진단

확인 도구를 `tools/PortfolioVerification/check_api.py`에 추가했다. Release 빌드 후 Python 표준 라이브러리만으로 실행할 수 있으며 임시 폴더에 별도 DB와 로그를 만든다. 실제 운영 DB·개인 API 키·물리 장비를 사용하지 않는다. 원본 제품 소스는 수정하지 않았다.

## 기존 UI 통합 검증의 제한

`tools/RemoteVerification/Program.cs:126`은 `_conveyorPort`를 `System.Windows.Forms.ComboBox`로 변환한다. 현재 화면은 `ProductionLine.UserApp.DarkComboBox`를 사용하므로 `InvalidCastException`이 발생해 뒤의 화면 검증으로 진행하지 못했다. 이는 기존 테스트의 화면 타입 가정 불일치이며, 이 결과만으로 실제 운영 기능 장애라고 단정하지 않는다. 검사 코드를 임의 수정하지 않고 승인 후 정리 항목으로 남겼다.

외부 AI 실호출, 카메라 실입력, 실제 USB/COM·모터 구동, 현장 검출 정확도와 장시간 부하 검증은 이번 작업에서 수행하지 않았다. 테스트 통과 수를 현장 안전성·모델 정확도 수치로 사용하지 않는다.
