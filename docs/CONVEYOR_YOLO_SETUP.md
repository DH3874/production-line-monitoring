# YOLO 휴대폰 검출과 컨베이어 정지

설정·업로드·구동 순서는 [hardware/README.md](../hardware/README.md)에 정리했습니다.

- Arduino IDE 코드: `hardware/ConveyorYoloControl/ConveyorYoloControl.ino`
- C# USB 시리얼 통신: `02_UserApp/ProductionLine.UserApp/ConveyorSerialController.cs`
- 사용자 버튼과 자동 정지 연결: `02_UserApp/ProductionLine.UserApp/ConveyorControls.cs`
- 실제 검출 연결: `CameraInspection.cs`에서 매 YOLO 결과를 받은 후 `StopOnPhoneAsync` 호출
- 모의 장치 검증: `02_UserApp/Verification`, `--continuous-yolo "아이폰 샘플.jpg"`

검증 결과: C# 검증 빌드 경고 0 / 오류 0. 실제 ONNX 모델 + 모의 컨베이어의 23개 검사 통과. 실제 COM 포트 개방·모터 구동·아두이노 업로드는 수행하지 않았습니다. 일반 실행 폴더로 빌드할 때 기존 DLL 파일이 사용 중이면 사용자 프로그램을 완전히 종료하고 다시 빌드하세요.
