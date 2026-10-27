using OpenCvSharp;
using ProductionLine.Shared;
using ProductionLine.Vision;

namespace ProductionLine.Verification;

internal static class PhonePackageChecks
{
    public static void Run(string importedProject)
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }

        var legacy = Path.Combine(AppContext.BaseDirectory, "Models", "alternatives", "02_기종검출_카메라영역", "best.onnx");
        var active = File.Exists(legacy) ? legacy : Path.Combine(AppContext.BaseDirectory, "Models", "best.onnx");
        using var six = new YoloOnnxInspector(active, 0.5f);
        using var other = new YoloOnnxInspector(Path.Combine(AppContext.BaseDirectory,
            "Models", "alternatives", "01_기존_3클래스", "best.onnx"), 0.5f);
        using var previous = new YoloOnnxInspector(Path.Combine(AppContext.BaseDirectory,
            "Models", "alternatives", "05_이전_휴대폰위치검출", "best.onnx"), 0.5f);
        Check(six.ClassNames.SequenceEqual(new[] { "target_iphone", "other_phone", "crack", "iphone_16", "iphone_15", "iphone_15_pro" }),
            "six-class names and IDs come from ONNX metadata");
        Check(six.HasPhoneModelClasses && !six.CanClassifyProducts, "new model reports model names without an unverified quality judgment");
        Check(other.ClassNames.Count == 3 && !other.CanClassifyProducts, "additional model retains its own disabled quality policy");
        Check(previous.ClassNames.SequenceEqual(new[] { "phone" }) && !previous.CanClassifyProducts,
            "previous phone model remains selectable");

        var expected = new Dictionary<string, string?>
        {
            ["KakaoTalk_20261017_111004349.jpg"] = null,
            ["KakaoTalk_20261017_111004349_01.jpg"] = YoloOnnxInspector.Iphone16,
            ["KakaoTalk_20261017_111004349_02.jpg"] = YoloOnnxInspector.Iphone16,
            ["KakaoTalk_20261017_111004349_03.jpg"] = YoloOnnxInspector.Iphone15Pro
        };
        var output = Path.Combine(AppContext.BaseDirectory, "phone-package-previews");
        Directory.CreateDirectory(output);
        foreach (var (file, phoneClass) in expected)
        {
            var bytes = File.ReadAllBytes(Path.Combine(importedProject, "images", file));
            var result = six.Inspect(bytes);
            Check(result.ObjectFound == (phoneClass != null) && result.Result == InspectionResultType.Undetermined,
                $"six-class real image {file}: detection and quality state");
            if (phoneClass != null)
            {
                Check(result.Detections.Any(value => value.ClassName == phoneClass && value.Confidence >= .5f),
                    $"expected phone class {phoneClass} is returned above threshold");
                Check(result.Reason.Contains(phoneClass), "detected model name survives into the stored reason");
            }
            File.WriteAllBytes(Path.Combine(output, file), result.ResultImage);
            var alternative = other.Inspect(bytes);
            Check(alternative.ObjectFound && alternative.Result == InspectionResultType.Undetermined,
                $"additional model detects {file} without claiming normal quality");
        }
        using var blank = new Mat(720, 1280, MatType.CV_8UC3, Scalar.Black);
        var empty = blank.ToBytes(".jpg");
        Check(!six.Inspect(empty).ObjectFound && !other.Inspect(empty).ObjectFound,
            "empty conveyor frame produces no phone detection in either imported model");
        foreach (var name in new[] { YoloOnnxInspector.Phone, YoloOnnxInspector.TargetIphone,
                     YoloOnnxInspector.OtherPhone, YoloOnnxInspector.Iphone16,
                     YoloOnnxInspector.Iphone15, YoloOnnxInspector.Iphone15Pro,
                     YoloOnnxInspector.CrackPhone })
            Check(YoloOnnxInspector.IsPhoneClass(name), $"conveyor stop recognizes phone class {name}");
        Check(!YoloOnnxInspector.IsPhoneClass(YoloOnnxInspector.Crack), "crack-only box is not a phone arrival event");
        Check(YoloOnnxInspector.IsPhoneClass(YoloOnnxInspector.Iphone14) &&
              YoloOnnxInspector.IsPhoneClass(YoloOnnxInspector.Iphone8) &&
              YoloOnnxInspector.IsPhoneClass(YoloOnnxInspector.Iphone12Pro) &&
              YoloOnnxInspector.IsPhoneClass(YoloOnnxInspector.Iphone12ProMax),
            "requested iPhone model classes are accepted as phone detections");
        Console.WriteLine($"PHONE PACKAGE CHECKS PASSED: {checks}");
    }
}
