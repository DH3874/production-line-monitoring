using ProductionLine.Shared;

namespace ProductionLine.UserApp;

public sealed partial class UserMainForm
{
    private readonly PictureBox _historyImage = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(20, 30, 42) };
    private readonly Label _historyImageCaption = new() { Dock = DockStyle.Fill, Text = "검사 기록을 선택하면 인식 제품 사진을 표시합니다." };
    private Dictionary<Guid, InspectionDto> _historyRecords = new();
    private CancellationTokenSource? _historyImageRequest;
    private Guid? _historyImageId;
    private int _historyImageGeneration;
    private bool _bindingHistory;
    private InspectionDto[] _displayedInspections = Array.Empty<InspectionDto>();

    private Control HistoryWithImage()
    {
        var panels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        panels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        panels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        var photo = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(10, 0, 0, 0) };
        photo.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        photo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        photo.Controls.Add(_historyImageCaption, 0, 0);
        photo.Controls.Add(_historyImage, 0, 1);
        panels.Controls.Add(GridWithDarkScrollBar(_history), 0, 0);
        panels.Controls.Add(photo, 1, 0);
        return panels;
    }

    private Guid? SelectedInspectionId() => _history.CurrentRow?.Cells["검사번호"].Value is Guid id ? id : null;

    private void BindInspectionHistory(IReadOnlyList<InspectionDto> inspections)
    {
        // Polling normally returns identical records. Rebinding them resets the grid's viewport.
        if (_displayedInspections.SequenceEqual(inspections)) return;
        var previous = SelectedInspectionId();
        var column = _history.CurrentCell?.OwningColumn.Name;
        int firstVisible = _history.FirstDisplayedScrollingRowIndex;
        Guid? firstVisibleId = firstVisible >= 0
            ? _history.Rows[firstVisible].Cells["검사번호"].Value as Guid? : null;
        int horizontalOffset = _history.HorizontalScrollingOffset;
        _bindingHistory = true;
        try
        {
            _historyRecords = inspections.ToDictionary(x => x.Id);
            _history.DataSource = inspections.Select(x => new
            {
                검사번호 = x.Id,
                시각 = x.InspectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                판정 = ResultText(x.Result),
                사진 = x.HasImage ? "있음" : "없음",
                검출신뢰도 = x.DetectionConfidence?.ToString("P1") ?? "—",
                사유 = x.Reason
            }).ToList();
            if (previous is Guid selected)
                foreach (DataGridViewRow row in _history.Rows)
                    if (row.Cells["검사번호"].Value is Guid id && id == selected)
                    { _history.CurrentCell = column != null && _history.Columns.Contains(column) ? row.Cells[column] : row.Cells[0]; break; }
            // Restore the viewport AFTER CurrentCell, which otherwise scrolls to the selected row.
            // IDs keep the same record visible when newer inspections are inserted at the top.
            if (firstVisible >= 0 && _history.Rows.Count > 0)
            {
                int restoredIndex = Math.Min(firstVisible, _history.Rows.Count - 1);
                if (firstVisibleId is Guid visibleId)
                    foreach (DataGridViewRow row in _history.Rows)
                        if (row.Cells["검사번호"].Value is Guid id && id == visibleId)
                        { restoredIndex = row.Index; break; }
                _history.FirstDisplayedScrollingRowIndex = restoredIndex;
                _history.HorizontalScrollingOffset = horizontalOffset;
            }
            _displayedInspections = inspections.ToArray();
        }
        finally { _bindingHistory = false; }
        _ = ShowInspectionImageAsync();
    }

    private async Task ShowInspectionImageAsync()
    {
        var id = SelectedInspectionId();
        if (id.HasValue && id == _historyImageId) return;
        _historyImageRequest?.Cancel();
        var generation = ++_historyImageGeneration;
        _historyImageId = id;
        var old = _historyImage.Image;
        _historyImage.Image = null;
        old?.Dispose();
        if (!id.HasValue || !_historyRecords.TryGetValue(id.Value, out var inspection))
        { _historyImageCaption.Text = "검사 기록을 선택하면 인식 제품 사진을 표시합니다."; return; }
        var description = $"선택한 검사 · {inspection.InspectedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\r\n{ResultText(inspection.Result)} · 신뢰도 {inspection.DetectionConfidence?.ToString("P1") ?? "—"}\r\n{inspection.Reason}";
        if (!inspection.HasImage)
        { _historyImageCaption.Text = "저장된 인식 사진 없음\r\n" + description; return; }
        _historyImageCaption.Text = "인식 사진을 불러오는 중...\r\n" + description;
        using var request = new CancellationTokenSource();
        _historyImageRequest = request;
        try
        {
            var bytes = await _api.GetInspectionImageAsync(id.Value, request.Token);
            if (IsDisposed || generation != _historyImageGeneration) return;
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            _historyImage.Image = new Bitmap(image);
            _historyImageCaption.Text = "기록 당시 인식 제품 사진\r\n" + description;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && generation == _historyImageGeneration)
            { _historyImageId = null; _historyImageCaption.Text = "사진 조회 실패 · " + ex.Message; }
        }
        finally { if (ReferenceEquals(_historyImageRequest, request)) _historyImageRequest = null; }
    }

    private static byte[] CreateRecordImage(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        using var original = Image.FromStream(stream);
        var scale = Math.Min(1d, 1280d / Math.Max(original.Width, original.Height));
        using var image = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, 0, 0, image.Width, image.Height);
        }
        using var output = new MemoryStream();
        image.Save(output, System.Drawing.Imaging.ImageFormat.Jpeg);
        return output.ToArray();
    }
}
