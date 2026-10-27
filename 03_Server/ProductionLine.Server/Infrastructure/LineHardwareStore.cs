using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ProductionLine.Server.Data;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// Latest live state only. Never substitute simulated values for missing sensors.
public sealed class LineHardwareStore
{
    private readonly ConcurrentDictionary<int, LineHardwareStatusDto> _latest = new();
    private readonly object _historyGate = new();
    private readonly Dictionary<int, List<LineHardwareStatusDto>> _history = new();

    // DB 기록(LineHardwareLogs): 10초에 한 번 + 상태가 바뀐 순간 저장, 7일 지난 기록은 하루 한 번 삭제 (김하균)
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<LineHardwareStore> _logger;
    private readonly TimeSpan _logInterval;
    private readonly int _retentionDays;
    private readonly object _logGate = new();
    private readonly Dictionary<int, (DateTimeOffset At, LineHardwareStatusDto Status)> _lastLogged = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    public LineHardwareStore(IDbContextFactory<AppDbContext> contextFactory, IConfiguration configuration,
        ILogger<LineHardwareStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _logInterval = TimeSpan.FromSeconds(configuration.GetValue("HardwareLogIntervalSeconds", 10));
        _retentionDays = configuration.GetValue("HardwareLogRetentionDays", 7);
    }

    public IReadOnlyList<LineHardwareStatusDto> History(int lineId)
    {
        lock (_historyGate) return _history.TryGetValue(lineId, out var samples) ? samples.ToArray() : Array.Empty<LineHardwareStatusDto>();
    }
    public LineStatusDto Attach(LineStatusDto line)
    {
        if (!line.IsReal)
        {
            // Virtual conveyors follow the representative line's actual state.
            // Never attach real sensor readings or counts to a virtual line.
            var linked = _latest.TryGetValue(1, out var main) && main.IsFresh;
            return line with
            {
                OperationStatus = !linked ? "대표 라인 통신 미확인" : main!.EmergencyStopped ? "긴급 정지" : main!.BeltRunning ? "가동" : "정지",
                InspectionStatus = "가상 데이터 · D12/벨트 상태 연동"
            };
        }
        if (!line.IsReal || !_latest.TryGetValue(line.Id, out var hardware)) return line;
        return line with
        {
            Hardware = hardware,
            OperationStatus = !hardware.IsFresh ? "장비 통신 미확인"
                : hardware.EmergencyStopped ? "긴급 정지" : hardware.BeltRunning ? "가동" : "정지"
        };
    }

    public LineHardwareStatusDto Update(int lineId, UpdateLineHardwareRequest value)
    {
        if (value.TemperatureCelsius is double t && (!double.IsFinite(t) || t < -40 || t > 125) ||
            value.HumidityPercent is double h && (!double.IsFinite(h) || h < 0 || h > 100) ||
            value.SorterAngle is < 0 or > 180 || value.GasAnalogValue is < 0 or > 1023 || value.EmergencyStopped && value.BeltRunning || value.SpeedBoosted && !value.BeltRunning)
            throw new ArgumentException("센서 값이나 구동 상태가 올바르지 않습니다.");
        var status = new LineHardwareStatusDto(value.Connected, value.TemperatureCelsius,
            value.HumidityPercent, value.GasDetected, value.EmergencyStopped, value.BeltRunning,
            value.SorterAngle, value.Sorting, DateTimeOffset.UtcNow, value.SpeedBoosted, value.GasAnalogValue, value.GasWarmingUp);
        _latest[lineId] = status;
        lock (_historyGate)
        {
            if (!_history.TryGetValue(lineId, out var samples)) _history[lineId] = samples = new();
            if (samples.Count == 0 || status.ReceivedAt - samples[^1].ReceivedAt >= TimeSpan.FromSeconds(2) || !status.Connected)
                samples.Add(status);
            samples.RemoveAll(s => status.ReceivedAt - s.ReceivedAt > TimeSpan.FromMinutes(10));
            if (samples.Count > 600) samples.RemoveRange(0, samples.Count - 600);
        }
        SaveLog(lineId, status);
        return status;
    }

    // 기록 저장에 실패해도 장비 상태 전송은 정상 처리되도록 예외를 밖으로 내보내지 않습니다.
    private void SaveLog(int lineId, LineHardwareStatusDto status)
    {
        string reason;
        bool cleanup;
        lock (_logGate)
        {
            var hasLast = _lastLogged.TryGetValue(lineId, out var last);
            var changed = hasLast && (last.Status.Connected != status.Connected
                || last.Status.EmergencyStopped != status.EmergencyStopped
                || last.Status.BeltRunning != status.BeltRunning
                || last.Status.SpeedBoosted != status.SpeedBoosted
                || last.Status.Sorting != status.Sorting
                || last.Status.GasDetected != status.GasDetected);
            if (hasLast && !changed && status.ReceivedAt - last.At < _logInterval) return;
            reason = changed ? "상태 변경" : "주기";
            _lastLogged[lineId] = (status.ReceivedAt, status);
            cleanup = status.ReceivedAt - _lastCleanup >= TimeSpan.FromDays(1);
            if (cleanup) _lastCleanup = status.ReceivedAt;
        }

        try
        {
            using var db = _contextFactory.CreateDbContext();
            db.LineHardwareLogs.Add(new LineHardwareLogEntity
            {
                LineId = lineId,
                RecordedAt = status.ReceivedAt,
                Reason = reason,
                Connected = status.Connected,
                TemperatureCelsius = status.TemperatureCelsius,
                HumidityPercent = status.HumidityPercent,
                GasDetected = status.GasDetected,
                GasAnalogValue = status.GasAnalogValue,
                GasWarmingUp = status.GasWarmingUp,
                EmergencyStopped = status.EmergencyStopped,
                BeltRunning = status.BeltRunning,
                SpeedBoosted = status.SpeedBoosted,
                SorterAngle = status.SorterAngle,
                Sorting = status.Sorting
            });
            db.SaveChanges();
            if (cleanup)
            {
                var cutoff = status.ReceivedAt.AddDays(-_retentionDays);
                var deleted = db.LineHardwareLogs.Where(x => x.RecordedAt < cutoff).ExecuteDelete();
                if (deleted > 0) _logger.LogInformation("{Days}일 지난 센서·벨트 기록 {Count}건을 지웠습니다.", _retentionDays, deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "센서·벨트 기록 저장 실패: 라인 {LineId}", lineId);
        }
    }
}
