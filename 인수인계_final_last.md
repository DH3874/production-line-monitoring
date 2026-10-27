# final_last 인수인계 (2026-10-23, 김하균 노트북 → 컨베이어 PC)

> **이 문서를 받은 Claude에게:** 이 폴더가 **최종 소스**입니다. 앞으로 작업은 컨베이어 PC에서만 합니다.
> 김하균님은 코딩 초보라 쉬운 한국어로 짧게 설명하고, 고치기 전에 계획을 말하고, 지우기 전에 확인받고(영구 삭제 대신 휴지통), 설치·GitHub 업로드는 허락받고 하세요. 로그인·비밀번호·API 키는 김하균님이 직접 입력합니다.

## 0. 받기·실행

1. 받기: `git pull` 후 `final_last/` 폴더 사용, 또는 `final_last/ProductionLine_final_last_20261023.zip`을 받아 **짧은 경로**(예: `C:\PL\final_last`)에 풀기
   - 경로가 길면 서버가 `e_sqlite3` DLL 오류로 꺼집니다.
2. `ProductionLineSystem.sln`을 Visual Studio로 열고 빌드 (또는 `scripts\build.ps1`)
3. **서버는 한 대만**: 관리자 앱 실행 → **[서버 켜고 접속]** → 관리자 로그인
4. 작업자 앱: 서버 자동 찾기(UDP 5051) 또는 IP 직접 입력 → 로그인 → COM 포트·벨트 연결
5. 방화벽: 서버 PC에서 TCP 5050, UDP 5051 인바운드 허용
6. 시연용 기본 계정: `admin` / `admin123!`, `worker1` / `worker123!` (README와 같음)
7. DB 파일은 처음 서버를 켤 때 `03_Server/ProductionLine.Server/App_Data/productionline.db`로 새로 만들어집니다(마이그레이션 자동 적용).

## 1. ★ 꼭 할 일: Gemini(AI) 키 연결 — 관리자 PC(서버 켜는 PC)에서만

AI 상담을 실제 Gemini로 쓰려면 **관리자 앱으로 [서버 켜고 접속]을 누르는 PC(= 서버 PC)에서 키를 한 번 저장**해야 합니다. 노트북에 저장한 키는 옮겨지지 않습니다.
- 관리자 PC와 컨베이어(작업자) PC가 다른 컴퓨터라면, **관리자 PC에만** 저장합니다. 작업자 PC에는 저장하지 않습니다.
- 서버를 켤 때 쓰는 **같은 Windows 계정**으로 저장해야 합니다(키가 계정별로 저장됨).
- 나중에 서버를 다른 PC에서 켜게 되면, 그 PC에서 한 번 더 저장합니다.

1. 관리자 PC에서 `scripts\set-gemini-key.cmd` 실행 → 창이 뜨면 **김하균님이 직접 키 입력** (Claude는 키를 묻거나 파일·대화에 적지 않음)
   - 키는 `%LOCALAPPDATA%\ProductionLine\gemini-key.dpapi`에 **관리자 PC·그 Windows 계정 전용으로 암호화** 저장
2. 관리자 앱에서 서버를 다시 켜면 자동으로 키를 읽어 Gemini 연결 (코드는 이미 `Gemini:Enabled=true`, `UseSavedKey=true`)
3. 확인: 로그인 후 `GET /api/ai/chat/status` → `aiConfigured: true`, 그리고 작업자 앱 AI 상담에서 실제 질문 1~2개 → 서버 창/`ai-events.jsonl`에 `gemini_success`가 남는지
   - `aiConfigured=true`는 "설정됨"일 뿐, 실제 응답 성공은 질문해서 확인
4. 알아둘 것
   - 작업자 PC에는 키가 필요 없습니다.
   - 키 없음·인터넷 끊김·30초 초과·한도 초과 → 자동으로 **매뉴얼(`Manuals.json`) 답변**
   - 한도: 하루 100번, 분당 20번 (`appsettings.json`의 `Gemini:RequestsPerDay`, `RequestsPerMinute`)
   - 환경변수 `GEMINI_API_KEY`가 있으면 저장된 키보다 우선

## 2. 전체 구조

