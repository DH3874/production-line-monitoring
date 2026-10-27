using System.Drawing.Drawing2D;
using ProductionLine.Shared;

namespace ProductionLine.UserApp;

internal static class IndustrialPalette
{
    public static readonly Color Canvas = Color.FromArgb(10, 17, 26);
    public static readonly Color Sidebar = Color.FromArgb(8, 15, 24);
    public static readonly Color Surface = Color.FromArgb(18, 29, 42);
    public static readonly Color SurfaceRaised = Color.FromArgb(23, 37, 52);
    public static readonly Color Border = Color.FromArgb(43, 61, 78);
    public static readonly Color Text = Color.FromArgb(230, 238, 245);
    public static readonly Color Muted = Color.FromArgb(142, 160, 177);
    public static readonly Color Accent = Color.FromArgb(25, 192, 198);
    public static readonly Color Green = Color.FromArgb(50, 205, 132);
    public static readonly Color Red = Color.FromArgb(243, 91, 103);
    public static readonly Color Amber = Color.FromArgb(242, 176, 73);
}

public sealed partial class UserMainForm
{
    private static readonly Color UiInk = IndustrialPalette.Text;
    private static readonly Color UiMuted = IndustrialPalette.Muted;
    private static readonly Color UiAccent = IndustrialPalette.Accent;
    private static readonly Color UiCanvas = IndustrialPalette.Canvas;
    private readonly List<Button> _navigation = new();
    private readonly Label _lineName = new() { Dock = DockStyle.Fill, Text = "라인 정보 확인 중", TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true, Font = new Font("Noto Sans KR", 10, FontStyle.Bold), ForeColor = UiInk };
    private readonly Label _pageTitle = new() { Dock = DockStyle.Fill, Font = new Font("Noto Sans KR", 24, FontStyle.Bold), ForeColor = UiInk };
    private readonly Label _pageSubtitle = new() { Dock = DockStyle.Fill, ForeColor = UiMuted, Font = new Font("Noto Sans KR", 9.5f) };
    private readonly Label _topOperation = new() { Dock = DockStyle.Fill, Text = "●  상태 확인 중", TextAlign = ContentAlignment.MiddleCenter, ForeColor = IndustrialPalette.Green, Font = new Font("Noto Sans KR", 9.5f, FontStyle.Bold) };
    private readonly Label _topCom = new() { Dock = DockStyle.Fill, Text = "◇  COM 연결 확인", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(118, 178, 235), Font = new Font("Noto Sans KR", 9.5f) };
    private readonly Label _topUser = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiInk, Font = new Font("Noto Sans KR", 9.5f) };
    private readonly Label[] _metricValues = Enumerable.Range(0, 4).Select(_ => new Label { Text = "—", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 23, FontStyle.Bold), ForeColor = UiInk }).ToArray();
    private readonly string[] _titles = { "실시간 검사", "검사 결과 · 사진", "설비 · 센서", "문제 조치", "AI 상담", "통계", "설정" };
    private readonly string[] _subtitles =
    {
        "카메라 영상과 현재 검사 상태를 한눈에 확인합니다.",
        "검사 결과와 저장된 제품 사진을 함께 조회합니다.",
        "Arduino Mega, 환경 센서와 구동 장치를 점검하고 제어합니다.",
        "발생한 문제를 확인하고 관리자와 조치 내용을 공유합니다.",
        "설비 증상을 입력하면 AI가 점검 순서를 안내합니다.",
        "누적 검사 수량과 판정 비율을 확인합니다.",
        "카메라와 YOLO 모델 등 검사 환경을 설정합니다."
    };

    private Control BuildModernShell()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = UiCanvas };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(11, 31, 51), Padding = new Padding(0, 24, 0, 18) };
        var brand = new Panel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(22, 0, 10, 0) };
        brand.Controls.Add(new Label { Text = "생산라인 모니터링", Dock = DockStyle.Top, Height = 48, ForeColor = Color.White, Font = new Font("Noto Sans KR", 17, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 470, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        var names = new[] { "▣   실시간 검사", "▧   검사 결과 · 사진", "⚙   설비 · 센서", "⚠   문제 조치", "◇   AI 상담", "▥   통계", "⚙   설정" };
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Text = names[i], Width = 270, Height = 58, TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat, Font = new Font("Noto Sans KR", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(174, 197, 220), BackColor = sidebar.BackColor, Margin = Padding.Empty,
                Padding = new Padding(26, 0, 0, 0), Cursor = Cursors.Hand, Tag = "navigation"
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => _pages.SelectedIndex = index;
            _navigation.Add(button); nav.Controls.Add(button);
        }
        var account = new Panel { Dock = DockStyle.Bottom, Height = 108 };
        account.Controls.Add(new Label { Text = "●  서버 연결 상태", Dock = DockStyle.Top, Height = 42, ForeColor = IndustrialPalette.Green, Font = new Font("Noto Sans KR", 9), Padding = new Padding(22, 8, 0, 0) });
        var logout = Button("로그아웃", () => { LogoutRequested = true; Close(); });
        logout.Dock = DockStyle.Bottom; logout.Height = 38; logout.Tag = "navigation"; logout.FlatStyle = FlatStyle.Flat; logout.FlatAppearance.BorderSize = 0;
        logout.BackColor = Color.FromArgb(17, 43, 68); logout.ForeColor = Color.White; logout.Margin = new Padding(22, 0, 22, 0);
        account.Controls.Add(logout); sidebar.Controls.Add(nav); sidebar.Controls.Add(brand); sidebar.Controls.Add(account); shell.Controls.Add(sidebar, 0, 0);

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = Padding.Empty, BackColor = UiCanvas };
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty, Padding = new Padding(22, 0, 22, 0), BackColor = Color.FromArgb(13, 30, 47) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _lineName.TextAlign = ContentAlignment.MiddleLeft; _lineName.Font = new Font("Noto Sans KR", 11, FontStyle.Bold);
        toolbar.Controls.Add(_lineName, 0, 0); toolbar.Controls.Add(_topOperation, 1, 0); toolbar.Controls.Add(_topCom, 2, 0);
        _topUser.Text = $"●  {_api.CurrentUser?.Name ?? "작업자"}  ⌄"; toolbar.Controls.Add(_topUser, 3, 0); workspace.Controls.Add(toolbar, 0, 0);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24, 12, 24, 4), Margin = Padding.Empty };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(_pageTitle, 0, 0); heading.Controls.Add(_pageSubtitle, 0, 1); workspace.Controls.Add(heading, 0, 1);
        _pages.Margin = new Padding(22, 0, 22, 0); workspace.Controls.Add(_pages, 0, 2);
        _connection.ForeColor = UiMuted; _connection.Font = new Font("Noto Sans KR", 8.5f); _connection.Padding = new Padding(24, 0, 0, 0);
        workspace.Controls.Add(_connection, 0, 3); shell.Controls.Add(workspace, 1, 0);
        _pages.SelectedIndexChanged += (_, _) => UpdateNavigation(); UpdateNavigation();
        return shell;
    }

    private void UpdateNavigation()
    {
        int index = Math.Clamp(_pages.SelectedIndex, 0, _titles.Length - 1);
        _pageTitle.Text = _titles[index]; _pageSubtitle.Text = _subtitles[index];
        for (int i = 0; i < _navigation.Count; i++)
        {
            _navigation[i].BackColor = i == index ? Color.FromArgb(22, 79, 145) : Color.FromArgb(11, 31, 51);
            _navigation[i].ForeColor = i == index ? Color.White : Color.FromArgb(174, 197, 220);
            _navigation[i].FlatAppearance.BorderSize = 0;
        }
    }

    private Control Surface(string? title, Control content)
    {
        var card = new RoundedSurface { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 14), Margin = new Padding(0, 0, 0, 10) };
        content.Dock = DockStyle.Fill; card.Controls.Add(content);
        if (title != null) card.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 30, ForeColor = UiInk, Font = new Font("Noto Sans KR", 10, FontStyle.Bold) });
        return card;
    }

    private Control BuildMetricCards()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        var labels = new[] { "정상", "불량", "판정 불가", "정상률" };
        var accents = new[] { IndustrialPalette.Green, IndustrialPalette.Red, IndustrialPalette.Amber, UiAccent };
        for (int i = 0; i < 4; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); _metricValues[i].ForeColor = accents[i];
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(new Label { Text = labels[i], Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = UiMuted, Font = new Font("Noto Sans KR", 9, FontStyle.Bold) }, 0, 0);
            _metricValues[i].Margin = Padding.Empty; content.Controls.Add(_metricValues[i], 0, 1);
            var card = Surface(null, content); card.Margin = new Padding(i == 0 ? 0 : 5, 0, i == 3 ? 0 : 5, 10); row.Controls.Add(card, i, 0);
        }
        return row;
    }

    private void UpdateMetricCards(LineStatusDto line)
    {
        _metricValues[0].Text = line.NormalCount.ToString("N0"); _metricValues[1].Text = line.DefectCount.ToString("N0"); _metricValues[2].Text = line.UndeterminedCount.ToString("N0");
        int total = line.NormalCount + line.DefectCount;
        _metricValues[3].Text = $"{(total == 0 ? 0 : line.NormalCount * 100.0 / total):0.0}%";
        _liveMetricValues[0].Text = (line.NormalCount + line.DefectCount + line.UndeterminedCount).ToString("N0");
        _liveMetricValues[1].Text = line.NormalCount.ToString("N0");
        _liveMetricValues[2].Text = line.DefectCount.ToString("N0");
        _liveMetricValues[3].Text = $"{(total == 0 ? 0 : line.DefectCount * 100.0 / total):0.0}%";
    }

    private static void ApplyModernControls(Control root)
    {
        root.ForeColor = UiInk;
        foreach (Control control in root.Controls)
        {
            if (control is TabPage page) { page.BackColor = UiCanvas; page.Padding = Padding.Empty; }
            if (control is Label label && label.ForeColor == SystemColors.ControlText) label.ForeColor = UiInk;
            if (control is Button button)
            {
                void Shape() { if (button.Width < 2 || button.Height < 2) return; using var path = UiGeometry.Round(new Rectangle(0, 0, button.Width, button.Height), 6); var previous = button.Region; button.Region = new Region(path); previous?.Dispose(); }
                button.SizeChanged += (_, _) => Shape(); Shape();
                if (!Equals(button.Tag, "navigation"))
                {
                    bool danger = button.Text.Contains("정지") || button.Text.Contains("해제");
                    bool primary = button.Text.Contains("시작") || button.Text.Contains("보내기") || button.Text.Contains("전송") || button.Text is "카메라 연결" or "완료 요청" or "새 문제 등록" or "벨트 구동" or "벨트 연결";
                    button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = primary || danger ? 0 : 1; button.FlatAppearance.BorderColor = IndustrialPalette.Border;
                    bool compact = Equals(button.Tag, "compact");
                    button.Font = new Font("Noto Sans KR", compact ? 8.3f : 9, FontStyle.Bold); button.Cursor = Cursors.Hand; button.MinimumSize = compact ? Size.Empty : new Size(82, 34); button.Margin = compact ? Padding.Empty : new Padding(0, 0, 8, 0); button.Padding = compact ? new Padding(2, 0, 2, 0) : new Padding(10, 0, 10, 0);
                    if (button.Dock == DockStyle.None) { button.AutoSizeMode = AutoSizeMode.GrowAndShrink; button.AutoSize = true; button.Size = button.GetPreferredSize(Size.Empty); }
                    void Palette() { button.BackColor = !button.Enabled ? Color.FromArgb(37, 48, 59) : danger ? Color.FromArgb(132, 47, 58) : primary ? Color.FromArgb(15, 124, 132) : IndustrialPalette.SurfaceRaised; button.ForeColor = !button.Enabled ? Color.FromArgb(100, 115, 129) : Color.White; }
                    button.EnabledChanged += (_, _) => Palette(); Palette();
                    button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(18, 151, 158) : Color.FromArgb(35, 54, 69);
                }
            }
            if (control is TextBox text) { text.BorderStyle = BorderStyle.FixedSingle; text.BackColor = Color.FromArgb(13, 23, 34); text.ForeColor = UiInk; }
            if (control is RichTextBox rich) { rich.BorderStyle = BorderStyle.None; rich.BackColor = Color.FromArgb(13, 23, 34); rich.ForeColor = UiInk; }
            if (control is ComboBox combo) { combo.FlatStyle = FlatStyle.Flat; combo.BackColor = IndustrialPalette.SurfaceRaised; combo.ForeColor = UiInk; combo.Font = new Font("Noto Sans KR", 9); }
            if (control is NumericUpDown numeric) { numeric.BackColor = IndustrialPalette.SurfaceRaised; numeric.ForeColor = UiInk; }
            if (control is CheckBox check) check.ForeColor = UiInk;
            if (control is TabControl tabs) { tabs.BackColor = UiCanvas; tabs.ForeColor = UiInk; }
            if (control is DataGridView grid)
            {
                grid.BackgroundColor = IndustrialPalette.Surface; grid.BorderStyle = BorderStyle.None; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.GridColor = IndustrialPalette.Border; grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; grid.ColumnHeadersHeight = 40; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(25, 40, 55), ForeColor = UiMuted, Font = new Font("Noto Sans KR", 9, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
                grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = IndustrialPalette.Surface, ForeColor = UiInk, SelectionBackColor = Color.FromArgb(24, 75, 82), SelectionForeColor = Color.White, Padding = new Padding(8, 0, 0, 0), Font = new Font("Noto Sans KR", 9.5f), NullValue = "—" };
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(20, 33, 47); grid.RowTemplate.Height = 42; grid.AllowUserToResizeRows = false;
            }
            ApplyModernControls(control);
        }
    }
}

