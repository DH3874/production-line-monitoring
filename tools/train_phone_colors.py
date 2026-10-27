"""Train the requested four body colors once real labeled photos exist.

Uses a separate classification head, so color remains independent of model ID.
Photos should be cropped to handset bodies before entering the dataset.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from hashlib import sha256
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "training/phone_colors"
COLORS = {"black": "검정", "white": "흰색", "silver": "실버", "pacific_blue": "퍼시픽 블루"}
EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp"}


def prepare():
    for split in ["train", "val"]:
        for name in COLORS:
            folder = PROJECT / "dataset" / split / name
            folder.mkdir(parents=True, exist_ok=True)
            (folder / ".gitkeep").touch(exist_ok=True)
    (PROJECT / "models").mkdir(exist_ok=True)
    mapping = {"color_labels": COLORS, "color_classification_trained": False,
               "reason": "실제 색상별 사진이 아직 제공되지 않음",
               "minimum_run_gate": {"train_per_color": 20, "validation_per_color": 5},
               "recommended_collection": {"train_per_color": 100, "validation_per_color": 25, "test_per_color": 25},
               "note": "숫자는 시작 목표이며 정확도를 보장하지 않음. 실제 컨베이어 조명에서 세션별로 분리 촬영."}
    (PROJECT / "color_labels.json").write_text(json.dumps(mapping, ensure_ascii=False, indent=2), encoding="utf-8")
    inspect_dataset()


def inspect_dataset(require_ready=False):
    from PIL import Image
    counts = {}
    seen = defaultdict(list)
    for split in ["train", "val"]:
        counts[split] = {}
        for color in COLORS:
            files = sorted(p for p in (PROJECT / "dataset" / split / color).rglob("*")
                           if p.is_file() and p.suffix.lower() in EXTENSIONS)
            for path in files:
                with Image.open(path) as image:
                    image.verify()
                seen[sha256(path.read_bytes()).hexdigest()].append(str(path.relative_to(PROJECT)))
            counts[split][color] = len(files)
    duplicates = [locations for locations in seen.values() if len(locations) > 1]
    ready = (not duplicates and all(counts["train"][name] >= 20 and counts["val"][name] >= 5 for name in COLORS))
    report = {"ready": ready, "counts": counts, "duplicate_files": duplicates,
              "missing_data_message": "Each of all four colors needs at least 20 train and 5 val real, distinct photos before this training starts."}
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)
    if require_ready and not ready:
        raise RuntimeError("4색 학습 자료가 부족하거나 중복 사진이 있습니다. 실제 사진을 먼저 준비하세요.")
    return report


def train(epochs):
    report = inspect_dataset(require_ready=True)
    os.environ["YOLO_OFFLINE"] = "true"
    os.environ["YOLO_AUTOINSTALL"] = "false"
    import torch
    from ultralytics import YOLO
    torch.set_num_threads(4)
    # Reuse installed architecture and local learned backbone; no download.
    model = YOLO("yolo26n-cls.yaml").load(str(ROOT / "models/best.pt"))
    result = model.train(data=str(PROJECT / "dataset"), epochs=epochs, imgsz=224,
                         batch=16, device="cpu", workers=0, seed=19,
                         optimizer="AdamW", lr0=0.0003, pretrained=True,
                         project=str(PROJECT / "models/runs"), name="four_colors",
                         exist_ok=False, auto_augment=None, erasing=0.0,
                         hsv_h=0.0, hsv_s=0.05, hsv_v=0.1,
                         fliplr=0.5, flipud=0.5, plots=True)
    output = PROJECT / "models"
    shutil.copy2(Path(result.save_dir) / "weights/best.pt", output / "color.pt")
    classifier = YOLO(str(output / "color.pt"))
    exported = Path(classifier.export(format="onnx", imgsz=224, dynamic=False, simplify=False, opset=17))
    if exported.resolve() != (output / "color.onnx").resolve():
        shutil.copy2(exported, output / "color.onnx")
    actual_names = dict(classifier.names)
    assert set(actual_names.values()) == set(COLORS), actual_names
    info = {"classes": actual_names, "labels_ko": COLORS,
            "trained": True, "data_counts": report["counts"], "epochs": epochs,
            "task": "classify", "input": "RGB float32 NCHW /255, shortest-side resize 224 then center crop 224",
            "sha256": sha256((output / "color.onnx").read_bytes()).hexdigest(),
            "physical_size_calibrated": False, "independent_test_available": False}
    (output / "color_info.json").write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding="utf-8")
    print("FOUR_COLOR_TRAINING_AND_ONNX_COMPLETED", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["prepare", "inspect", "train"])
    parser.add_argument("--epochs", type=int, default=80)
    args = parser.parse_args()
    if args.action == "train":
        train(args.epochs)
    elif args.action == "inspect":
        inspect_dataset()
    else:
        prepare()