```
final_last/
├─ ProductionLineSystem.sln      솔루션 (아래 프로젝트 전부)
├─ README.md                     프로젝트 소개·실행
├─ 인수인계_final_last.md        이 문서
├─ 컨베이어_통합실행.cmd         작업자 앱(컨베이어) 실행
├─ 00_Shared/        공용 계약(DTO)·API 클라이언트·서버 찾기          (3번 관리, 전원 합의)
├─ 01_AdminApp/      관리자 WinForms                                 (1번 조영준)
├─ 02_UserApp/       작업자 WinForms (카메라·YOLO·컨베이어·AI 상담)   (2번 장서진)
├─ 03_Server/        ASP.NET Core 8 서버 + SQLite DB                 (3번 김하균)
├─ 04_Vision/        YOLO ONNX 검사 + models/ (v5)                   (4번 문성찬, 조장)
├─ 05_AiAssistant/   상담 AI (Gemini + 매뉴얼)                        (5번 공도현)
├─ 06_TrainingTool/  라벨링·학습 도구 (실행 앱과 별개)
├─ hardware/         Arduino Mega 펌웨어 V7 (ConveyorYoloControl)
├─ scripts/          빌드·실행·키 저장 스크립트
├─ tools/            arduino-cli, 시험 도구(RemoteVerification 등)
├─ docs/             기능별 문서
└─ md파일/           정리 문서·보고서 예시(report_from)·발표 자료
```

통신: 앱 ↔ 서버 HTTP REST/JSON (포트 5050), 서버 찾기 UDP 5051. 앱은 DB에 직접 접근하지 않고 서버 API만 씁니다.

## 3. 파트별 구조

### 00_Shared (공용)
- `Contracts.cs` 로그인·라인·검사·문제 DTO / `ProductionLineApiClient.cs` 서버 호출
- `HardwareContracts.cs` 센서·벨트 / `RemoteContracts.cs` 원격 명령 / `ChatContracts.cs`·`GroupChatApiClient.cs` 단체 채팅
- `ServerDiscovery.cs` 서버 자동 찾기 / **새로** `AiChatContracts.cs`, `DeviceDiagnostics.cs`

### 01_AdminApp (관리자)
- `Program.cs` 서버 연결 창 → `AdminLoginForm` → `AdminMainForm`(라인 현황, 메뉴, **보고서 저장·종료 시 자동 저장**)
- `LineStatusCard.cs`·`RotaryGateSimulation.cs` 라인 카드·회전판 시뮬레이션 / `LineDetailForm.cs` 상세
- `RemoteWorkerForm.cs` 작업자 원격 운영·센서 그래프 / `UserManagementForm.cs` 사용자 관리
- `InspectionIssueForm.cs` 검사·문제 조회 / `CompletionRequestForm.cs`+`IssueConversationPanel.cs` 종결·반려·1:1 채팅
- `InspectionHistoryForm.cs` 이력·통계 / `UiText.cs` 상태 글자

### 02_UserApp (작업자)
- `UserMainForm.cs` 메뉴 5개(실시간 검사, 검사 현황·이력, 문제·조치, AI 상담, 단체 채팅)
- `CameraInspection.cs` 카메라+YOLO 연속 검사, 검사 저장(기종·요청 번호) / `CameraSession.cs`
- `HardwareControls.cs`·`ConveyorSerialController.cs`·`HardwareSerialProtocol.cs` 컨베이어·센서 시리얼
- `ArduinoFirmwareTools.cs`·`ArduinoSetupForm.cs` 펌웨어 업로드(arduino-cli) / `RemoteWorker.cs` 관리자 원격 명령 수행
- `AiConsultationApi.cs` AI 상담(`/api/ai/consult`, 35초 대기)·AI 상태·장비 진단 조회 / `IssueChat.cs` 문제 채팅
- `AiChatStatusPanel.cs` AI 상담 화면 위쪽 상태 영역(연결·응답 결과·응답 시간, [연결 확인]·[장비 상태 확인]) / `DeviceDiagnosticsForm.cs` 장비 상태 확인 창(읽기 전용)

