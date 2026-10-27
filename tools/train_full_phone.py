"""Rebuild full-body annotations, fine-tune locally, and audit ONNX results.

No generated image is counted as a new photograph. The seven source photos
remain the complete real dataset; same-session validation is diagnostic only.
"""
from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime
from hashlib import sha256
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "training/full_phone_20261019"
NAMES = ["phone", "iphone_16", "iphone_15_pro"]
# Coordinates reviewed against the original 1920x1080 photos. The old labels
# for model classes enclosed camera islands, not the entire handset.
ANNOTATIONS = [
    ("WIN_20261017_10_57_11_Pro", 0, [258, 166, 1784, 938], "train"),
    ("WIN_20261017_11_08_19_Pro", 2, [246, 162, 1795, 923], "val"),
    ("WIN_20261017_11_08_25_Pro", 2, [167, 137, 1710, 917], "train"),
    ("WIN_20261017_11_08_36_Pro", 1, [172, 144, 1718, 914], "train"),
    ("WIN_20261017_11_08_42_Pro", 1, [198, 145, 1747, 924], "val"),
    ("WIN_20261017_11_15_18_Pro", 0, [251, 143, 1794, 923], "train"),
    ("WIN_20261017_11_15_24_Pro", 0, [326, 154, 1870, 934], "train"),
]


def digest(path):
    return sha256(Path(path).read_bytes()).hexdigest()


def read_image(path):
    import cv2
    import numpy as np
    return cv2.imdecode(np.frombuffer(Path(path).read_bytes(), dtype=np.uint8), cv2.IMREAD_COLOR)


def save_image(path, image):
    import cv2
    success, encoded = cv2.imencode(Path(path).suffix, image)
    if not success:
        raise RuntimeError(f"Cannot encode {path}")
    Path(path).write_bytes(encoded.tobytes())


def write_json(path, data):
    Path(path).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def prepare():
    import cv2
    if (PROJECT / "manifest.json").exists():
        raise RuntimeError("Prepared project already exists; inspect it instead of overwriting labels.")
    PROJECT.mkdir(parents=True, exist_ok=True)
    (PROJECT / "classes.txt").write_text("\n".join(NAMES) + "\n", encoding="utf-8")
    records = []
    for stem, class_id, box, split in ANNOTATIONS:
        source = ROOT / "images" / (stem + ".jpg")
        frame = read_image(source)
        if frame is None:
            raise RuntimeError(f"Unreadable source: {source}")
        height, width = frame.shape[:2]
        x1, y1, x2, y2 = box
        assert 0 <= x1 < x2 <= width and 0 <= y1 < y2 <= height
        annotation = (f"{class_id} {(x1+x2)/2/width:.6f} {(y1+y2)/2/height:.6f} "
                      f"{(x2-x1)/width:.6f} {(y2-y1)/height:.6f}\n")
        for directory in [PROJECT / "images", PROJECT / "dataset/images" / split]:
            directory.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, directory / source.name)
        for directory in [PROJECT / "labels", PROJECT / "dataset/labels" / split]:
            directory.mkdir(parents=True, exist_ok=True)
            (directory / (stem + ".txt")).write_text(annotation, encoding="utf-8")
        # Audit overlays are documentation, not training images.
        cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 210, 0), 4)
        cv2.putText(frame, f"{NAMES[class_id]} FULL BODY ({split})", (x1, y1 - 14),
                    cv2.FONT_HERSHEY_SIMPLEX, 1.1, (0, 210, 0), 3)
        (PROJECT / "annotation_review").mkdir(exist_ok=True)
        save_image(PROJECT / "annotation_review" / source.name, frame)
        records.append({"file": source.name, "source": str(source.relative_to(ROOT)),
                        "sha256": digest(source), "class_id": class_id,
                        "class": NAMES[class_id], "box_xyxy": box,
                        "image_size": [width, height], "split": split})
    hashes = [r["sha256"] for r in records]
    assert len(set(hashes)) == len(hashes), "Duplicate source photograph"
    dataset = PROJECT / "dataset"
    yaml = f"path: '{dataset.as_posix()}'\ntrain: images/train\nval: images/val\nnames:\n"
    yaml += "".join(f"  {i}: {name}\n" for i, name in enumerate(NAMES))
    (dataset / "data.yaml").write_text(yaml, encoding="utf-8")
    (PROJECT / "models").mkdir(exist_ok=True)
    manifest = {"created_at": datetime.now().astimezone().isoformat(),
                "annotation_target": "full_handset_body", "names": NAMES,
                "real_unique_images": len(records),
                "source_model": "models/best.pt", "source_model_sha256": digest(ROOT / "models/best.pt"),
                "split_counts": dict(Counter(r["split"] for r in records)),
                "class_counts": dict(Counter(r["class"] for r in records)),
                "unavailable_requested_models": ["iphone_14", "iphone_8", "iphone_12_pro_max", "iphone_12_pro"],
                "validation_limitation": "Same camera/session; NOT an independent accuracy benchmark.",
                "color_classification_trained": False,
                "physical_size_calibrated": False,
                "records": records}
    write_json(PROJECT / "manifest.json", manifest)
    print(json.dumps(manifest, ensure_ascii=False, indent=2), flush=True)