internal static class UiGeometry
{
    public static GraphicsPath Round(Rectangle rect, int radius)
    {
        float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)); var path = new GraphicsPath();
        if (d < 1) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class PageHost : TabControl
{
    public PageHost() { Appearance = TabAppearance.FlatButtons; ItemSize = new Size(0, 1); SizeMode = TabSizeMode.Fixed; TabStop = false; }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x1328 && !DesignMode) { m.Result = new IntPtr(1); return; } base.WndProc(ref m); }
}

internal sealed class RoundedSurface : Panel
{
    public RoundedSurface() { DoubleBuffered = true; BackColor = IndustrialPalette.Surface; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var canvas = new SolidBrush(IndustrialPalette.Canvas); e.Graphics.FillRectangle(canvas, e.ClipRectangle); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiGeometry.Round(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 10);
        using var brush = new SolidBrush(IndustrialPalette.Surface); e.Graphics.FillPath(brush, path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiGeometry.Round(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 10);
        using var pen = new Pen(IndustrialPalette.Border); e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class CameraCanvas : PictureBox
{
    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe); if (Image != null) return;
        var g = pe.Graphics; using var canvas = new SolidBrush(Color.FromArgb(7, 14, 22)); g.FillRectangle(canvas, ClientRectangle); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(22, 38, 53)); for (int x = 0; x < Width; x += 56) g.DrawLine(grid, x, 0, x, Height); for (int y = 0; y < Height; y += 56) g.DrawLine(grid, 0, y, Width, y);
        using var title = new Font("Noto Sans KR", 14, FontStyle.Bold); using var subtitle = new Font("Noto Sans KR", 9);
        TextRenderer.DrawText(g, "CAMERA OFFLINE", title, new Rectangle(0, Height / 2 - 26, Width, 34), IndustrialPalette.Text, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "카메라 연결 버튼을 눌러 검사를 준비하세요", subtitle, new Rectangle(0, Height / 2 + 14, Width, 24), IndustrialPalette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class DarkComboBox : UserControl
{
    private readonly ComboBox _model = new();
    private readonly Label _value = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), AutoEllipsis = true };
    private readonly Label _arrow = new() { Dock = DockStyle.Right, Width = 26, Text = "⌄", TextAlign = ContentAlignment.MiddleCenter };
    private readonly ContextMenuStrip _menu = new() { ShowImageMargin = false };

    public DarkComboBox()
    {
        Height = 34; MinimumSize = new Size(70, 30); BackColor = Color.FromArgb(15, 27, 39); ForeColor = IndustrialPalette.Text;
        BorderStyle = BorderStyle.FixedSingle; Controls.Add(_value); Controls.Add(_arrow);
        foreach (var control in new Control[] { _value, _arrow }) control.Click += (_, _) => OpenMenu();
        _model.SelectedIndexChanged += (_, _) => { _value.Text = _model.SelectedItem?.ToString() ?? string.Empty; SelectedIndexChanged?.Invoke(this, EventArgs.Empty); };
        EnabledChanged += (_, _) => { _value.ForeColor = _arrow.ForeColor = Enabled ? IndustrialPalette.Text : IndustrialPalette.Muted; };
    }

    public object? DataSource { get => _model.DataSource; set => _model.DataSource = value; }
    public ComboBox.ObjectCollection Items => _model.Items;
    public int SelectedIndex { get => _model.SelectedIndex; set => _model.SelectedIndex = value; }
    public object? SelectedItem { get => _model.SelectedItem; set => _model.SelectedItem = value; }
    public event EventHandler? SelectedIndexChanged;

    private void OpenMenu()
    {
        if (!Enabled) return;
        _menu.Items.Clear();
        foreach (var item in _model.Items.Cast<object>())
        {
            var menuItem = new ToolStripMenuItem(item.ToString()) { ForeColor = IndustrialPalette.Text, BackColor = IndustrialPalette.SurfaceRaised };
            menuItem.Click += (_, _) => SelectedItem = item;
            _menu.Items.Add(menuItem);
        }
        _menu.BackColor = IndustrialPalette.SurfaceRaised; _menu.ForeColor = IndustrialPalette.Text;
        if (_menu.Items.Count > 0) _menu.Show(this, new Point(0, Height));
    }
}

internal sealed class DarkNumericUpDown : UserControl
{
    private readonly Label _value = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    private readonly NumericStepper _steps = new() { Dock = DockStyle.Right, Width = 24 };
    private decimal _minimum, _maximum = 100, _current;

