namespace ProductionLine.UserApp;

// [서버 접속 창 어두운 테마] 관리자 앱의 서버 열기 창(AdminTheme)과 같은 색으로 맞춥니다.
// 화면 모양(색·버튼 모양)만 바꾸고 서버 찾기·접속 동작은 그대로입니다.
internal static class ConnectWindowTheme
{
    private static readonly Color Canvas = Color.FromArgb(11, 20, 34);
    private static readonly Color Surface = Color.FromArgb(20, 34, 54);
    private static readonly Color SurfaceRaised = Color.FromArgb(27, 44, 68);
    private static readonly Color Input = Color.FromArgb(12, 23, 39);
    private static readonly Color Border = Color.FromArgb(43, 62, 88);
    private static readonly Color Text = Color.FromArgb(230, 237, 245);
    private static readonly Color Muted = Color.FromArgb(143, 162, 186);
    private static readonly Color Accent = Color.FromArgb(47, 128, 237);
    private static readonly Color Good = Color.FromArgb(61, 214, 140);
    private static readonly Color Bad = Color.FromArgb(255, 107, 107);

    public static void Attach(Form form)
    {
        form.BackColor = Canvas; form.ForeColor = Text;
        Apply(form);
    }

    private static void Apply(Control control)
    {
        Style(control);
        control.ControlAdded += (_, e) => { if (e.Control != null) Apply(e.Control); };
        foreach (Control child in control.Controls) Apply(child);
    }

    private static void Style(Control control)
    {
        switch (control)
        {
            case Form:
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat; button.UseVisualStyleBackColor = false;
                button.FlatAppearance.BorderColor = Border; button.Cursor = Cursors.Hand;
                // 접속 창이 사용 가능/불가에 따라 색을 다시 넣으므로 그 뒤에 테마 색으로 덮음
                // 관리자 창처럼 [닫기]는 회색, 나머지(접속·다시 찾기)는 파란 주 버튼
                bool Primary() => button.Enabled && button.Text != "닫기";
                void Palette()
                {
                    button.BackColor = Primary() ? Accent : SurfaceRaised;
                    button.ForeColor = button.Enabled ? Color.White : Muted;
                    button.FlatAppearance.BorderSize = Primary() ? 0 : 1;
                }
                Palette();
                button.EnabledChanged += (_, _) => Palette();
                button.BackColorChanged += (_, _) => { var want = Primary() ? Accent : SurfaceRaised; if (button.BackColor != want) Palette(); };
                break;
            case TextBox or ListBox:
                control.BackColor = Input; control.ForeColor = Text;
                if (control is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
                else if (control is ListBox list) list.BorderStyle = BorderStyle.FixedSingle;
                break;
            case Label label:
                label.ForeColor = MapText(label.ForeColor);
                if (IsLight(label.BackColor)) label.BackColor = Surface;
                label.ForeColorChanged += (_, _) => { var mapped = MapText(label.ForeColor); if (mapped != label.ForeColor) label.ForeColor = mapped; };
                break;
            default:
                if (IsLight(control.BackColor)) control.BackColor = Canvas;
                control.ForeColor = Text;
                break;
        }
    }

    // 밝은 화면에서 쓰던 글자색 → 어두운 화면에서 읽히는 색
    private static Color MapText(Color color)
    {
        if (color == Color.Firebrick || color == Color.Red) return Bad;
        if (color == Color.DimGray || color == Color.Gray) return Muted;
        if (color == Text || color == Muted || color == Good || color == Bad) return color;
        if (color.G > color.R + 40 && color.GetBrightness() < 0.45f) return Good; // 짙은 초록·청록 안내 글자
        return color.GetBrightness() < 0.45f ? Text : color;
    }

    private static bool IsLight(Color c) => c.A > 0 && c.GetBrightness() > 0.8f;
}
