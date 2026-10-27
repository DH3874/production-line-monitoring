using System.Diagnostics;
using ProductionLine.Shared;

namespace ProductionLine.UserApp;

/// <summary>기존 AI 채팅 화면 위에 붙이는 연결·응답 시간·장비 상태 영역.</summary>
public sealed class AiChatStatusPanel : UserControl
{
    private readonly ProductionLineApiClient _api;
    private readonly Func<int> _currentLine;
    private readonly Label _connection = new() { Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, Text = "서버 연결 확인 전 · AI 연결은 실제 응답 후 확인됩니다." };
    private readonly Label _reply = new() { Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, ForeColor = IndustrialPalette.Muted,
        Text = "응답 대기 · 질문을 입력하세요." };
    private readonly Button _check = new() { Text = "연결 확인", Dock = DockStyle.Fill };
    private readonly Button _devices = new() { Text = "장비 상태 확인", Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Stopwatch _elapsed = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _sending, _checking, _released;
    private int _revision;
    private bool Closed => _released || IsDisposed || Disposing;
    private static readonly Color Accent = Color.FromArgb(0, 158, 180);

    public AiChatStatusPanel(ProductionLineApiClient api, Func<int> currentLine)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(currentLine);
        _api = api; _currentLine = currentLine;
        Dock = DockStyle.Top; Height = 104; MinimumSize = new Size(360, 104);
        BackColor = IndustrialPalette.Surface; ForeColor = IndustrialPalette.Text; BorderStyle = BorderStyle.None;
        Padding = new Padding(18, 10, 12, 10);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = IndustrialPalette.Surface, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.Add(_connection, 0, 0); grid.Controls.Add(_reply, 0, 1);
        grid.Controls.Add(_check, 1, 0); grid.Controls.Add(_devices, 1, 1);
        foreach (var button in new[] { _check, _devices })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = IndustrialPalette.Border;
            button.BackColor = IndustrialPalette.SurfaceRaised;
            button.ForeColor = IndustrialPalette.Text;
            button.Margin = new Padding(6, 3, 0, 3);
        }
        Controls.Add(grid);
        _timer.Tick += (_, _) =>
        {
            if (_sending && !Closed) _reply.Text = $"답변 작성 중 · 경과 {_elapsed.Elapsed.TotalSeconds:F1}초";
        };
        _check.Click += async (_, _) => await CheckConnectionAsync();
        _devices.Click += (_, _) => ShowDevices();
    }

    /// <summary>서버 연결·설정 준비만 확인한다. 외부 AI를 시험 호출하지 않는다.</summary>
    public async Task CheckConnectionAsync()
    {
        if (Closed || _sending || _checking) return;
        _checking = true; _check.Enabled = false;
        var revision = _revision;
        _connection.Text = "서버 연결과 AI 설정을 확인하는 중...";
        _connection.ForeColor = IndustrialPalette.Muted;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var status = await AiConsultationApi.GetStatusAsync(_api, timeout.Token);
            if (Closed || revision != _revision) return;
            _connection.Text = status.AiConfigured
                ? $"서버 연결됨 · {status.Provider} 설정됨 · 외부 AI 응답은 질문 전송 시 확인"
                : "서버 연결됨 · 외부 AI 미설정/사용 중지 · 기본 매뉴얼 모드";
            _connection.ForeColor = status.AiConfigured ? Accent : IndustrialPalette.Muted;
        }
        catch (OperationCanceledException) when (Closed) { }
        catch (Exception ex)
        {
            if (Closed || revision != _revision) return;
            _connection.Text = ex is OperationCanceledException
                ? "서버 연결 확인 시간 초과"
                : "서버 연결 확인 실패 · " + ex.Message;
            _connection.ForeColor = Color.Firebrick;
        }
        finally
        {
            _checking = false;
            if (!Closed) _check.Enabled = !_sending;
        }
    }

    /// <summary>기존 요청을 이 함수로 감싸면 대기·성공·실패와 왕복 시간을 표시한다.
    /// 원본 응답은 그대로 반환하며, 오류도 호출자에게 전달한다.</summary>
    public async Task<AiConsultResponse> RunAsync(
        Func<CancellationToken, Task<AiConsultResponse>> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(send);
        ObjectDisposedException.ThrowIf(Closed, this);
        if (_sending) throw new InvalidOperationException("이미 AI 답변을 기다리고 있습니다.");
        _revision++; _sending = true; _check.Enabled = false; _devices.Enabled = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _elapsed.Restart(); _timer.Start();
        _connection.Text = "서버로 질문 전송 중 · 응답 대기";
        _connection.ForeColor = IndustrialPalette.Muted;
        _reply.Text = "답변 작성 중 · 경과 0.0초";
        try
        {
            var response = await send(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (Closed) throw new OperationCanceledException(linked.Token);
            _elapsed.Stop(); _timer.Stop();
            // 원본의 표식으로 판별한 후, 채팅 본문에 표시할 때만 표식을 지운다.
            var state = AiReplyStatus.FromResponse(response);
            _connection.Text = state.Kind switch
            {
                AiReplyKind.Success => "서버 연결됨 · 외부 AI 응답 성공",
                AiReplyKind.Fallback => "서버 연결됨 · 외부 AI 응답 미확인 · 기본 매뉴얼로 답변",
                _ => "서버 연결됨 · 매뉴얼/안전 안내 응답 · 외부 AI 연결 미확인"
            };
            _connection.ForeColor = state.Kind == AiReplyKind.Success ? Accent : IndustrialPalette.Muted;
            _reply.Text = $"{state.Label} · 응답 시간 {_elapsed.Elapsed.TotalSeconds:F1}초 · {DateTime.Now:HH:mm:ss}";
            return response;
        }
        catch (Exception ex)
        {
            _elapsed.Stop();
            if (!Closed)
            {
                _connection.Text = ex is OperationCanceledException ? "상담 요청 취소 또는 시간 초과" : "서버/상담 연결 실패 · " + ex.Message;
                _connection.ForeColor = Color.Firebrick;
                _reply.Text = $"응답 실패 · 경과 {_elapsed.Elapsed.TotalSeconds:F1}초 · 질문을 유지하고 다시 시도하세요.";
            }
            throw;
        }
        finally
        {
            _elapsed.Stop(); _sending = false;
            if (!Closed) { _timer.Stop(); _check.Enabled = !_checking; _devices.Enabled = true; }
        }
    }

    private void ShowDevices()
    {
        if (Closed) return;
        var line = _currentLine();
        if (line <= 0) { _reply.Text = "서버에서 라인을 불러온 뒤 장비 상태를 확인하세요."; return; }
        using var form = new DeviceDiagnosticsForm(_api, line);
        form.ShowDialog(FindForm());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_released)
        {
            _released = true; _timer.Dispose(); _lifetime.Cancel(); _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