def train(epochs, phone_only=False):
    import os
    os.environ["YOLO_AUTOINSTALL"] = "false"
    os.environ["YOLO_OFFLINE"] = "true"
    import torch
    from ultralytics import YOLO
    torch.set_num_threads(4)
    torch.set_num_interop_threads(2)
    output = PROJECT / "models"
    source = ROOT / "training/full_phone_20261019/models/runs/full_phone/weights/last.pt" if phone_only else ROOT / "models/best.pt"
    model = YOLO(str(source))
    result = model.train(
        data=str(PROJECT / "dataset/data.yaml"), epochs=epochs, imgsz=640,
        batch=4, device="cpu", workers=0, cache=False,
        project=str(output / "runs"), name="full_phone", exist_ok=False,
        seed=19, deterministic=True, optimizer="AdamW", lr0=0.001,
        lrf=0.05, weight_decay=0.0005, patience=epochs, warmup_epochs=3,
        freeze=10 if phone_only else None,
        degrees=20 if phone_only else 30, translate=0.15 if phone_only else 0.25,
        scale=0.5 if phone_only else 0.65, flipud=0.5, fliplr=0.5,
        hsv_h=0.01, hsv_s=0.35, hsv_v=0.35,
        mosaic=0.5 if phone_only else 0.75, close_mosaic=20 if phone_only else 10, multi_scale=0.1 if phone_only else 0.2,
        mixup=0.0, plots=True, save=True, amp=False,
    )
    source_best = Path(result.save_dir) / "weights/best.pt"
    shutil.copy2(source_best, output / "best.pt")
    export_model(epochs, source, str(result.save_dir))


def export_model(epochs, source=None, run=None):
    from ultralytics import YOLO
    import yaml
    output = PROJECT / "models"
    # The app performs NMS itself and consumes [1,4+nc,N].
    exported = Path(YOLO(str(output / "best.pt")).export(
        format="onnx", imgsz=640, dynamic=False, nms=None,
        simplify=False, opset=17, batch=1, device="cpu"))
    if exported.resolve() != (output / "best.onnx").resolve():
        shutil.copy2(exported, output / "best.onnx")
    import onnxruntime as ort
    session = ort.InferenceSession(str(output / "best.onnx"), providers=["CPUExecutionProvider"])
    shape = session.get_outputs()[0].shape
    assert len(shape) == 3 and (shape[1] == len(NAMES) + 4 or shape[2] == len(NAMES) + 4), shape
    info = {"classes": NAMES, "classificationAvailable": False,
            "inputShape": session.get_inputs()[0].shape, "outputShape": shape,
            "sha256": digest(output / "best.onnx"), "confidenceThreshold": 0.5,
            "annotationTarget": "full_handset_body", "realUniqueImages": 7,
            "trainingImages": 5, "validationImages": 2,
            "colorClassificationAvailable": False, "physicalSizeCalibrated": False,
            "note": "휴대폰 전체 외곽 재학습. 소량 동일세션 자료의 시제품이며 기종·품질·색상·실제 치수 정확도는 미검증."}
    write_json(output / "model_info.json", info)
    run_path = Path(run) if run else output / "runs/full_phone"
    settings = yaml.safe_load((run_path / "args.yaml").read_text(encoding="utf-8"))
    write_json(output / "training_result.json", {
        "model": str(source or settings["model"]), "data": str(PROJECT / "dataset/data.yaml"),
        "epochs": epochs, "imgsz": 640, "device": "cpu", "run": str(run_path),
        "best_pt": str(output / "best.pt"), "best_onnx": str(output / "best.onnx"),
        "augmentation": {key: settings[key] for key in ["scale", "multi_scale", "translate", "degrees", "hsv_h", "hsv_s", "hsv_v", "mosaic", "close_mosaic"]},
        "independent_test_available": False, "mergedDatasetRequiresRetraining": False,
        "color_classification_trained": False, "physical_size_calibrated": False})
    print("FULL_PHONE_TRAINING_AND_ONNX_COMPLETED", flush=True)