### 03_Server (서버·DB)
- `Program.cs` 시작점: 설정 → 부품 등록 → DB 준비 → 통신 기록 → API 창구
- `Infrastructure/`: `AppStore`(DB 저장·조회), `AuthService`(토큰), `LineHardwareStore`(센서 실시간+**DB 기록**), `RemoteLineStore`(원격 명령), `GroupChatStore`(단체 채팅 JSON), `RequestLogService`(통신 기록), **`LineReportService`(보고서 CSV)**, **`DeviceDiagnosticsService`·`GeneralAiChatEndpoints`·`DeviceDiagnosticsEndpoints`(AI 진단)**, `GeminiKeyStorage`(키 읽기), `ServerDiscoveryResponder`
- `Persistence/` DB 표, `Migrations/` 마이그레이션 7개
- 저장 위치: DB `App_Data/productionline.db`, 단체 채팅 `data/group-chat.json`, 검사 사진 `data/inspection-images/`

### 04_Vision (영상처리)
- `YoloOnnxInspector.cs` ONNX 실행·판정 (**`_crack`으로 끝나는 클래스 = 불량**), `CombinedYoloModels.cs`(옛 다중 모델용)
- `models/best.onnx` = **v5** (SHA256 `9fb6d967…`), `classes.txt`, `model_info.json`, `background/empty_belt_reference.jpg`

### 05_AiAssistant (상담 AI)
- `AiConsultationService*.cs` Gemini 호출·실패 시 매뉴얼 전환·장비 진단 반영
- `ManualAssistant*.cs` + `Manuals.json`(DLL에 포함) 매뉴얼 답변 / `GeminiResponsesClient.cs`
- 시험: `ProductionLine.AiAssistant.SmokeTests` (외부 호출 없음)

### hardware (펌웨어 V7)
- Mega 2560: PUL D5 / DIR D6, 조명 D7, DHT11 D30, **MQ-9 AO → A0**, 서보 D11, NC 긴급 정지 D12

## 4. 기존 final(2026-10-21 팀 통합본)에서 추가·바뀐 것

### 10-22에 한 것 (GitHub `final/ProductionLine_final_20261022.zip`에 이미 포함)
| 내용 | 위치 |
|---|---|
| 화면 배율 설정 (`DpiUnawareGdiScaled`) | 관리자·작업자 csproj |
| 센서·벨트 기록 DB 저장 (10초마다 + 상태 변경, 7일 보관) | `LineHardwareStore.cs`, 표 `LineHardwareLogs` |
| 안 쓰는 점수 칸(ShapeScore, ColorScore) 삭제 | `InspectionEntity.cs`, 마이그레이션 |
| 검사 기록에 휴대폰 기종(`PhoneModel`) | Contracts, 서버, 작업자 앱 |
| 같은 검사 두 번 저장 막기 (요청 번호 = 검사 번호) | `/api/inspections/camera`, `AppStore` |

