# 현재 코드 분석 및 폴더 정리 기준

분석 기준: 2026-10-27, 전달된 `ProductionLine_final_last` 폴더. 원본의 C#/Python/Arduino/C++ 소스 170개(제3자 라이브러리와 검증 코드 포함)를 대상으로 파일·프로젝트 참조·진입점·핵심 처리 흐름을 분석했다. 모든 줄에 대해 정확성을 증명하거나 코드 감사를 인증하는 문서는 아니다.

## 모듈별 기능

| 모듈 | 주요 구성 | 확인한 구현 방식 |
|---|---|---|
| 공통 | Contracts, ProductionLineApiClient, GroupChatApiClient, ServerDiscovery, 공통 UI | HTTP/JSON DTO·Bearer 세션 전달, UDP 서버 검색, UI 소스 링크 |
| 관리자 | AdminLoginForm, AdminMainForm, LineDetailForm, RemoteWorkerForm, CompletionRequestForm | 계정 관리, 다중 라인 현황, 검사 사진·통계, 완료 요청 종결/반려, 명령 결과 확인 |
| 작업자 | UserMainForm의 partial 파일, CameraSession, CameraInspection, HardwareControls, RemoteWorker | 카메라와 검사 세션, 100ms 목표 연속 추론, 제품별 기록, USB 시리얼과 관리자 명령 처리 |
| 서버 | Program, AppStore, AuthService, Persistence, Migrations, Infrastructure | Minimal API, SQLite/EF Core, 역할·라인 권한, 문제 상태 전이, 데이터·사진 저장 |
| 영상처리 | YoloOnnxInspector, CombinedYoloModels | 레터박스 입력, ONNX 추론, 박스 후처리/NMS, 기종·파손 판정, 빈 벨트/검은 프레임 처리 |
| AI | ManualAssistant 분할 파일, AiConsultationService, AiResponseProtocol, 제공자별 클라이언트 | 16개 매뉴얼, 키워드·상담 문맥 선택, 최근 12개 대화 턴, 근거 ID 검증, 대체 응답 |
| 학습 | AnnotationCanvas, YoloProject, MainForm, train_yolo.py | 박스 좌표 정규화, YOLO 라벨 저장, 데이터 구성, 학습·ONNX 변환 |
| 하드웨어 | ConveyorYoloControl.ino, DHT·Servo·NeoPixel | Mega V7, PUL/DIR, DHT11/MQ-9, LED·부저·서보, 긴급정지 입력·통신 감시 |
| 검증 | AiAssistant.SmokeTests, UserApp/Verification, tools/*Verification | 가짜 HTTP, 모의 시리얼/장비, 정적 모델 검사, 화면·서버 통합 확인 도구 |

## 주요 처리 흐름

### 카메라 검사
프레임 수신 → ONNX 추론 → 기종·파손 규칙 적용 → 화면·인식 제품 이미지 표시 → 새 제품 기록 여부 판정 → 서버에 요청 ID·결과·사진 전달 → SQLite 집계 및 파일 저장. 벨트는 인식 때문에 매번 정지하지 않는다. 자동 분류와 인식 후 가속은 별도 설정과 프로토콜로 처리한다.

### AI 상담
인증·허용 라인 확인 → 일반 상담 ID 또는 문제 ID로 세션 구분 → 즉시 안내 대상 검사 → 관련 매뉴얼과 최근 대화 선택 → 선택 제공자에 구조화 응답 요청 → 등록된 출처 ID 검증 → 답변·출처·관리자 검토 여부 반환. 설정 없음/실패/한도/시간 초과 시 로컬 매뉴얼로 대체한다.

### 두 종류의 채팅
- 단체 채팅: 활성 계정 공동 대화, 2초 HTTP 증분 조회, 서버 JSON 최근 5,000건, 전송 ID 중복 방지.
- 관리자 상담: 문제 ID별 메시지, SQLite 저장, 작업자는 조치 중 전송, 완료 요청·종결 시 작업자 입력 제한. 일반적인 개인별 DM 수신자 모델과 다르다.

### 관리자 원격 명령
관리자 인증 → 대상 실제 라인·작업자 인스턴스·온라인 상태 확인 → 만료 시간과 요청 ID를 가진 명령 저장 → 작업자 heartbeat로 전달 → 작업자가 실행 결과 회신 → 관리자 화면에 결과 표시. 서버 연결 성공을 모터의 실제 동작 성공으로 대신 표시하지 않는다.

## 정리 방식

1. 원본은 삭제·수정·이동하지 않았다.
2. 업로드 준비본에는 기존 `00_Shared`~`06_TrainingTool` 구조와 상대 참조 경로를 유지했다.
3. C#/Python/Arduino 소스와 프로젝트 파일은 원본 바이트를 유지한다. 문서·Git 설정만 새로 작성했다.
4. 빌드·IDE 캐시와 실행 DB/기록은 준비본 복사 및 Git 추적에서 제외한다. 원본 파일 삭제와 다르다.
5. 기존 기획·인수인계·발표 보조 파일은 함부로 제거하지 않았다. 필요한 정리는 아래 승인 항목으로 남긴다.

## 승인 후 고려할 정리

| 제안 | 이유 | 현재 상태 |
|---|---|---|
| `md파일/`와 중간 버전 문서를 `docs/archive/`로 이동 | 최신 설명과 개발 이력 구분 | 이동하지 않음 · 링크/경로 변경 검토 필요 |
| `tools/build_*plan*.py`, 발표 제작 스크립트 별도 보관 | 제품 실행 코드와 보조 작업 구분 | 유지 · 삭제/이동 미승인 |
| 구형 원격 UI 검증의 컨트롤 타입 참조 수정 | 현재 DarkComboBox와 테스트 가정 불일치 | 코드 수정하지 않음 |
| 개인 PC 경로가 기본값인 선택 학습 스크립트 인자화 | 재현성 개선 | 코드 수정하지 않음 |
| 기본 데모 계정 생성 정책 변경 | 실제 서비스 전환 시 필요 | 기능 보존 위해 유지 |

## 문서의 기준

`docs/PROJECT_OVERVIEW_CURRENT.md`, `06_TrainingTool/README.md` 등에는 이전 JSON 저장소, 단일 phone 클래스, 구형 핀/V3 설명이 남아 있다. 현재 루트 README는 실제 `Program.cs`, `AppDbContext`, `model_info.json`, Mega V7 소스를 기준으로 SQLite·6클래스 모델·현행 센서 구성을 설명한다. 기존 문서는 중간 개발 기록으로 보존했다.

`AiServiceRegistration.cs`의 보조 등록 함수와 `Program.cs`의 실제 AI 등록 설정도 다르다. 현재 진입점이 사용하는 제공자별 설정을 README 실행 안내의 기준으로 삼았다.
