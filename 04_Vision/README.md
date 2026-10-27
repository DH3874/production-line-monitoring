# 아이폰 영상처리

2026-10-23부터 **아이폰 통합 모델 v5 하나만** 사용합니다. (예전 v4는 GitHub `training_model/v4_merged_default/`에 있음)

| 항목 | 값 |
|---|---|
| 모델 | `models/best.onnx` (v5, SHA256 앞 16자 `9fb6d967b0168629`) |
| 입력 / 출력 | 640×640 / `[1,10,8400]` |
| 클래스 (번호 순) | 0 `iphone12_promax_crack`(불량·깨진 12 Pro Max) · 1 `iphone16_white` · 2 `iphone12_pro_silver` · 3 `iphone15_pro_Natural_Titanium` · 4 `iphone14_white` · 5 `iphone8_crack`(불량·깨진 8) |
| 판정 | 이름이 `_crack`으로 끝나는 클래스가 하나라도 있으면 **불량**(그 이름이 기종 칸에 저장), 없으면 가장 큰 박스의 기종으로 **정상**, 아무것도 없으면 대기 |
| 기준 | 신뢰도 45%, NMS IoU 0.45 |
| 원본 | GitHub `dragon1894-lab/mobile-project-2`의 `training_model/v5_merged_default/v5_merged_default.onnx` (인수인계: `training_model/HANDOFF_v5.md`) |
| 주의 | 8·12 Pro Max가 아닌 다른 깨진 폰은 학습에서 빠져서 약 69%만 불량으로 잡습니다. |

`models/model_info.json`과 `models/classes.txt`에 같은 내용이 있고, 빌드하면 모델과 함께 실행 폴더 `Models/`로 복사됩니다.

## 라벨 기준과 박스 처리

학습 라벨은 한 폰에 **폰 전체 박스 + 작은 박스(기종은 카메라 모듈, 파손은 깨진 부위)**를 같은 이름으로 그렸습니다. 그래서 앱은 큰 박스 안에 80% 이상 들어간 같은 종류의 작은 박스를 빼고, 폰 전체 박스로 위치·개수·크기(px)를 셉니다(`YoloOnnxInspector.RemoveNestedBoxes`).

## 성능 (2026-10-21, 이 PC 기준)

- 학습에 안 쓴 시험 사진 78장: **78/78** (C# 앱 코드로 확인).
- 처리 시간: 1장 약 60~70ms (CPU). 예전 14개 동시 실행은 약 960ms.
- 실제 컨베이어 사진 8장: 정상 폰을 불량으로 판정한 것 0장. **그러나 8장 모두 15 Pro로 판정** — 실제는 15 Pro 4, 16 화이트 2, 카메라 2개짜리 흰색 폰 2라서 기종 정답은 4/8.
- 원인: 컨베이어에서 찍은 학습 사진이 거의 15 Pro뿐. **해결: 컨베이어 카메라로 기종마다 10~20장 + 빈 벨트 사진을 찍어 학습 PC(김하균)로 보내 재학습**합니다. 실제로 깨진 폰의 컨베이어 시험은 아직 안 했습니다.

## 실행

1. 프로젝트 폴더의 `컨베이어_통합실행.cmd`로 실행합니다(빌드 후 작업자 앱 실행). 또는 `ProductionLineSystem.sln`을 빌드해 서버 → 사용자 앱 순서로 실행합니다.
2. 사용자 화면에서 카메라 연결 → **YOLO 모델 확인**(“기종·깨짐 검사”, 클래스 5개 표시) → 검사 시작.
3. “전체 학습 사용” 버튼은 남아 있지만 이제 `Models` 폴더의 모델 1개(v2)만 불러옵니다. “YOLO 모델 선택”으로 다른 ONNX를 고르면 앱을 다시 열 때까지 그 모델을 씁니다.
4. 벨트 연결은 [아두이노 연결 안내](../hardware/README.md)를 따릅니다.

## 시험 도구 (`ProductionLine.Vision.Tester`)

```text
ProductionLine.Vision.Tester.exe "사진.jpg" "best.onnx" 0.5          사진 1장 판정, 결과 이미지 04_Vision/output/iphone_result.jpg
ProductionLine.Vision.Tester.exe --batch "사진폴더" 0.45 [모델.onnx]   폴더 전체 판정 + 1장당 시간 (모델 생략 시 Models 폴더 전체)
```

## 예전 모델과 기록

- 옛 모델 14개와 예전 학습 결과: 이 PC 바탕화면 `옛 yolo 모델` 폴더, GitHub `training_model/옛 yolo 모델/`(폴더마다 `MODEL_INFO.md`).
- 왜 바꿨는지와 점검 결과: GitHub `all_phones/README.md`, `all_phones/from_local_pc_20261020/HANDOFF.md`, `docs/AI_COLLAB_LOG.md`.
- 예전 적용 기록(지금 상태와 다름): [전체 외곽 재학습](../docs/PHONE_FULL_BODY_TRAINING_20261019.md), [과거 패키지 통합](../docs/PHONE_PACKAGE_MERGE.md). 이 문서들의 `output/…` 실행 경로는 정리되어 없습니다.
- 색상 4종 학습 준비: [phone_colors](../training/phone_colors/README.md).