    public DarkNumericUpDown()
    {
        Height = 34; MinimumSize = new Size(52, 30); BackColor = Color.FromArgb(15, 27, 39); ForeColor = IndustrialPalette.Text; BorderStyle = BorderStyle.FixedSingle;
        _steps.ChangeRequested += Change; Controls.Add(_value); Controls.Add(_steps); RefreshText();
        MouseWheel += (_, e) => Change(e.Delta > 0 ? 1 : -1);
    }

    public decimal Minimum { get => _minimum; set { _minimum = value; Value = _current; } }
    public decimal Maximum { get => _maximum; set { _maximum = value; Value = _current; } }
    public decimal Value { get => _current; set { _current = Math.Clamp(value, _minimum, _maximum); RefreshText(); } }

    private void Change(int delta) { if (Enabled) Value = _current + delta; }
    private void RefreshText() => _value.Text = _current.ToString("0");

    private sealed class NumericStepper : Control
    {
        public event Action<int>? ChangeRequested;
        public NumericStepper() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(31, 49, 65));
            using var divider = new Pen(IndustrialPalette.Border); e.Graphics.DrawLine(divider, 0, Height / 2, Width, Height / 2);
            using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "+", font, new Rectangle(0, 0, Width, Height / 2), Color.FromArgb(214, 226, 237), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "−", font, new Rectangle(0, Height / 2, Width, Height - Height / 2), Color.FromArgb(214, 226, 237), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); ChangeRequested?.Invoke(e.Y < Height / 2 ? 1 : -1); }
    }
}

