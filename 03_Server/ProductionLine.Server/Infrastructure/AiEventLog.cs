using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProductionLine.Server.Infrastructure;

public sealed class AiEventLog(IHostEnvironment environment)
{
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(environment.ContentRootPath, "data", "ai-events.jsonl");

    public void Record(string code)
    {
        if (!Regex.IsMatch(code, "^[a-z0-9_]{1,80}$")) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > 131072) File.WriteAllText(_path, string.Empty);
                File.AppendAllText(_path, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, code }) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 진단 파일 쓰기 실패가 상담 응답을 막지 않도록 한다.
            }
        }
    }
}
