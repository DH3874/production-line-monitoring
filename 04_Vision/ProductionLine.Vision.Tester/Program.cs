using ProductionLine.Vision;

if (args.FirstOrDefault() == "--all-models")
{
    CombinedModelChecks.Run(args[1]);
    return;
}

// 사진 폴더를 앱과 같은 방식(전체 학습 사용)으로 판정하고 1장당 처리 시간을 잽니다.
// 사용법: ProductionLine.Vision.Tester.exe --batch <사진 폴더> [신뢰도 기준] [모델 경로(생략 시 전체 학습 사용)]
if (args.FirstOrDefault() == "--batch")
{
    var batchThreshold = args.Length > 2 && float.TryParse(args[2], out var t) ? t : 0.45f;
    using var batchInspector = args.Length > 3 ? new YoloOnnxInspector(args[3], batchThreshold)
        : YoloOnnxInspector.CreateCombined(batchThreshold);
    var files = Directory.EnumerateFiles(args[1])
        .Where(f => new[] { ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
        .OrderBy(f => f, StringComparer.Ordinal).ToArray();
    Console.WriteLine($"모델 {batchInspector.LoadedModelPaths.Count}개 · {batchInspector.CapabilityDescription}");
    batchInspector.Inspect(await File.ReadAllBytesAsync(files[0]));  // 첫 실행 준비 시간은 측정에서 제외
    var times = new List<double>();
    foreach (var file in files)
    {
        var bytes = await File.ReadAllBytesAsync(file);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = batchInspector.Inspect(bytes);
        watch.Stop();
        times.Add(watch.Elapsed.TotalMilliseconds);
        var names = string.Join(", ", result.Detections
            .Select(d => $"{d.ClassName} {d.Confidence:0.00} w{d.Box.Width:F0}"));
        Console.WriteLine($"{Path.GetFileName(file),-34} {result.Result,-13} {watch.Elapsed.TotalMilliseconds,6:F0}ms  [{names}]");
    }
    Console.WriteLine($"평균 {times.Average():F0}ms / 최대 {times.Max():F0}ms ({files.Length}장)");
    return;
}

var defaultImagePath = Path.Combine(
    AppContext.BaseDirectory,
    "samples",
    "iphone",
    "iphone_sample.jpg");
var defaultModelPath = Path.Combine(
    AppContext.BaseDirectory,
    "Models",
    "best.onnx");

var imagePath = args.Length > 0 ? Path.GetFullPath(args[0]) : defaultImagePath;
var modelPath = args.Length > 1 ? Path.GetFullPath(args[1]) : defaultModelPath;
var threshold = args.Length > 2 && float.TryParse(args[2], out var parsedThreshold)
    ? parsedThreshold
    : 0.5f;

if (!File.Exists(imagePath))
{
    Console.WriteLine($"이미지 파일을 찾을 수 없습니다: {imagePath}");
    Console.WriteLine("사용법: ProductionLine.Vision.Tester.exe [사진 경로] [best.onnx 경로] [신뢰도 기준]");
    return;
}

if (!File.Exists(modelPath))
{
    Console.WriteLine($"ONNX 모델을 찾을 수 없습니다: {modelPath}");
    return;
}

using var inspector = new YoloOnnxInspector(modelPath, threshold);
var analysis = inspector.Inspect(await File.ReadAllBytesAsync(imagePath));

var outputDirectory = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..",
    "output"));
Directory.CreateDirectory(outputDirectory);
var resultPath = Path.Combine(outputDirectory, "iphone_result.jpg");
if (analysis.ResultImage.Length > 0)
    await File.WriteAllBytesAsync(resultPath, analysis.ResultImage);

Console.WriteLine("========== 휴대폰 YOLO 검출 결과 ==========");
Console.WriteLine($"모델: {modelPath}");
Console.WriteLine($"사진: {imagePath}");
Console.WriteLine($"신뢰도 기준: {threshold:P0}");
Console.WriteLine($"제품 검출: {(analysis.ObjectFound ? "성공" : "실패")}");
Console.WriteLine($"최종 판정: {analysis.Result}");
Console.WriteLine($"판정 사유: {analysis.Reason}");
foreach (var detection in analysis.Detections)
{
    Console.WriteLine(
        $"- {detection.ClassName} {detection.Confidence:P1} " +
        $"X={detection.Box.X:F0}, Y={detection.Box.Y:F0}, W={detection.Box.Width:F0}, H={detection.Box.Height:F0}");
}
Console.WriteLine($"검출 결과 이미지: {resultPath}");