internal sealed class DarkProgressBar : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 45 };
    private ProgressBarStyle _style;
    private int _position;

    public DarkProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        MinimumSize = new Size(80, 20); BackColor = Color.FromArgb(13, 23, 34);
        _animation.Tick += (_, _) => { _position = (_position + 7) % Math.Max(1, Width + 90); Invalidate(); };
    }

    public ProgressBarStyle Style
    {
        get => _style;
        set { _style = value; if (value == ProgressBarStyle.Marquee) _animation.Start(); else { _animation.Stop(); _position = 0; } Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var border = new Pen(IndustrialPalette.Border); e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        if (_style == ProgressBarStyle.Marquee)
        {
            var x = _position - 90; using var fill = new SolidBrush(IndustrialPalette.Accent);
            e.Graphics.FillRectangle(fill, x, 2, 88, Math.Max(1, Height - 4));
        }
        else
        {
            using var idle = new SolidBrush(Color.FromArgb(24, 40, 55));
            e.Graphics.FillRectangle(idle, 2, 2, Math.Max(0, Width - 4), Math.Max(1, Height - 4));
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) _animation.Dispose(); base.Dispose(disposing); }
}

internal sealed class DarkGridScrollBar : Control
{
    private readonly DataGridView _grid;
    private Rectangle _thumb;
    private bool _dragging;
    private int _dragOffset;

