using System.Drawing.Drawing2D;
using ProductionLine.Shared;

namespace ProductionLine.ChatUI;

// Shared by operator and administrator; MQ9 AO is raw 0..1023, never invented ppm.
public sealed class SensorTrendPanel : Control
{
    private readonly List<LineHardwareStatusDto> _samples = new();
    private readonly System.Windows.Forms.Timer _age = new() { Interval = 1000 };
    public int SampleCount => _samples.Count;
    private bool _dark;
    // 관리자 앱 어두운 테마용. 기본값(false)은 작업자 앱의 밝은 그래프 그대로입니다.
    public bool DarkMode
    {
        get => _dark;
        set { _dark = value; BackColor = value ? Color.FromArgb(20, 34, 54) : Color.White; ForeColor = value ? Color.FromArgb(230, 237, 245) : Color.FromArgb(25, 45, 65); Invalidate(); }
    }
    private Color MutedText => _dark ? Color.FromArgb(143, 162, 186) : Color.DimGray;
    private Color WarnText => _dark ? Color.FromArgb(255, 107, 107) : Color.Firebrick;
    public SensorTrendPanel()
    {
        DoubleBuffered = true; BackColor = Color.White; ForeColor = Color.FromArgb(25,45,65);
        Font = new Font("맑은 고딕", 9); MinimumSize = new Size(240, 240);
        AccessibleName = "최근 10분 온도·습도·가스 감지 그래프";
        _age.Tick += (_, _) => Invalidate(); _age.Start();
    }
    public void Append(LineHardwareStatusDto sample)
    {
        if (_samples.Count > 0 && sample.ReceivedAt <= _samples[^1].ReceivedAt) return;
        if (_samples.Count > 0 && sample.ReceivedAt - _samples[^1].ReceivedAt < TimeSpan.FromSeconds(1.8) && sample.Connected) return;
        _samples.Add(sample); Trim(); Invalidate();
    }
    public void SetSamples(IEnumerable<LineHardwareStatusDto> samples)
    {
        _samples.Clear(); _samples.AddRange(samples.OrderBy(s => s.ReceivedAt)); Trim(); Invalidate();
    }
    public void MergeSamples(IEnumerable<LineHardwareStatusDto> samples)
    {
        var combined = samples.Concat(_samples).DistinctBy(s => s.ReceivedAt).OrderBy(s => s.ReceivedAt).ToArray();
        SetSamples(combined);
    }
    private void Trim()
    {
        var limit = DateTimeOffset.UtcNow.AddMinutes(-10);
        _samples.RemoveAll(s => s.ReceivedAt < limit);
        if (_samples.Count > 600) _samples.RemoveRange(0, _samples.Count - 600);
    }
    protected override void Dispose(bool disposing) { if (disposing) _age.Dispose(); base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var now = DateTimeOffset.UtcNow;
        var current = _samples.LastOrDefault();
        var fresh = current?.IsFresh == true;
        var analogGas = current?.GasAnalogValue != null || _samples.LastOrDefault(s => s.Connected)?.GasAnalogValue != null;
        using var heading = new Font(Font.FontFamily, 10, FontStyle.Bold);
        TextRenderer.DrawText(g, "환경 센서 · 최근 10분", heading, new Rectangle(10,8,Width-20,24), ForeColor);
        var dhtMissing = fresh && current?.TemperatureCelsius == null && current?.HumidityPercent == null;
        var sensorMode = dhtMissing ? "DHT11 D30 미응답 · DATA/VCC/GND 배선 확인"
            : analogGas ? current?.GasWarmingUp == true ? "A0 원시값 · 초기 예열 중" : "가스 A0 입력 (0~1023) · ppm 아님" : "실측값 · 가스는 DO 감지 신호";
        TextRenderer.DrawText(g, fresh ? sensorMode : "센서 미연결 / 수신 지연 · 현재 값 미확인", Font,
            new Rectangle(10,32,Width-20,22), fresh ? MutedText : WarnText, TextFormatFlags.EndEllipsis);
        var row = Math.Max(58, (Height - 78) / 3);
        DrawTrace(g, new Rectangle(12,58,Width-24,row), "온도", "℃", Color.FromArgb(227,127,45), s => s.TemperatureCelsius, false, now, fresh);
        DrawTrace(g, new Rectangle(12,58+row,Width-24,row), "습도", "%", Color.FromArgb(32,137,191), s => s.HumidityPercent, false, now, fresh);
        if (analogGas)
            DrawTrace(g,new Rectangle(12,58+row*2,Width-24,row),"가스 A0","ADC",Color.FromArgb(190,61,77),s=>s.GasAnalogValue,false,now,fresh);
        else DrawTrace(g, new Rectangle(12,58+row*2,Width-24,row), "가스", "DO", Color.FromArgb(190,61,77), s => s.GasDetected.HasValue ? s.GasDetected.Value ? 1 : 0 : null, true, now, fresh);
        TextRenderer.DrawText(g, "10분 전                         현재", Font, new Rectangle(40, Height-20,Width-50,18), MutedText, TextFormatFlags.HorizontalCenter);
    }
    private void DrawTrace(Graphics g, Rectangle area, string title, string unit, Color color,
        Func<LineHardwareStatusDto,double?> value, bool digital, DateTimeOffset now, bool fresh)
    {
        var latest = fresh && _samples.Count > 0 ? value(_samples[^1]) : null;
        var label = latest.HasValue ? digital ? latest.Value > .5 ? "감지 (1)" : "미감지 (0)" : unit == "ADC" ? $"{latest:0}/1023" : $"{latest:F1}{unit}" : "--";
        TextRenderer.DrawText(g, $"{title}  {label}", Font, new Rectangle(area.Left,area.Top,area.Width,22), color, TextFormatFlags.EndEllipsis);
        var plot = new Rectangle(area.Left+32,area.Top+25,Math.Max(20,area.Width-40),Math.Max(20,area.Height-37));
        var values = _samples.Where(s => s.Connected && now-s.ReceivedAt <= TimeSpan.FromMinutes(10)).Select(value).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        double min = digital ? -.1 : unit is "%" or "ADC" ? 0 : values.Length == 0 ? 0 : Math.Floor(values.Min()-2);
        double max = digital ? 1.1 : unit == "ADC" ? 1023 : unit == "%" ? 100 : values.Length == 0 ? 40 : Math.Ceiling(values.Max()+2);
        if (max-min < 1) max=min+1;
        using var grid = new Pen(_dark ? Color.FromArgb(43,62,88) : Color.FromArgb(230,235,240));
        for (int i=0;i<3;i++) { float y=plot.Top+plot.Height*i/2f; g.DrawLine(grid,plot.Left,y,plot.Right,y); }
        TextRenderer.DrawText(g,digital ? "1" : $"{max:0}",Font,new Rectangle(area.Left,plot.Top-5,32,18),MutedText);
        TextRenderer.DrawText(g,digital ? "0" : $"{min:0}",Font,new Rectangle(area.Left,plot.Bottom-12,32,18),MutedText);
        using var pen = new Pen(color,2);
        PointF? previous=null; DateTimeOffset previousTime=default;
        foreach (var s in _samples)
        {
            var v=s.Connected ? value(s) : null;
            if (!v.HasValue || s.ReceivedAt < now.AddMinutes(-10)) { previous=null; continue; }
            var p = new PointF(plot.Left+(float)((s.ReceivedAt-now.AddMinutes(-10)).TotalSeconds/600)*plot.Width,
                plot.Bottom-(float)((v.Value-min)/(max-min))*plot.Height);
            if (previous is PointF last && s.ReceivedAt-previousTime < TimeSpan.FromSeconds(6))
            {
                if (digital) { g.DrawLine(pen,last,new PointF(p.X,last.Y)); g.DrawLine(pen,new PointF(p.X,last.Y),p); }
                else g.DrawLine(pen,last,p);
            }
            using var brush = new SolidBrush(color); g.FillEllipse(brush,p.X-2,p.Y-2,4,4);
            previous=p; previousTime=s.ReceivedAt;
        }
    }
}
