using System.Drawing.Drawing2D;
using ProductionLine.ChatUI;

namespace _01_AdminApp;

// [관리자 앱 어두운 테마] 화면 모양(색·글꼴·버튼 모양)만 바꿉니다. 기능·이벤트·글자는 그대로입니다.
// 창마다 생성자 끝에서 AdminTheme.Attach(this)를 부르면, 지금 있는 컨트롤과 나중에 추가되는 컨트롤에 모두 적용됩니다.
// 코드가 실행 중에 밝은 화면용 색(DimGray, Firebrick 등)을 넣어도 어두운 배경에서 읽히는 색으로 바꿔 줍니다.
internal static class AdminTheme
{
    public static readonly Color Canvas = Color.FromArgb(11, 20, 34);       // 창 바탕
    public static readonly Color Sidebar = Color.FromArgb(14, 26, 44);      // 왼쪽 메뉴
    public static readonly Color Surface = Color.FromArgb(20, 34, 54);      // 카드
    public static readonly Color SurfaceRaised = Color.FromArgb(27, 44, 68); // 버튼·표 머리
    public static readonly Color Input = Color.FromArgb(12, 23, 39);        // 입력칸·표 칸
    public static readonly Color Border = Color.FromArgb(43, 62, 88);
    public static readonly Color Text = Color.FromArgb(230, 237, 245);
    public static readonly Color Muted = Color.FromArgb(143, 162, 186);
    public static readonly Color Accent = Color.FromArgb(47, 128, 237);     // 파란 주 버튼
    public static readonly Color Good = Color.FromArgb(61, 214, 140);
    public static readonly Color Bad = Color.FromArgb(255, 107, 107);
    public static readonly Color Warn = Color.FromArgb(255, 184, 77);
    public static readonly Color Danger = Color.FromArgb(214, 69, 69);

    private static readonly HashSet<Control> Styled = new();

    // 생성자 맨 앞에서 부르면, 생성자가 컨트롤을 다 만든 뒤(창이 처음 열릴 때) 테마를 입힙니다.
    public static void AttachOnLoad(Form form) => form.Load += (_, _) => Attach(form);

    public static void Attach(Control root)
    {
        Apply(root);
        if (root is Form form) form.BackColor = Canvas;
    }

    public static void Apply(Control control)
    {
        if (control.IsDisposed) return;
        if (Styled.Add(control))
        {
            control.Disposed += (_, _) => Styled.Remove(control);
            control.ControlAdded += (_, e) => { if (e.Control != null) Apply(e.Control); };
            StyleOnce(control);
        }
        foreach (Control child in control.Controls) Apply(child);
    }

