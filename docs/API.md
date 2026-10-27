# 기본 REST API

기본 주소: `http://서버IP:5050`

로그인 성공 후 `Authorization: Bearer {token}` 헤더를 사용합니다.

| Method | Path | 권한 | 기능 |
|---|---|---|---|
| POST | `/api/auth/login` | 공개 | 로그인 |
| GET | `/api/lines` | 로그인 | 접근 가능한 라인 조회 |
| GET/POST/PUT | `/api/users` | 관리자 | 사용자 관리 |
| GET | `/api/inspections?lineId=1` | 라인 권한 | 검사 이력 |
| POST | `/api/inspections/simulate` | 라인 권한 | 기본 검사 실행 |
| POST | `/api/inspections/camera` | 라인 권한 | 카메라 결과·검출 사진 저장 |
| GET | `/api/inspections/{id}/image` | 해당 검사 라인 권한 | 검사 번호에 연결된 JPEG 사진 조회 |
| GET/POST | `/api/issues` | 라인 권한 | 문제 조회·등록 |
| GET | `/api/issues/{id}/messages?afterId=번호` | 해당 문제 라인 권한 | 문제별 대화 전체·증분 조회 |
| POST | `/api/issues/{id}/messages` | 해당 문제 라인 권한 | 작업자 문의·관리자 답장 전송 |
| PUT | `/api/issues/{id}/status` | 라인 권한 | 문제 상태 변경 |
| POST | `/api/ai/consult` | 라인 권한 | 문제 상담 |

정확한 요청·응답 필드는 `00_Shared/ProductionLine.Shared/Contracts.cs`를 기준으로 합니다.

카메라 요청의 선택 필드 `resultImage`는 JPEG 바이트의 Base64 표현입니다(원본 5MB 이하). 사용자 앱은 박스가 포함된 인식 프레임을 긴 변 최대 1280px로 저장합니다. 응답·검사 목록의 `hasImage`로 사진 유무를 확인하고, 사진은 목록과 분리해 필요할 때만 조회합니다. 사진 없는 기존 요청·저장 기록과 호환합니다. 사진 응답은 `image/jpeg`, `Cache-Control: no-store`이며 인증·라인 권한을 검사합니다.


메시지는 `{ message: 문자열 }`로 전송하며 공백 제외 1~2000자입니다. 보낸 사람·역할·시각은 인증 정보로 서버에서 결정합니다. 종결 후에는 조회만 허용합니다. 메시지는 기존 서버 JSON 저장소에 저장하고 같은 문제 안에서 ID 오름차순으로 반환합니다.
