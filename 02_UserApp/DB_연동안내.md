# SQLite DB 클래스 문서 적용 및 조원 연결 안내

기준 문서: 사용자가 제공한 `C:/Users/moble/Desktop/DB_클래스_7개_이름과_역할.md`.
이 문서는 서버의 저장 모델 설계이며 사용자 앱에서 Entity 또는 AppDbContext를 직접 생성하지 않습니다.

## 역할별 연결

| 서버 내부 클래스 | 사용자 앱이 사용하는 기존 계약 | 화면·전송 역할 |
|---|---|---|
| UserEntity | LoginAsync → UserDto | 이름·역할·허용 라인을 이용. PasswordHash는 앱에서 조회하지 않음 |
| UserLineAccessEntity | UserDto.AllowedLineIds, GetLinesAsync | 서버가 허용한 라인만 선택. 최종 권한 판단은 서버 |
| LineEntity | LineStatusDto | 누적 개수, 실제/가상, 가동·검사 상태, 갱신 시각 표시 |
| InspectionEntity | InspectionDto | 검사 번호·시각·판정·점수·사유 조회. 원본 이미지 저장과는 별개 |
| IssueEntity | IssueDto, CreateIssueRequest, UpdateIssueStatusRequest | 문제 등록자·등록/갱신 시각·현재 상태·마지막 조치 메모 표시. 새 조치만 전송 |
| IssueHistoryEntity | 아직 공유된 조회 계약 없음 | 전체 변경 이력을 임의로 생성하지 않음. 화면에 현재 정보와 전체 이력의 차이 표시 |
| AppDbContext | 사용자 앱에서 사용하지 않음 | 서버 담당이 테이블·관계·조회·저장을 관리 |

## 이번 사용자 앱 변경

- 문제 단계: 발생에서 확인, 확인에서 조치 중, 조치 중에서 완료 요청. 조치 중의 추가 메모 저장은 허용. 완료 요청 후에는 관리자 처리 대기, 종결 후에는 작업자 수정 불가.
- 버튼 비활성화와 전송 전 검사를 함께 적용. 저장 중에는 조치 버튼과 메모 편집을 잠금. 이는 UI 안내이며 서버에서도 상태 순서·역할·최신 상태를 검증해야 함.
- 현재 메모는 IssueDto.ActionNote에서 표시. IssueHistoryEntity가 생성됐다는 이유로 과거 이력이 저장된다고 가정하지 않음.
- 검사 개수는 LineStatusDto의 서버 누적값, 표는 InspectionDto의 개별 기록. 두 값을 행 수로 혼동하지 않음.

## 서버 담당에게 전달할 계약

- 주소: PRODUCTIONLINE_SERVER_URL에 http://실제서버주소:5050 설정. 실제 IP는 미확정. 기존 로그인·조회·갱신 함수와 DTO 유지.
- 조치 호출: UpdateIssueAsync(issueId, new UpdateIssueStatusRequest(status, 새메모)). 서버는 인증 토큰에서 ChangedBy를 정하고, 서버 시각으로 ChangedAt을 기록해야 함.
- 서버 저장 요구: IssueEntity 최신 상태·메모 갱신과 IssueHistoryEntity 추가를 한 트랜잭션에서 처리. 이전 상태는 클라이언트가 아닌 서버의 최신 데이터에서 읽기.
- 조치 중 → 조치 중 메모 저장도 이력으로 남길지 합의 필요. 동일 요청 중복 전송·동시 수정 시 처리도 서버 규격으로 확정 필요.
- 기존 응답은 IssueDto, 기존 오류는 ApiMessage. 조회/변경 권한 또는 상태 충돌 오류가 오면 입력 보존 후 갱신·재시도 안내.
- 추가 필요: 문제별 이력 조회 함수·엔드포인트·응답 DTO·정렬/페이지 규격. 예상 표시 항목은 Id, IssueId, PreviousStatus, NewStatus, ChangedBy, ChangedAt, ActionNote. 이 목록은 문서 기준이며 확정 API가 아님.
- 반려 시 작업자가 어느 상태로 되돌아오는지 규격 필요. 현재는 서버에서 조치 중으로 반환되면 사용자 앱이 다시 편집을 허용함.
- 관리자 문의·AI 상담 저장은 6개 Entity의 범위 밖. 기존 문의 등록 및 PC별 대화 저장이 DB 채팅 저장으로 바뀐 것은 아님.

## 실제 확인 결과와 미완료

- 2026-10-16 현재 이 작업 폴더의 서버에는 Data 폴더의 7개 클래스가 없고, 기존 AppStore는 JSON 저장 구조임. 전달 문서의 다른 저장소 진행 상태를 이 폴더의 구현 완료로 표현하지 않음.
- 수정 파일: 복사본의 UserMainForm.cs, Verification/FunctionalChecks.cs, 구현안내.md, 이 안내. 원본 앱·관리자·공용 계약·서버 소스 수정 없음.
- 빌드: 별도 artifacts 경로에서 경고 0·오류 0. 실행 중인 이전 앱의 파일 잠금을 피하기 위한 경로이며 소스는 동일.
- 실행 검증: 별도 테스트 서버에서 확인 전 완료 요청 차단, 확인→조치→완료 요청, 최신 메모 표시, 완료 요청 후 수정 차단, 라인 정보 표시 및 기존 조치·상담·합성 프레임 검사 검증 통과. 실제 카메라 수신은 장치 열기 실패로 미검증.
- DB 생성·마이그레이션·JSON 이전·DB 영속성·이력 조회는 미구현/미검증. 제공 MD를 읽었다는 사실과 실제 DB 연동 검증을 구분함.

## 검증 명령

프로젝트 루트에서 실행:

```powershell
dotnet build ./02_UserApp/Verification/Verification.csproj
dotnet run --project ./02_UserApp/Verification/Verification.csproj
```

서버를 먼저 실행한 뒤 솔루션의 `ProductionLine.UserApp`을 실행합니다. 이전 버전이 실행 중이면 종료 후 재실행해야 새 화면이 반영됩니다.
