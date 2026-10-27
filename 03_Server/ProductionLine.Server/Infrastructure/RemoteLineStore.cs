using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// Only allowlisted application actions; no shell, arbitrary files, or remote firmware upload.
public sealed class RemoteLineStore
{
    private sealed class Line
    {
        public WorkerConnectionDto? Worker;
        public List<RemoteCommandDto> Commands = new();
    }
    private readonly object _gate = new();
    private readonly Dictionary<int, Line> _lines = new();
    private readonly Func<DateTimeOffset> _now;
    public RemoteLineStore() : this(() => DateTimeOffset.UtcNow) { }
    public RemoteLineStore(Func<DateTimeOffset> now) => _now = now;
    private Line Get(int id) => _lines.TryGetValue(id, out var line) ? line : _lines[id] = new();
    public LineStatusDto Attach(LineStatusDto status)
    {
        lock (_gate)
        {
            if (!status.IsReal || !_lines.TryGetValue(status.Id,out var line) || line.Worker == null) return status;
            return status with { InspectionStatus = !Online(line.Worker) ? "작업자 오프라인" : line.Worker.State.Inspecting ? "검사 중" : "검사 중지" };
        }
    }
    private bool Online(WorkerConnectionDto? worker) => worker != null && _now() - worker.ReceivedAt < TimeSpan.FromSeconds(6);
    private void Expire(Line line)
    {
        for (var i = 0; i < line.Commands.Count; i++)
        {
            var command = line.Commands[i];
            if (command.Status == RemoteCommandStatus.Pending && _now() >= command.ExpiresAt)
                line.Commands[i] = command with { Status = RemoteCommandStatus.Expired, Message = "전달 기한 만료 · 재연결 후 자동 실행하지 않음" };
            else if (command.Status == RemoteCommandStatus.Executing && _now() >= command.CreatedAt.AddSeconds(40))
                line.Commands[i] = command with { Status = RemoteCommandStatus.Unknown, Message = "실행 응답 미확인 · 자동 재실행하지 않음" };
        }
    }
    public RemoteLineSnapshotDto Snapshot(int lineId)
    {
        lock (_gate) { var line = Get(lineId); Expire(line); return new(line.Worker, line.Commands.AsEnumerable().Reverse().Take(100).ToArray()); }
    }
    public WorkerHeartbeatResponse Heartbeat(int lineId, string userId, WorkerHeartbeatRequest value)
    {
        ValidateState(value);
        lock (_gate)
        {
            var line = Get(lineId); Expire(line);
            if (Online(line.Worker) && (line.Worker!.ClientId != value.ClientId || line.Worker.UserId != userId))
                throw new InvalidOperationException("다른 작업자 프로그램이 이 라인을 제어 중입니다.");
            if (line.Worker?.ClientId != value.ClientId || !Online(line.Worker))
            {
                // Commands are bound to one uninterrupted worker connection.
                for (var i = 0; i < line.Commands.Count; i++)
                    if (line.Commands[i].Status == RemoteCommandStatus.Pending)
                        line.Commands[i] = line.Commands[i] with { Status = RemoteCommandStatus.Cancelled, Message = "작업자 연결 변경 · 재접속 시 실행하지 않음" };
            }
            line.Worker = new(value.ClientId, userId, value.State, _now());
            var next = line.Commands.FindIndex(c => c.ClientId == value.ClientId && c.Status == RemoteCommandStatus.Pending
                && (value.ReadyForCommand || c.Request.Action == RemoteAction.StopAll));
            if (next < 0) return new(null);
            var command = line.Commands[next] with { Status = RemoteCommandStatus.Executing, Message = "작업자에 전달됨 · 실행 응답 대기" };
            line.Commands[next] = command;
            return new(command); // Never redeliver an executing command, even when its HTTP reply is lost.
        }
    }
    public RemoteCommandDto Enqueue(int lineId, string admin, CreateRemoteCommandRequest request)
    {
        lock (_gate)
        {
            var line = Get(lineId); Expire(line);
            var existing = line.Commands.FirstOrDefault(c => c.Id == request.RequestId);
            if (existing != null)
            {
                if (existing.Request != request || existing.Administrator != admin) throw new ArgumentException("명령 ID가 다른 요청에 사용됐습니다.");
                return existing;
            }
            if (!Online(line.Worker) || line.Worker!.ClientId != request.ExpectedClientId)
                throw new InvalidOperationException("작업자 연결이 끊겼거나 바뀌었습니다. 상태를 새로 확인하세요.");
            ValidateCommand(request, line.Worker.State);
            if (request.Action is RemoteAction.StopAll or RemoteAction.StopConveyor)
            {
                for (var i = 0; i < line.Commands.Count; i++)
                    if (line.Commands[i].Status == RemoteCommandStatus.Pending)
                        line.Commands[i] = line.Commands[i] with { Status = RemoteCommandStatus.Cancelled, Message = "정지 명령으로 취소" };
            }
            if (line.Commands.Count(c => c.Status == RemoteCommandStatus.Pending) >= 8)
                throw new InvalidOperationException("대기 명령이 많습니다. 실행 결과를 확인하세요.");
            var command = new RemoteCommandDto(request.RequestId, lineId, request.ExpectedClientId, admin, request,
                RemoteCommandStatus.Pending, "작업자 전달 대기", _now(), _now().AddSeconds(10));
            line.Commands.Add(command);
            // Keep bounded audit history for this server session; pending/executing entries stay intact.
            while (line.Commands.Count > 200)
            {
                var index = line.Commands.FindIndex(c => c.Status is not RemoteCommandStatus.Pending and not RemoteCommandStatus.Executing);
                if (index < 0) break;
                line.Commands.RemoveAt(index);
            }
            return command;
        }
    }
    public RemoteCommandDto Complete(int lineId, string user, Guid id, CompleteRemoteCommandRequest result)
    {
        lock (_gate)
        {
            var line = Get(lineId);
            if (line.Worker?.ClientId != result.ClientId || line.Worker.UserId != user)
                throw new InvalidOperationException("명령을 수행한 작업자 연결과 일치하지 않습니다.");
            var index = line.Commands.FindIndex(c => c.Id == id && c.ClientId == result.ClientId);
            if (index < 0) throw new ArgumentException("명령을 찾을 수 없습니다.");
            var command = line.Commands[index];
            if (command.Status is RemoteCommandStatus.Succeeded or RemoteCommandStatus.Failed) return command;
            if (command.Status is not RemoteCommandStatus.Executing and not RemoteCommandStatus.Unknown)
                throw new InvalidOperationException("전달되지 않은 명령은 완료할 수 없습니다.");
            return line.Commands[index] = command with { Status = result.Succeeded ? RemoteCommandStatus.Succeeded : RemoteCommandStatus.Failed,
                Message = (result.Message ?? "")[..Math.Min(result.Message?.Length ?? 0, 600)], CompletedAt = _now() };
        }
    }
    private static void ValidateState(WorkerHeartbeatRequest request)
    {
        var s = request.State;
        if (request.ClientId == Guid.Empty || s == null || s.Ports == null || s.Models == null ||
            s.Ports.Length > 64 || s.Models.Length > 64 || s.Ports.Any(p => p == null || p.Length > 32) ||
            s.Models.Any(p => p == null || p.Length > 200) || s.MachineName?.Length > 100 || s.Status?.Length > 2000 ||
            s.Model == null || s.Model.Length > 200 || s.Port?.Length > 32 || s.CameraIndex is < 0 or > 9 ||
            s.BoostDurationMs is < 500 or > 60000 || s.Page is < 0 or > 5 || s.TravelMs is < 500 or > 60000 || s.HoldMs is < 200 or > 60000 ||
            s.LiveJpeg?.Length > 180000 || s.LastProductJpeg?.Length > 180000)
            throw new ArgumentException("작업자 상태 데이터가 올바르지 않습니다.");
    }
    private static void ValidateCommand(CreateRemoteCommandRequest r, WorkerStateDto s)
    {
        if (r.RequestId == Guid.Empty || !Enum.IsDefined(r.Action)) throw new ArgumentException("허용되지 않은 명령입니다.");
        if (r.Action == RemoteAction.ConnectCamera && r.CameraIndex is not (>= 0 and <= 9) ||
            r.Action == RemoteAction.ConnectConveyor && (r.Port == null || !s.Ports.Contains(r.Port)) ||
            r.Action == RemoteAction.SelectModel && (r.Model == null || !s.Models.Contains(r.Model)) ||
            r.Action == RemoteAction.SetLight && r.Enabled == null ||
            r.Action == RemoteAction.SetBoostDuration && r.BoostMs is not (>= 500 and <= 60000) ||
            r.Action == RemoteAction.SetSorting && (r.Enabled == null || r.TravelMs is not (>= 500 and <= 60000) || r.HoldMs is not (>= 200 and <= 60000)) ||
            r.Action == RemoteAction.ChangePage && r.Page is not (>= 0 and <= 5))
            throw new ArgumentException("명령 설정값이 올바르지 않습니다.");
    }
}
