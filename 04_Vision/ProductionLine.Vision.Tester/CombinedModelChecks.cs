using OpenCvSharp;
using ProductionLine.Shared;
using ProductionLine.Vision;
using System.Diagnostics;
using System.Security.Cryptography;

internal static class CombinedModelChecks
{
    public static void Run(string imagePath)
    {
        var checks = 0;
        void Check(bool value, string text)
        {
            if (!value) throw new InvalidOperationException(text);
            checks++; Console.WriteLine("PASS: " + text);
        }
        var bytes = File.ReadAllBytes(imagePath);
        using var combined = YoloOnnxInspector.CreateCombined();
        Check(combined.LoadedModelPaths.Count == 13, "all thirteen unique trained models are loaded");
        var hashes = combined.LoadedModelPaths.Select(path => { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }).ToList();
        Check(hashes.Distinct().Count() == 13, "duplicate file copies do not execute same learned weights twice");
        foreach (var hash in new[] {
            "C3BD3BAF04CF5E77B0DC0016B6E7ECFCCF07196FB6F7C748A40EB36C63F6022D",
            "4981E78C5656E24565C7BBC96630511E0C83A5A35DC95F69095ABF3D30F5D4ED",
            "2B7C63F6F6778F460639B9DD6B15D80C231A26FBE659BD16D132391A726D6DE1",
            "548ED8C6C60E9F16757A02FFEE1A8EFB2AD46E80600A2A7A644A0B9E34C94FB7" })
            Check(hashes.Contains(hash), "new 20261020 trained weights loaded: " + hash[..8]);
        Check(hashes.Contains("C84FD688EE00A1FEB51BBE8344CFA31B2F56A4844F17E2D0B7A962484611A37B"), "new best (1).onnx is part of runtime detection");
        foreach (var name in new[] { "phone", "crack_phone", "crack", "iphone_16", "iphone_15", "iphone_15_pro",
            "iphone_12pro_graphite", "iphone_12pro_pacificblue", "iphone_12pro_silver",
            "iphone_14_white", "iphone16_white", "iphone_12_pro_max" })
            Check(combined.ClassNames.Contains(name), "retained learned class: " + name);
        var classified = new List<YoloInspectionAnalysis>();
        foreach (var path in combined.LoadedModelPaths)
        {
            using var model = new YoloOnnxInspector(path, .45f);
            var result = model.Inspect(bytes);
            Check(result.ResultImage.Length > 0, "ONNX input/output and image processing compatible: " + Path.GetFileName(Path.GetDirectoryName(path)));
            if (model.CanClassifyProducts && result.ObjectFound) classified.Add(result);
            Console.WriteLine("MODEL: " + Path.GetFileName(Path.GetDirectoryName(path)) + " | " + string.Join(", ", model.ClassNames));
        }
        var timer = Stopwatch.StartNew();
        var analysis = combined.Inspect(bytes);
        timer.Stop();
        Console.WriteLine("COMBINED_PROCESSING_MS=" + timer.ElapsedMilliseconds);
        Console.WriteLine($"COMBINED_RESULT={analysis.Result}; REASON={analysis.Reason}");
        Check(analysis.ObjectFound && analysis.Detections.Any(x => YoloOnnxInspector.IsPhoneClass(x.ClassName)), "combined models find sample phone");
        Check(analysis.Detections.All(x => combined.ClassNames[x.ClassId] == x.ClassName), "combined class IDs remain mapped to correct class names");
        Check(analysis.Result != InspectionResultType.Undetermined,
            "combined phone model and visible-surface check produce a final quality result");
        if (imagePath.Contains("iphone_12_pro_max_Crack", StringComparison.OrdinalIgnoreCase))
        {
            Check(analysis.Result == InspectionResultType.Defect, "new damaged-phone sample is classified as defective");
            Check(analysis.Detections.Any(x => x.ClassName == "crack_phone"), "new damage class maps to crack_phone");
            Check(analysis.Detections.All(x => x.ClassName != "iphone_12_pro_max_Crack"), "raw damage class cannot appear as a normal model");
        }
        using var empty = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        Check(!combined.Inspect(empty.ToBytes(".jpg")).ObjectFound, "empty frame does not become phone result in combined mode");
        var output = Path.Combine(AppContext.BaseDirectory, "combined-model-verification");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "all-models-result.jpg"), analysis.ResultImage);
        Console.WriteLine($"ALL MODEL CHECKS PASSED: {checks}; classes={string.Join(", ", combined.ClassNames)}");
    }
}
