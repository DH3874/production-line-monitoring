using ProductionLine.Shared;

namespace _01_AdminApp.Services;

// Display-only examples for the prototype's virtual lines. Never save as real inspections.
internal static class VirtualInspectionSamples
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Now;
    public static List<InspectionDto> Get(int lineId)
    {
        if (lineId is < 2 or > 4) return new();
        var results = new[] { InspectionResultType.Normal, InspectionResultType.Normal,
            InspectionResultType.Defect, InspectionResultType.Normal, InspectionResultType.Undetermined };
        var reasons = new[] { "iphone14_white · 정상", "iphone16_white · 정상",
            "깨진 휴대폰 · 파손 예시", "iphone12_pro_silver · 정상", "영상 흐림 · 판정 불가 예시" };
        return Enumerable.Range(0,5).Select(i => new InspectionDto(
            new Guid($"{lineId:X8}-0000-4000-8000-{i+1:X12}"), lineId, results[i],
            "[가상 예시] " + reasons[i], CreatedAt.AddMinutes(-(lineId+i*2)), HasImage:true)).ToList();
    }

    public static bool IsSample(Guid id) => FindImage(id) != null;

    private static string? FindImage(Guid id)
    {
        var files = new[] { "iphone14_white.jpg", "iphone16_white.jpg", "crack_phone.jpg", "iphone12pro_silver.png", "iphone14_white.jpg" };
        for (var line = 2; line <= 4; line++)
            for (var index = 0; index < files.Length; index++)
                if (id == new Guid($"{line:X8}-0000-4000-8000-{index+1:X12}")) return files[index];
        return null;
    }

    public static byte[]? GetImage(Guid id)
    {
        var file = FindImage(id);
        if (file == null) return null;
        using var stream = typeof(VirtualInspectionSamples).Assembly.GetManifestResourceStream("VirtualInspections." + file)
            ?? throw new FileNotFoundException("가상 예시 사진 리소스가 없습니다.", file);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