### 10-23에 한 것 (이번 final_last에서 새로)
| 내용 | 위치 |
|---|---|
| **YOLO v5 모델 교체** (정상 4종 + 불량 `iphone12_promax_crack`, `iphone8_crack`) | `04_Vision/models/` |
| `_crack`으로 끝나면 불량, 클래스 이름 그대로 화면·DB·보고서에 | `YoloOnnxInspector.cs`, `CameraInspection.cs` |
| **보고서 CSV** (관리자 [보고서 저장] + 앱 종료 시 자동 저장, 5분 단위 센서 등 6칸) | `LineReportService.cs`, `/api/reports/lines/{id}`, `AdminMainForm.cs`, `ProductionLineApiClient.cs` |
| 영준 수정본: 회전판 시뮬레이션(LINE 2 정상, 3 불량, 4 혼합), 문의/답변 메뉴·화면 제거 | `LineStatusCard.cs`, `RotaryGateSimulation.cs`, `UiText.cs` |
| 공도현 AI 연동: 장비 진단을 보고 답함, `/api/ai/chat`·상태·진단 창구 (자유 상담 `/api/ai/consult`는 유지) | 05_AiAssistant, 서버 Infrastructure 3개, Shared 2개 |
| 가스 센서 **MQ-9 통일** (매뉴얼 MQ-2→MQ-9, D8 DO→A0 AO), 진단이 MQ-9 A0 값을 읽음 | `Manuals.json`, `DeviceDiagnosticsService.cs` |
| 정리 문서 | `md파일/` (발표용 기능 정리, 담당자별 전달 목록, 보고서 예시) |
| **관리자 앱 어두운 테마** (기능 변화 없음, 화면 모양만): 왼쪽 메뉴(아이콘·단체 채팅 안 읽은 수), 위쪽 LINE 1·서버 상태 표시, 지표 아이콘 카드, 둥근 라인 카드·어두운 컨베이어 그림, 하위 창 전체(표·입력칸·드롭다운·탭·스크롤바·버튼) 어두운 색, 단체 채팅 글 흰색. 공용 `SensorTrendPanel`·`GroupChatPanel`에는 `DarkMode`(기본 끔)를 추가해 작업자 앱은 그대로 | `AdminTheme.cs`(새), `AdminMainForm.cs`, `LineStatusCard.cs`, 관리자 창 8개 생성자 한 줄, `00_Shared/ProductionLine.ChatUI` 2개 |
| **작업자 앱 어두운 화면 + 관리자 테마와 병합** (컨베이어 PC): 메뉴 7개(실시간 검사, 검사 결과·사진, 설비·센서, 문제 조치(단체 채팅 탭 포함), AI 상담, 통계, 설정), 단체 채팅·센서 그래프 `DarkMode` 켬, 서버 접속 창도 관리자 창과 같은 어두운 색. 누락 수정: 아두이노 업로드 창 COM 목록 빈칸, 검사·벨트·원격 안내 글자 다시 표시, 구동 장치 표의 컨베이어·긴급 정지 상태를 실제 장비 보고로 표시, 원격 명령 중에는 조작 버튼만 잠금. 펌웨어 DHT11 읽기 실패 시 마지막 값 유지·자동 재시작(업로드 필요). 실물 확인: 벨트 구동/정지·원격 정지·잠금 해제 표시, 센서 직접 읽기(온도·습도·가스 정상) | `02_UserApp` 화면 파일, `ConnectWindowTheme.cs`(새), `SensorTrendPanel.cs`(DHT 미응답 안내), `ConveyorYoloControl.ino` |
| **경보 부저·판정 LED (펌웨어 업로드 완료)**: 가스 A0 600 이상 또는 온도 31℃ 이상이면 빨간 LED(D40) 깜빡임 + 부저(D8) 200Hz 0.5초 울림/0.5초 쉼 반복, 가스 580·온도 30.5℃ 미만이면 해제(긴급 정지는 벨트만 멈춤). 앱 미연결 때도 아두이노가 2초마다 온습도를 직접 읽음. YOLO 판정마다 앱이 `RESULT 1/0` 전송 → 불량 빨강(D40)·정상 초록(D42) 2초 표시. 인식 후 2배 가속 유지 기본값 3초 → 18초(작업자·관리자) | `ConveyorYoloControl.ino`, `ConveyorSerialController.cs`, `HardwareControls.cs`, `CameraInspection.cs`, `RemoteWorkerForm.cs` |
| **긴급 정지 버튼 잠금 안내 (작업자·관리자, 펌웨어 업로드 완료)**: 펌웨어 STATUS에 `emergencySwitchReady` 추가. 잠금 중에는 [벨트 구동] 비활성, 버튼이 원위치돼 0.2초 지나야 [잠금 해제] 활성. 작업자 상태 칸 '버튼 눌림/잠금 중/정상' + 안내 글자, 관리자 원격 창 버튼 옆 안내. 잠금 중 구동 거절은 연결을 끊지 않음. `WorkerStateDto`에 `EmergencyStopped`·`EmergencySwitchReady`(기본값 있음) 추가 → **서버·작업자·관리자 모두 다시 빌드** | `ConveyorYoloControl.ino`, `RemoteContracts.cs`, `RemoteWorkerForm.cs`, `UserMainForm.cs`, `ConveyorControls.cs`, `HardwareControls.cs`, `RemoteWorker.cs`, `ConveyorSerialController.cs` |
| **초기값 변경**: 불량 자동 분류 기본 켜짐(작업자·관리자), 55° 유지 15초 → 7초(작업자·관리자, 이동 시간 15초는 그대로), 온도 경보 31℃(해제 30.5℃, 펌웨어 업로드 완료). 자동 분류가 켜진 채 벨트 정지 중 불량이 인식되면 기존 안전 장치대로 검사 중지·자동 분류 해제 → **벨트 구동 → 검사 시작** 순서로 사용 | `HardwareControls.cs`, `RemoteWorkerForm.cs`, `ConveyorYoloControl.ino` |
| **보고서 CSV 시각 칸 글자 그대로 표시**: 한셀·엑셀이 시각을 날짜 숫자로 바꿔 `####`·오전/오후로 보이던 문제 → `="2026-10-22 16:37:22"` 형식으로 저장 (한셀에서 확인). 칸 너비는 CSV에 저장할 수 없어 좁으면 잘려 보임 → 열 경계 더블클릭 | `LineReportService.cs` |
| **작업자 AI 상담 상단 상태 영역** (공도현 `AI채팅_상단상태_버튼_추가병합` 패키지): 서버·AI 연결, 응답 결과·시간, [연결 확인]·[장비 상태 확인]. 질문은 기존 자유 상담(`/api/ai/consult`) 그대로 | `AiChatStatusPanel.cs`, `DeviceDiagnosticsForm.cs`, `AiConsultationApi.cs`, `AiReplyStatus.cs`, `UserMainForm.cs` |