def iou(a, b):
    x1, y1, x2, y2 = max(a[0], b[0]), max(a[1], b[1]), min(a[2], b[2]), min(a[3], b[3])
    intersection = max(0, x2 - x1) * max(0, y2 - y1)
    union = (a[2]-a[0]) * (a[3]-a[1]) + (b[2]-b[0]) * (b[3]-b[1]) - intersection
    return float(intersection / union) if union else 0.0


def evaluate():
    import cv2
    import numpy as np
    import torch
    from ultralytics import YOLO
    torch.set_num_threads(4)
    manifest = json.loads((PROJECT / "manifest.json").read_text(encoding="utf-8"))
    legacy = ROOT / "04_Vision/models/alternatives/02_기종검출_카메라영역/best.onnx"
    candidates = {"before": legacy if legacy.is_file() else ROOT / "models/best.onnx", "after": PROJECT / "models/best.onnx"}
    report = {"threshold": 0.5, "limitation": "Diagnostic only: same-session photos and their transforms; not independent test data.", "models": {}}
    for version, checkpoint in candidates.items():
        model = YOLO(str(checkpoint), task="detect")
        records = []
        destination = PROJECT / "evaluation" / version
        destination.mkdir(parents=True, exist_ok=True)
        for entry in manifest["records"]:
            frame = read_image(PROJECT / "images" / entry["file"])
            height, width = frame.shape[:2]
            # Resize the whole frame onto a fixed canvas to diagnose apparent
            # scale sensitivity. These are NOT independent photographs.
            for scale in [1.0, 0.65, 0.4]:
                if scale == 1:
                    transformed, truth = frame, entry["box_xyxy"]
                else:
                    scaled_width, scaled_height = round(width * scale), round(height * scale)
                    dx, dy = (width-scaled_width)//2, (height-scaled_height)//2
                    transformed = np.full_like(frame, 114)
                    transformed[dy:dy+scaled_height, dx:dx+scaled_width] = cv2.resize(frame, (scaled_width, scaled_height))
                    truth = [entry["box_xyxy"][i] * scale + (dx if i%2 == 0 else dy) for i in range(4)]
                prediction = model.predict(transformed, imgsz=640, conf=0.5, iou=0.45, agnostic_nms=True, verbose=False)[0]
                boxes = []
                for box in prediction.boxes:
                    class_id = int(box.cls.item())
                    name = model.names[class_id]
                    boxes.append({"class": name, "confidence": float(box.conf.item()),
                                  "xyxy": box.xyxy[0].tolist(), "whole_body_iou": iou(box.xyxy[0].tolist(), truth)})
                phone_boxes = [b for b in boxes if b["class"] != "crack"]
                best = max(phone_boxes, key=lambda b: b["whole_body_iou"], default=None)
                records.append({"file": entry["file"], "source_split": entry["split"],
                                "scale": scale, "truth_class": entry["class"],
                                "whole_body_hit_iou50": bool(best and best["whole_body_iou"] >= 0.5),
                                "best_iou": best["whole_body_iou"] if best else 0,
                                "detections": boxes})
                annotated = prediction.plot()
                if scale == 1:
                    save_image(destination / entry["file"], annotated)
        originals = [r for r in records if r["scale"] == 1]
        heldout = [r for r in originals if r["source_split"] == "val"]
        negative_checks = []
        screenshot_path = Path("C:/Users/moble/AppData/Local/Temp/codex-clipboard-cf8a905b-68e9-4fc3-b1c8-661e6736f03b.png")
        if screenshot_path.exists():
            screenshot = read_image(screenshot_path)
            empty_camera = screenshot[175:420, 223:550].copy()
            output = model.predict(empty_camera, imgsz=640, conf=0.5, verbose=False)[0]
            negative_checks.append({"source": "user_screenshot_left_camera_empty_belt", "detections": [
                {"class": model.names[int(b.cls.item())], "confidence": float(b.conf.item())}
                for b in output.boxes]})
            save_image(destination / "empty_belt_screenshot.jpg", output.plot())
        report["models"][version] = {"onnx_sha256": digest(checkpoint),
            "original_whole_body_hits": sum(r["whole_body_hit_iou50"] for r in originals),
            "original_count": len(originals), "heldout_same_session_hits": sum(r["whole_body_hit_iou50"] for r in heldout),
            "heldout_count": len(heldout), "scale_hits": {str(scale): sum(r["whole_body_hit_iou50"] for r in records if r["scale"] == scale) for scale in [1.0, 0.65, 0.4]},
            "negative_checks": negative_checks, "records": records}
    write_json(PROJECT / "evaluation.json", report)
    print(json.dumps({k: {key: value for key, value in v.items() if key != "records"}
                      for k, v in report["models"].items()}, ensure_ascii=False, indent=2), flush=True)


