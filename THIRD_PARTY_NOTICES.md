# 제3자 구성 요소

팀 자체 코드의 공개 라이선스는 확정하지 않았다. 아래 포함 자료의 원본 출처·고지 파일을 보존하며, 이 문서가 각 구성 요소의 라이선스를 대체하지 않는다.

| 구성 요소 | 저장소 내 자료 | 출처·고지 |
|---|---|---|
| Arduino CLI 1.5.1 | `tools/arduino-cli/arduino-cli.exe` | `LICENSE.txt`, `provenance.json`에 GPL-3.0·배포 URL·SHA-256 기록 |
| Adafruit DHT | `hardware/ConveyorYoloControl/DHT.*` | 같은 폴더 `DHT_LICENSE.txt` |
| Arduino Servo | `hardware/ConveyorYoloControl/Servo.*`, `avr/` | 같은 폴더 `SERVO_LICENSE.txt` |
| Adafruit NeoPixel | `hardware/ConveyorYoloControl/Adafruit_NeoPixel.*` | 소스 저작권 고지와 `COPYING` |
| 통합 YOLO ONNX | `04_Vision/models/best.onnx` | `model_info.json`에 원본 모델 출처·해시·클래스 기록 |
| .NET 의존성 | OpenCvSharp, ONNX Runtime, EF Core, System.IO.Ports | 각 `.csproj`의 PackageReference. 빌드 시 NuGet에서 복원 |
| 선택 학습 환경 | Python Ultralytics | 학습 도구가 외부 Python 환경을 사용. 가상 환경과 새 학습 출력은 저장소에서 제외 |

샘플 이미지와 팀 제공 모델은 원본을 보존했다. 자체 코드와 팀 공유 자료의 공개 범위를 최종 확인한 뒤 원격 업로드를 승인한다.
