using _01_AdminApp.Services;
using ProductionLine.Shared;

namespace _01_AdminApp;

// One saved inspection ID per preview; late responses never replace a newer selection.
internal sealed class InspectionPhotoPanel : UserControl
{
    private readonly ILineDetailService _detail;
    private readonly PictureBox _image = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(20,30,42) };
    private readonly Label _caption = new() { Dock = DockStyle.Fill, Text = "목록에서 검사 기록을 선택하면 사진이 표시됩니다.", AutoEllipsis = true, Padding = new Padding(6) };
    private CancellationTokenSource? _request;
    private int _generation;
    private (Guid Id, bool HasImage)? _selection;

    public InspectionPhotoPanel(ILineDetailService detail)
    {
        _detail = detail;
        Dock = DockStyle.Fill; Margin = Padding.Empty;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_caption,0,0); layout.Controls.Add(_image,0,1); Controls.Add(layout);
    }

    public async Task ShowInspectionAsync(InspectionDto? inspection)
    {
        if (IsDisposed || Disposing) return;
        var selected = inspection == null ? ((Guid Id, bool HasImage)?)null : (inspection.Id,inspection.HasImage);
        if (selected.HasValue && _selection == selected) return;
        _request?.Cancel();
        var generation = ++_generation;
        _selection = selected;
        ReplaceImage(null);
        if (inspection == null)
        { _caption.Text = "목록에서 검사 기록을 선택하면 사진이 표시됩니다."; return; }
        var result = inspection.Result switch { InspectionResultType.Normal => "정상", InspectionResultType.Defect => "불량", _ => "판정 불가" };
        var sample = VirtualInspectionSamples.IsSample(inspection.Id) && inspection.Reason.StartsWith("[가상 예시] ", StringComparison.Ordinal);
        var title = $"{(sample ? "가상 예시 사진" : "검사 사진")} · LINE {inspection.LineId} · {result} · {inspection.InspectedAt.ToLocalTime():MM-dd HH:mm:ss}";
        if (!inspection.HasImage)
        { _caption.Text = title + "\r\n이 기록에는 저장된 사진이 없습니다."; return; }
        _caption.Text = title + "\r\n사진을 불러오는 중...";
        using var request = new CancellationTokenSource();
        _request = request;
        try
        {
            var bytes = await _detail.GetInspectionImageAsync(inspection.Id,request.Token);
            if (IsDisposed || Disposing || generation != _generation) return;
            using var stream = new MemoryStream(bytes);
            using var original = Image.FromStream(stream);
            ReplaceImage(new Bitmap(original));
            _caption.Text = title + (sample ? "\r\n시연용 참고 사진 · 실제 검사 촬영·판정 결과가 아닙니다." : "\r\n선택한 검사 당시 저장된 결과 사진");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing && generation == _generation)
            { _selection = null; _caption.Text = title + "\r\n사진 조회 실패: " + ex.Message; }
        }
        finally { if (ReferenceEquals(_request,request)) _request = null; }
    }

    private void ReplaceImage(Image? next)
    { var previous = _image.Image; _image.Image = next; previous?.Dispose(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ++_generation; _request?.Cancel(); ReplaceImage(null); }
        base.Dispose(disposing);
    }
}