## 5. DB (SQLite, EF Core 마이그레이션 자동 적용)

표: `Users`, `UserLineAccesses`, `Lines`, `Inspections`(+`PhoneModel`, `DetectionConfidence`, `HasImage`), `Issues`, `IssueHistories`, `IssueMessages`, `RequestLogs`(7일 후 CSV 백업 후 삭제), **`LineHardwareLogs`**(7일 보관)

마이그레이션: InitialCreate → AddIssueMessages → AddRequestLogs → AddCameraInspectionFields → **AddLineHardwareLogs → RemoveInspectionScores → AddInspectionPhoneModel**
- 손으로 쓴 마이그레이션에서 칸 삭제·변경은 `.Designer.cs`(목표 모델)가 꼭 필요 (SQLite는 표를 새로 만듦)

## 6. 시험 결과 (김하균 노트북, 2026-10-23)

| 시험 | 결과 |
|---|---|
| 솔루션 빌드 | 경고 0, 오류 0 |
| AI 모듈 모의 시험 (외부 호출 없음) | 188 통과 |
| AI 상담 창구 (자유·문제·일반 상담, 상태, 진단, MQ-9 A0 값) | 11 통과 |
| 보고서 API / 실제 관리자 앱 저장·자동 저장 | 19 통과 / 확인 |
| 센서 기록 DB / 점수 칸 삭제(기존 기록 보존) | 6 / 10 통과 |
| 회전판 시뮬레이션 (RemoteVerification `--simulation`) | 45 통과 (노트북 175% 배율의 최소 창 크기 1개만 실패, 원본도 동일) |
| v5 사진 시험 | 정상 3종·빈 벨트·검은 화면 맞음 / 다른 기종 깨진 폰 사진은 정상으로 나옴(모델 한계) |
| AI 상단 상태 영역 병합 후 (컨베이어 PC) | 빌드 경고 0·오류 0, RemoteVerification 156 통과, AI 모의 시험 58 통과, 실제 작업자 앱 화면에서 상태 영역·응답 시간·장비 상태 창 확인(테스트 서버, Gemini 키 없이 매뉴얼 답변) |

## 7. 남은 일 (컨베이어 PC·관리자 PC에서)

1. **Gemini 키 저장 — 관리자 PC(서버 켜는 PC)에서만** (1번 항목)
2. **실물 시험**: 깨진 8·12 Pro Max → 불량 + 이름(`iphone8_crack`, `iphone12_promax_crack`) + 서보 분류, 정상 4종 → 기종 이름, 빈 벨트 → 검출 없음
3. 센서 기록이 쌓이는지, 관리자 앱 종료 시 보고서 CSV가 실제 데이터로 나오는지
4. 결과를 기록하고 GitHub에 올리기 (김하균님 허락 후)

## 8. 주의

- 서버는 한 대만. 두 대 켜면 데이터가 따로 저장됩니다.
- 8·12 Pro Max가 아닌 깨진 폰은 v5가 잘 못 잡습니다(학습에서 제외됨).
- `Manuals.json`의 조명·분류 부분에 펌웨어 "V4"라는 옛 표기가 남아 있음 (지금은 V7) → 공도현 확인
- `docs/PROJECT_OVERVIEW_CURRENT.md`는 10-19 기준이라 옛 내용 있음
- 김하균 노트북에서는 Windows 스마트 앱 컨트롤이 새로 빌드한 DLL을 가끔 막았음 → `dotnet build --no-incremental`로 다시 빌드하면 풀림 (보안 설정은 바꾸지 말 것)
- 담당자별 전달 내용: `md파일/변경내용_전달목록_2026-10-23.md`