def deploy():
    report = json.loads((PROJECT / "evaluation.json").read_text(encoding="utf-8"))
    after, before = report["models"]["after"], report["models"]["before"]
    assert after["original_whole_body_hits"] > before["original_whole_body_hits"], "No full-body improvement"
    assert after["original_whole_body_hits"] == after["original_count"], "Not every provided photograph has a full-body detection"
    assert after["heldout_same_session_hits"] == after["heldout_count"], "Heldout full-body check failed"
    assert all(not item["detections"] for item in after["negative_checks"]), "Empty belt produces false detections"
    assert after["onnx_sha256"] == digest(PROJECT / "models/best.onnx")
    backup = ROOT / "output/backups" / ("before-full-phone-" + datetime.now().strftime("%Y%m%d-%H%M%S") + ".zip")
    backup.parent.mkdir(parents=True, exist_ok=True)
    targets = [ROOT / "models", ROOT / "04_Vision/models"]
    names = ["best.pt", "best.onnx", "model_info.json", "training_result.json", "deployed_model.json"]
    with zipfile.ZipFile(backup, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for folder in targets:
            for name in names:
                path = folder / name
                if path.is_file():
                    archive.write(path, path.relative_to(ROOT).as_posix())
        for path in [ROOT / "classes.txt", *(ROOT / "labels").glob("*.txt"), *(ROOT / "dataset").rglob("*")]:
            if path.is_file():
                archive.write(path, path.relative_to(ROOT).as_posix())
    # Retain the old camera-island detector as a selectable alternative.
    old = ROOT / "04_Vision/models/alternatives/02_기종검출_카메라영역"
    old.mkdir(parents=True, exist_ok=False)
    for name in ["best.onnx", "model_info.json"]:
        shutil.copy2(ROOT / "04_Vision/models" / name, old / name)
    source = PROJECT / "models"
    deployed = {"deployedAt": datetime.now().astimezone().isoformat(),
                "source": str(source.relative_to(ROOT)), "backup": str(backup.relative_to(ROOT)),
                "sha256": digest(source / "best.onnx"), "annotationTarget": "full_handset_body",
                "independent_test_available": False, "validation": str((PROJECT / "evaluation.json").relative_to(ROOT))}
    for target in targets:
        for name in ["best.pt", "best.onnx", "model_info.json", "training_result.json"]:
            shutil.copy2(source / name, target / name)
        write_json(target / "deployed_model.json", deployed)
        assert digest(target / "best.onnx") == deployed["sha256"]
    # Keep the annotation tool's root project coherent with the new class IDs.
    dataset_source = (ROOT / "dataset").resolve()
    preserved = (ROOT / "imports/full-phone-before-20261019/dataset").resolve()
    assert dataset_source.parent == ROOT and preserved.is_relative_to(ROOT / "imports")
    assert not preserved.exists()
    preserved.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(dataset_source), str(preserved))
    shutil.copytree(PROJECT / "dataset", ROOT / "dataset")
    yaml_file = ROOT / "dataset/data.yaml"
    yaml_file.write_text((PROJECT / "dataset/data.yaml").read_text(encoding="utf-8").replace(
        (PROJECT / "dataset").as_posix(), (ROOT / "dataset").as_posix()), encoding="utf-8")
    shutil.copy2(PROJECT / "classes.txt", ROOT / "classes.txt")
    for label in (PROJECT / "labels").glob("*.txt"):
        shutil.copy2(label, ROOT / "labels" / label.name)
    # The existing _2 file is a byte-identical original; keep it coherent but
    # continue excluding it from the deduplicated training dataset.
    duplicate = ROOT / "images/WIN_20261017_10_57_11_Pro_2.jpg"
    if duplicate.is_file():
        assert digest(duplicate) == digest(ROOT / "images/WIN_20261017_10_57_11_Pro.jpg")
        shutil.copy2(PROJECT / "labels/WIN_20261017_10_57_11_Pro.txt", ROOT / "labels/WIN_20261017_10_57_11_Pro_2.txt")
    write_json(PROJECT / "deployment.json", deployed)
    print(json.dumps(deployed, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["prepare", "train", "export", "evaluate", "deploy"])
    parser.add_argument("--epochs", type=int, default=100)
    parser.add_argument("--phone-only", action="store_true")
    args = parser.parse_args()
    if args.phone_only:
        PROJECT = ROOT / "training/full_phone_detector_20261019"
        NAMES = ["phone"]
        ANNOTATIONS = [(stem, 0, box, split) for stem, _, box, split in ANNOTATIONS]
    if args.action == "train":
        train(args.epochs, args.phone_only)
    elif args.action == "export":
        export_model(args.epochs)
    else:
        globals()[args.action]()
