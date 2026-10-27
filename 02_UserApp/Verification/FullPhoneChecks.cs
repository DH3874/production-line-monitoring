using System.Text.Json;
using ProductionLine.Shared;
using ProductionLine.UserApp;
using ProductionLine.Vision;

namespace ProductionLine.Verification;

internal static class FullPhoneChecks
{
    public static void Run(string projectRoot)
    {
        var root = Path.GetFullPath(projectRoot);
        const string trainingFolder = "full_phone_detector_20261019";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "training", trainingFolder, "manifest.json")));
        using var inspector = new YoloOnnxInspector(Path.Combine(root, "04_Vision", "models", "best.onnx"), 0.45f);
        var expectedClasses = new[] { "target_iphone", "other_phone", "crack", "crack_phone" };
        if (!inspector.CanClassifyProducts || !inspector.ClassNames.SequenceEqual(expectedClasses))
            throw new InvalidOperationException("Imported model classes or quality policy are not active.");
        var passed = 0;
        foreach (var record in manifest.RootElement.GetProperty("records").EnumerateArray())
        {
            var file = record.GetProperty("file").GetString()!;
            var coordinates = record.GetProperty("box_xyxy").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var expected = new RectangleF(coordinates[0], coordinates[1], coordinates[2] - coordinates[0], coordinates[3] - coordinates[1]);
            var analysis = inspector.Inspect(File.ReadAllBytes(Path.Combine(root, "training", trainingFolder, "images", file)));
            var detection = analysis.Detections.Where(value => YoloOnnxInspector.IsPhoneClass(value.ClassName))
                .MaxBy(value => IoU(value.Box, expected));
            if (!analysis.ObjectFound || analysis.Result != InspectionResultType.Defect ||
                detection == null || IoU(detection.Box, expected) < 0.5f)
                throw new InvalidOperationException($"C# full-body ONNX failed for {file}");
            Console.WriteLine($"PASS: {file}: {detection.ClassName} {detection.Confidence:P1}, box {detection.Box.Width:F0}x{detection.Box.Height:F0}px, IoU {IoU(detection.Box, expected):F3}");
            passed++;
        }
        var size = new Size(1920, 1080);
        if (UserMainForm.IsPhoneFullyInsideFrame(new RectangleF(0, 100, 300, 500), size) ||
            UserMainForm.IsPhoneFullyInsideFrame(new RectangleF(1700, 100, 220, 500), size) ||
            !UserMainForm.IsPhoneFullyInsideFrame(new RectangleF(200, 150, 1500, 750), size))
            throw new InvalidOperationException("Edge-clipped phones must wait before recording.");
        Console.WriteLine($"FULL PHONE C# CHECKS PASSED: {passed} photos + edge entry checks. Same-session diagnostic only.");
    }

    private static float IoU(RectangleF first, RectangleF second)
    {
        var intersection = RectangleF.Intersect(first, second);
        var intersectionArea = Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
        var union = first.Width * first.Height + second.Width * second.Height - intersectionArea;
        return union <= 0 ? 0 : intersectionArea / union;
    }
}