    // 스크롤바: Windows 기본 어두운 스크롤바 모양(탐색기 어두운 모드와 같은 것)을 씀
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);

    private static void DarkScrollbars(Control control)
    {
        if (control is not (TextBoxBase or ListBox or DataGridView or ScrollBar or ComboBox or ScrollableControl)) return;
        if (control is ScrollableControl scrollable && control is not (Form or UserControl) && !scrollable.AutoScroll) return;
        void Set() { try { SetWindowTheme(control.Handle, control is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer", null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } }
        if (control.IsHandleCreated) Set();
        control.HandleCreated += (_, _) => Set();
    }

    private static void StyleOnce(Control control)
    {
        DarkScrollbars(control);
        switch (control)
        {
            case Form form:
                form.BackColor = Canvas; form.ForeColor = Text; break;
            case NavButton:
                break;
            case Button button:
                StyleButton(button); break;
            case StatusPill:
            case CardPanel:
            case IconBadge:
                break;
            case DataGridView grid:
                StyleGrid(grid); break;
            case TextBoxBase box:
                box.BackColor = Input; box.ForeColor = Text;
                if (box is TextBox t) t.BorderStyle = BorderStyle.FixedSingle; else box.BorderStyle = BorderStyle.None;
                box.BackColorChanged += (_, _) => { if (IsLight(box.BackColor)) box.BackColor = Input; };
                box.ForeColorChanged += (_, _) => { var mapped = MapText(box.ForeColor); if (mapped != box.ForeColor) box.ForeColor = mapped; };
                break;
            case ComboBox combo:
                StyleCombo(combo); break;
            case NumericUpDown number:
                number.BackColor = Input; number.ForeColor = Text; number.BorderStyle = BorderStyle.FixedSingle; break;
            case ListBox list:
                list.BackColor = Input; list.ForeColor = Text; list.BorderStyle = BorderStyle.FixedSingle; break;
            case DateTimePicker picker:
                picker.CalendarMonthBackground = Input; picker.CalendarForeColor = Text; break;
            case CheckBox or RadioButton:
                control.ForeColor = Text; break;
            case TabControl tabs:
                StyleTabs(tabs); break;
            case TabPage page:
                page.BackColor = Canvas; page.ForeColor = Text; page.UseVisualStyleBackColor = false; break;
            case SensorTrendPanel trend:
                trend.DarkMode = true; break;
            case GroupChatPanel chat:
                chat.BackColor = Surface; chat.ForeColor = Text; chat.DarkMode = true; break;
            case PictureBox picture:
                if (IsLight(picture.BackColor)) picture.BackColor = Input; break;
            case Label label:
                label.ForeColor = MapText(label.ForeColor);
                if (IsLight(label.BackColor) && label.BackColor != Color.Transparent) label.BackColor = Color.Transparent;
                label.ForeColorChanged += (_, _) => { var mapped = MapText(label.ForeColor); if (mapped != label.ForeColor) label.ForeColor = mapped; };
                break;
            default:
                // 패널·표 배치 칸: 밝은 바탕을 직접 지정한 칸은 카드 색, 나머지는 부모 색을 그대로 따름
                if (IsLight(control.BackColor)) control.BackColor = Surface;
                control.BackColorChanged += (_, _) => { if (IsLight(control.BackColor)) control.BackColor = Surface; };
                break;
        }
    }

    // 밝은 화면에서 쓰던 글자색 → 어두운 화면에서 읽히는 색
    public static Color MapText(Color color)
    {
        if (color == Color.Firebrick || color == Color.Red || color == Color.DarkRed || color == Color.Crimson) return Bad;
        if (color == Color.SeaGreen || color == Color.DarkGreen || color == Color.Green || color == Color.ForestGreen) return Good;
        if (color == Color.DarkOrange || color == Color.Orange || color == Color.Chocolate) return Warn;
        if (color == Color.DimGray || color == Color.Gray || color == Color.DarkGray || color == Color.SlateGray) return Muted;
        var brightness = color.GetBrightness();
        if (color.A == 0 || color == Text || color == Muted || color == Good || color == Bad || color == Warn) return color;
        if (brightness < 0.45f) return color.GetSaturation() > 0.35f && brightness > 0.25f ? Lighten(color) : Text;
        return color;
    }

    private static Color Lighten(Color c) => Color.FromArgb(c.A, Math.Min(255, c.R + 90), Math.Min(255, c.G + 90), Math.Min(255, c.B + 90));
    private static bool IsLight(Color c) => c.A > 0 && c.GetBrightness() > 0.8f;

    // 드롭다운: 닫힌 상자와 목록을 직접 그려 어두운 색으로 (선택·검색 동작은 그대로)
    private static void StyleCombo(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Input; combo.ForeColor = Text;
        if (combo.DropDownStyle == ComboBoxStyle.DropDownList) DarkComboPainter.Attach(combo);
        if (combo.DrawMode != DrawMode.Normal) return;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.DrawItem += (_, e) =>
        {
            var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using var back = new SolidBrush(selected ? Accent : Input);
            e.Graphics.FillRectangle(back, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, e.Bounds,
                    combo.Enabled ? (selected ? Color.White : Text) : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    public static void StyleButton(Button button)
    {
        button.Padding = Padding.Empty;
        // 칸에 꽉 채운 낮은 버튼은 글자가 잘리지 않게 여백·글자 크기를 줄임
        if (button.Dock == DockStyle.Fill) { if (button.Margin == new Padding(3)) button.Margin = new Padding(2); if (button.Font.Size > 9.5F) button.Font = new Font(button.Font.FontFamily, 9F, button.Font.Style); }
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        void Palette()
        {
            if (Equals(button.Tag, "nav")) return;
            // 코드에서 직접 색을 정한 버튼(긴급 정지 빨강, 로그인 파랑)만 그 색을 유지. 부모 바탕색을 물려받은 버튼은 테마 색으로.
            var inherited = button.Parent != null && button.BackColor == button.Parent.BackColor;
            var custom = !inherited && button.BackColor != SystemColors.Control && !IsLight(button.BackColor)
                && button.BackColor != SurfaceRaised && button.BackColor != Accent && button.BackColor != Danger;
            if (custom) { button.ForeColor = Color.White; button.FlatAppearance.BorderSize = 0; return; }
            var kind = Kind(button.Text);
            button.BackColor = kind == 1 ? Accent : kind == 2 ? Danger : SurfaceRaised;
            button.ForeColor = button.Enabled ? Color.White : Muted;
            button.FlatAppearance.BorderSize = kind == 0 ? 1 : 0;
            button.FlatAppearance.MouseOverBackColor = kind == 1 ? Color.FromArgb(66, 145, 250) : kind == 2 ? Color.FromArgb(230, 90, 90) : Color.FromArgb(37, 58, 88);
        }
        Palette();
        button.EnabledChanged += (_, _) => Palette();
        button.TextChanged += (_, _) => Palette();
    }

    // 1 = 주 동작(파랑), 2 = 정지·삭제(빨강), 0 = 보통
    private static int Kind(string text)
    {
        if (text.Contains("정지") || text.Contains("삭제") || text.Contains("반려")) return 2;
        if (text.Contains("저장") || text.Contains("로그인") || text.Contains("접속") || text.Contains("종결") || text.Contains("보내기")
            || text.Contains("전송") || text.Contains("추가") || text.Contains("적용") || text.Contains("시작") || text == "연결") return 1;
        return 0;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface; grid.BorderStyle = BorderStyle.None; grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceRaised;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceRaised;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("맑은 고딕", 9F, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Input; grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(31, 74, 122); grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(16, 29, 47);
        grid.RowHeadersDefaultCellStyle.BackColor = SurfaceRaised; grid.RowHeadersDefaultCellStyle.ForeColor = Muted;
        // 코드가 행마다 밝은 화면용 글자색을 넣으면 어두운 화면용으로 바꿈
        grid.CellFormatting += (_, e) =>
        {
            if (e.CellStyle == null) return;
            var mapped = MapText(e.CellStyle.ForeColor);
            if (mapped != e.CellStyle.ForeColor) e.CellStyle.ForeColor = mapped;
            if (IsLight(e.CellStyle.BackColor)) e.CellStyle.BackColor = Input;
        };
    }

    // 탭: 탭 줄 전체(빈 칸 포함)를 직접 그려 흰 띠가 남지 않게 함. 탭 전환 동작은 그대로.
    private static void StyleTabs(TabControl tabs)
    {
        if (tabs.Appearance != TabAppearance.Normal || tabs.ItemSize.Height <= 1) return; // 숨긴 탭(페이지 전환용)은 건드리지 않음
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(Math.Max(tabs.ItemSize.Width, 190), 36);
        typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(tabs, new object[] { ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true });
        tabs.Paint += (_, e) =>
        {
            var g = e.Graphics;
            using (var canvas = new SolidBrush(tabs.Parent?.BackColor ?? Canvas)) g.FillRectangle(canvas, tabs.ClientRectangle);
            for (int i = 0; i < tabs.TabCount; i++)
            {
                var r = tabs.GetTabRect(i); r.Inflate(-2, 0);
                var selected = i == tabs.SelectedIndex;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = Round(new Rectangle(r.X, r.Y + 2, r.Width, r.Height - 2), 8);
                using var back = new SolidBrush(selected ? Accent : SurfaceRaised); g.FillPath(back, path);
                TextRenderer.DrawText(g, tabs.TabPages[i].Text, tabs.Font, r, selected ? Color.White : Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            var page = tabs.DisplayRectangle; page.Inflate(2, 2);
            using var edge = new Pen(Border); g.DrawRectangle(edge, page.X, page.Y, page.Width - 1, page.Height - 1);
        };
        tabs.SelectedIndexChanged += (_, _) => tabs.Invalidate();
    }

    // 둥근 모서리 카드 (메인 화면 지표·라인 카드용)
    public static GraphicsPath Round(Rectangle rect, int radius)
    {
        var path = new GraphicsPath(); var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d < 2) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

// 드롭다운(목록에서 고르기형)의 닫힌 상자를 Windows가 그린 뒤 그 위에 어두운 색으로 다시 그림:
// 바탕·선택된 글자·테두리(포커스 때 파랑)·화살표. 클릭·목록 열기·선택은 Windows 동작 그대로입니다.
internal sealed class DarkComboPainter : NativeWindow
{
    private const int WM_PAINT = 0x000F, WM_NCDESTROY = 0x0082;
    private readonly ComboBox _combo;
    private DarkComboPainter(ComboBox combo) { _combo = combo; }

    public static void Attach(ComboBox combo)
    {
        var painter = new DarkComboPainter(combo);
        if (combo.IsHandleCreated) painter.AssignHandle(combo.Handle);
        combo.HandleCreated += (_, _) => { if (painter.Handle == IntPtr.Zero) painter.AssignHandle(combo.Handle); };
        combo.HandleDestroyed += (_, _) => painter.ReleaseHandle();
        void Redraw() { if (combo.IsHandleCreated) combo.Invalidate(); }
        combo.SelectedIndexChanged += (_, _) => Redraw();
        combo.EnabledChanged += (_, _) => Redraw();
        combo.GotFocus += (_, _) => Redraw();
        combo.LostFocus += (_, _) => Redraw();
        combo.DropDownClosed += (_, _) => Redraw();
        combo.TextChanged += (_, _) => Redraw();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_NCDESTROY) { ReleaseHandle(); return; }
        if (m.Msg != WM_PAINT || _combo.IsDisposed) return;
        using var g = Graphics.FromHwnd(Handle);
        var r = new Rectangle(0, 0, _combo.Width, _combo.Height);
        var enabled = _combo.Enabled;
        using (var back = new SolidBrush(enabled ? AdminTheme.Input : Color.FromArgb(18, 30, 48))) g.FillRectangle(back, r);
        using (var edge = new Pen(_combo.Focused ? AdminTheme.Accent : AdminTheme.Border)) g.DrawRectangle(edge, 0, 0, r.Width - 1, r.Height - 1);
        var arrowWidth = 24;
        var text = new Rectangle(7, 0, Math.Max(1, r.Width - arrowWidth - 9), r.Height);
        TextRenderer.DrawText(g, _combo.Text, _combo.Font, text, enabled ? AdminTheme.Text : AdminTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        // 화살표(∨ 모양)
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = r.Width - arrowWidth / 2f - 2, cy = r.Height / 2f;
        using var arrow = new Pen(enabled ? AdminTheme.Muted : Color.FromArgb(80, 98, 122), 1.6f);
        g.DrawLines(arrow, new[] { new PointF(cx - 4.5f, cy - 2f), new PointF(cx, cy + 2.5f), new PointF(cx + 4.5f, cy - 2f) });
    }
}

// 왼쪽 메뉴 버튼: 아이콘 글자(Windows 기본 아이콘 글꼴) + 메뉴 이름. 누르면 기존 버튼과 똑같이 Click이 실행됩니다.
internal sealed class NavButton : Button
{
    private static readonly string IconFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    private bool _hover;
    public string Icon { get; set; } = "";
    public bool Selected { get; set; }
    public int Badge { get; set; }
    public NavButton(string text, string icon)
    {
        Text = text; Icon = icon; Tag = "nav"; Height = 46; Margin = new Padding(0, 0, 0, 6);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        Font = new Font("맑은 고딕", 10F); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(AdminTheme.Sidebar)) g.FillRectangle(bg, ClientRectangle);
        if (Selected || _hover)
        {
            using var path = AdminTheme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 9);
            using var fill = new SolidBrush(Selected ? AdminTheme.Accent : Color.FromArgb(25, 42, 66)); g.FillPath(fill, path);
        }
        var fore = Selected ? Color.White : Enabled ? Color.FromArgb(198, 212, 228) : AdminTheme.Muted;
        using (var icon = new Font(IconFont, 13F))
            TextRenderer.DrawText(g, Icon, icon, new Rectangle(14, 0, 28, Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(48, 0, Width - (Badge > 0 ? 88 : 54), Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Badge > 0)
        {
            var r = new Rectangle(Width - 36, Height / 2 - 11, 24, 22);
            using var red = new SolidBrush(AdminTheme.Danger); g.FillEllipse(red, r);
            TextRenderer.DrawText(g, Badge > 99 ? "99+" : Badge.ToString(), new Font("맑은 고딕", 8.5F, FontStyle.Bold), r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

// 상태 알약(●정상 가동 같은 작은 둥근 표시)
internal sealed class StatusPill : Control
{
    public Color Dot { get; set; } = AdminTheme.Good;
    public StatusPill() { DoubleBuffered = true; ResizeRedraw = true; Font = new Font("맑은 고딕", 9.5F, FontStyle.Bold); ForeColor = AdminTheme.Good; }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? AdminTheme.Canvas)) g.FillRectangle(bg, ClientRectangle);
        using var path = AdminTheme.Round(new Rectangle(0, 2, Width - 1, Height - 5), (Height - 5) / 2);
        using var fill = new SolidBrush(Color.FromArgb(40, Dot)); g.FillPath(fill, path);
        using var dot = new SolidBrush(Dot); g.FillEllipse(dot, 12, Height / 2 - 5, 10, 10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 34, Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// 지표 카드 왼쪽의 아이콘 동그라미
internal sealed class IconBadge : Control
{
    private static readonly string IconFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    public string Glyph { get; set; } = "";
    public Color Accent { get; set; } = AdminTheme.Accent;
    public IconBadge() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? AdminTheme.Surface)) g.FillRectangle(bg, ClientRectangle);
        var size = Math.Min(Width, Height) - 2; var r = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
        using (var fill = new SolidBrush(Color.FromArgb(45, Accent))) g.FillEllipse(fill, r);
        using var font = new Font(IconFont, Math.Max(8, size * 0.36F));
        TextRenderer.DrawText(g, Glyph, font, r, Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

// 둥근 카드 판: 바탕은 부모 색, 안쪽은 카드 색 + 얇은 테두리
internal sealed class CardPanel : Panel
{
    public Color Fill { get; set; } = AdminTheme.Surface;
    public Color Edge { get; set; } = AdminTheme.Border;
    public CardPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = AdminTheme.Surface; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using (var canvas = new SolidBrush(Parent?.BackColor ?? AdminTheme.Canvas)) e.Graphics.FillRectangle(canvas, ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = AdminTheme.Round(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 12);
        using var fill = new SolidBrush(Fill); e.Graphics.FillPath(fill, path);
        using var pen = new Pen(Edge); e.Graphics.DrawPath(pen, path);
    }
}