    public DarkGridScrollBar(DataGridView grid)
    {
        _grid = grid; Width = 14; MinimumSize = new Size(14, 20); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _grid.Scroll += (_, _) => Invalidate();
        _grid.RowsAdded += (_, _) => Invalidate();
        _grid.RowsRemoved += (_, _) => Invalidate();
        _grid.DataBindingComplete += (_, _) => Invalidate();
        _grid.Resize += (_, _) => Invalidate();
        _grid.MouseWheel += (_, e) =>
        {
            ScrollBy(e.Delta > 0 ? -3 : 3);
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(12, 22, 32));
        _thumb = CalculateThumb();
        if (_thumb.IsEmpty) return;
        using var brush = new SolidBrush(Color.FromArgb(70, 96, 119));
        e.Graphics.FillRectangle(brush, _thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _thumb = CalculateThumb();
        if (_thumb.IsEmpty) return;
        if (_thumb.Contains(e.Location)) { _dragging = true; _dragOffset = e.Y - _thumb.Top; Capture = true; }
        else ScrollToTrack(e.Y - _thumb.Height / 2);
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_dragging) ScrollToTrack(e.Y - _dragOffset); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; Capture = false; }

    private void ScrollBy(int rows)
    {
        if (_grid.Rows.Count == 0) return;
        var current = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
        SetFirstRow(current + rows);
    }

