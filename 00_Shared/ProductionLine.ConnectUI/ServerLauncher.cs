using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace ProductionLine.ConnectUI;

// [서버 열기] 관리자 앱이 이 PC에서 서버 프로그램을 대신 켜 줌 (김하균 추가, 2026-10-21)
// 서버 프로그램 찾는 순서
//  1) 앱 폴더 옆 Server\ProductionLine.Server.exe  ← 다른 PC에 나눠 줄 때 이렇게 넣음
//  2) 개발 폴더 03_Server\ProductionLine.Server\bin\(Debug|Release)\net8.0\ProductionLine.Server.exe  ← Visual Studio로 실행할 때
internal static class ServerLauncher
{
    private const string ExeName = "ProductionLine.Server.exe";

    public static string? FindServerExe()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Server", ExeName);
        if (File.Exists(bundled)) return bundled;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(dir.FullName, "03_Server", "ProductionLine.Server", "bin", config, "net8.0", ExeName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    // 개발 폴더에서 켜면 Visual Studio로 켤 때와 같은 DB(프로젝트 폴더\App_Data)를 쓰도록 프로젝트 폴더를 기준 폴더로
    public static string ContentRootFor(string exePath)
    {
        var exeDir = Path.GetDirectoryName(exePath)!;
        var parts = exeDir.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
        if (parts.Length >= 3 && parts[^3].Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            var project = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
            if (File.Exists(Path.Combine(project, "ProductionLine.Server.csproj"))) return project;
        }
        return exeDir;
    }

    public static Process Start(string exePath)
    {
        var root = ContentRootFor(exePath);
        var start = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = root,
            UseShellExecute = true, // 서버 창(검은 창)을 따로 띄워서 접속 주소가 보이게
        };
        start.ArgumentList.Add("--contentRoot");
        start.ArgumentList.Add(root);
        return Process.Start(start) ?? throw new InvalidOperationException("서버 프로그램을 시작하지 못했습니다.");
    }

    // 주소에서 생산라인 서버가 응답하는지 확인 (http://주소:포트/ → service = ProductionLine.Server)
    public static async Task<bool> IsServerAliveAsync(string url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await http.GetAsync(url.TrimEnd('/') + "/", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.TryGetProperty("service", out var name) && name.GetString() == "ProductionLine.Server";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