    private void ScrollToTrack(int thumbTop)
    {
        int displayed = DisplayedRows();
        int maxFirst = Math.Max(0, _grid.Rows.Count - displayed);
        int travel = Math.Max(1, Height - Math.Max(28, _thumb.Height));
        int target = (int)Math.Round(Math.Clamp(thumbTop, 0, travel) / (double)travel * maxFirst);
        SetFirstRow(target);
    }

    private void SetFirstRow(int index)
    {
        if (_grid.Rows.Count == 0) return;
        int displayed = DisplayedRows();
        int maximum = Math.Max(0, _grid.Rows.Count - displayed);
        try { _grid.FirstDisplayedScrollingRowIndex = Math.Clamp(index, 0, maximum); } catch (InvalidOperationException) { }
        Invalidate();
    }

    private Rectangle CalculateThumb()
    {
        int total = _grid.Rows.Count;
        int displayed = DisplayedRows();
        if (total <= displayed || total == 0 || Height < 8) return Rectangle.Empty;
        int thumbHeight = Math.Max(28, (int)Math.Round(Height * displayed / (double)total));
        int maxFirst = Math.Max(1, total - displayed);
        int first = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
        int top = (int)Math.Round((Height - thumbHeight) * first / (double)maxFirst);
        return new Rectangle(3, top, Math.Max(4, Width - 6), thumbHeight);
    }

    private int DisplayedRows()
    {
        if (!_grid.IsHandleCreated || _grid.Rows.Count == 0) return Math.Max(1, _grid.Rows.Count);
        return Math.Max(1, _grid.DisplayedRowCount(false));
    }
}

internal sealed class DarkTabControl : UserControl
{
    private readonly FlowLayoutPanel _headers = new() { Dock = DockStyle.Top, Height = 38, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = IndustrialPalette.Canvas };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = IndustrialPalette.Canvas, Padding = new Padding(2) };
    private readonly List<Panel> _pages = new();
    private readonly List<Label> _labels = new();
    private int _selectedIndex = -1;

    public DarkTabControl()
    {
        BackColor = IndustrialPalette.Canvas; ForeColor = IndustrialPalette.Text; Controls.Add(_content); Controls.Add(_headers);
        TabPages = new DarkTabPageCollection(this);
    }

    public DarkTabPageCollection TabPages { get; }
    public int SelectedIndex { get => _selectedIndex; set => SelectPage(value); }

    private void AddPage(TabPage page)
    {
        int index = _pages.Count;
        var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = IndustrialPalette.Canvas, Padding = Padding.Empty, Margin = Padding.Empty };
        while (page.Controls.Count > 0)
        {
            var child = page.Controls[0]; page.Controls.RemoveAt(0); pageHost.Controls.Add(child);
        }
        _pages.Add(pageHost);
        var label = new Label { Width = 116, Height = 34, Margin = new Padding(0, 0, 6, 0), Text = page.Text, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, Font = new Font("Noto Sans KR", 9, FontStyle.Bold) };
        label.Click += (_, _) => SelectPage(index); _labels.Add(label); _headers.Controls.Add(label); _content.Controls.Add(pageHost);
        if (_selectedIndex < 0) SelectPage(0); else pageHost.Visible = false;
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return; _selectedIndex = index;
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index; if (i == index) _pages[i].BringToFront();
            _labels[i].BackColor = i == index ? Color.FromArgb(22, 79, 145) : Color.FromArgb(20, 35, 49);
            _labels[i].ForeColor = i == index ? Color.White : IndustrialPalette.Muted;
        }
    }

    internal sealed class DarkTabPageCollection
    {
        private readonly DarkTabControl _owner;
        internal DarkTabPageCollection(DarkTabControl owner) => _owner = owner;
        public void Add(TabPage page) => _owner.AddPage(page);
    }
}
