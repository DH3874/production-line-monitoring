using ProductionLine.Shared;

namespace ProductionLine.UserApp;

internal static class UserTheme
{
    public static readonly Color Navy = Color.FromArgb(8, 15, 24);
    public static readonly Color Blue = Color.FromArgb(15, 124, 132);
    public static readonly Color PaleBlue = Color.FromArgb(22, 55, 67);
    public static readonly Color Surface = Color.FromArgb(18, 29, 42);
    public static readonly Color Canvas = Color.FromArgb(10, 17, 26);
    public static readonly Color Border = Color.FromArgb(43, 61, 78);
    public static readonly Color Text = Color.FromArgb(230, 238, 245);
    public static readonly Color Muted = Color.FromArgb(142, 160, 177);
    public static readonly Color Green = Color.FromArgb(50, 205, 132);
    public static readonly Color Red = Color.FromArgb(243, 91, 103);
    public static readonly Color Amber = Color.FromArgb(242, 176, 73);
}

public sealed class UserLoginForm : Form
{
    private readonly TextBox _id = Input("worker1");
    private readonly TextBox _password = Input("worker123!", true);
    private readonly Label _status = new()
    {
        AutoSize = false,
        Height = 36,
        Dock = DockStyle.Top,
        ForeColor = UserTheme.Red,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button _loginButton;

    public UserLoginForm()
    {
        Text = "생산라인 검사 시스템 · 사용자 로그인 [모던 UI v6.3]";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(820, 520);
        Font = new Font("Noto Sans KR", 10);
        BackColor = UserTheme.Canvas;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));

        var brand = new Panel { Dock = DockStyle.Fill, BackColor = UserTheme.Navy, Padding = new Padding(52) };
        var brandContent = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        brandContent.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        brandContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        brandContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        brandContent.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        brandContent.Controls.Add(new Panel(), 0, 0);
        brandContent.Controls.Add(new Label
        {
            Text = "LINE / OPS",
            AutoSize = true,
            ForeColor = Color.FromArgb(113, 212, 199),
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);
        brandContent.Controls.Add(new Label
        {
            Text = "생산라인 검사 및\r\n문제 대응 시스템",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 24, FontStyle.Bold),
            Margin = Padding.Empty
        }, 0, 2);
        brand.Controls.Add(brandContent);

        var loginArea = new Panel { Dock = DockStyle.Fill, BackColor = UserTheme.Canvas };
        var loginCard = new TableLayoutPanel
        {
            Width = 330,
            Height = 365,
            ColumnCount = 1,
            RowCount = 10,
            Anchor = AnchorStyles.None,
            BackColor = UserTheme.Surface
        };
        loginCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        loginCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        loginCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        loginCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        loginCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        loginCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        loginCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        loginCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        loginCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        loginCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        loginCard.Controls.Add(new Label
        {
            Text = "사용자 로그인",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 22, FontStyle.Bold),
            ForeColor = UserTheme.Text
        }, 0, 0);
        loginCard.Controls.Add(new Label
        {
            Text = "관리자가 등록한 계정으로 로그인하세요.",
            AutoSize = true,
            ForeColor = UserTheme.Muted,
            Margin = new Padding(0, 8, 0, 22)
        }, 0, 1);
        loginCard.Controls.Add(FieldLabel("사용자 ID"), 0, 2);
        loginCard.Controls.Add(_id, 0, 3);
        loginCard.Controls.Add(FieldLabel("비밀번호"), 0, 5);
        loginCard.Controls.Add(_password, 0, 6);
        _loginButton = PrimaryButton("로그인");
        _loginButton.Click += LoginClicked;
        loginCard.Controls.Add(_loginButton, 0, 8);
        loginCard.Controls.Add(_status, 0, 9);
        loginArea.Controls.Add(loginCard);
        loginArea.Resize += (_, _) =>
            loginCard.Location = new Point((loginArea.ClientSize.Width - loginCard.Width) / 2, (loginArea.ClientSize.Height - loginCard.Height) / 2);

        layout.Controls.Add(brand, 0, 0);
        layout.Controls.Add(loginArea, 1, 0);
        Controls.Add(layout);
        AcceptButton = _loginButton;
    }

    private async void LoginClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_id.Text) || string.IsNullOrWhiteSpace(_password.Text))
        {
            _status.Text = "아이디와 비밀번호를 입력하세요.";
            return;
        }

        try
        {
            _loginButton.Enabled = false;
            _loginButton.Text = "서버 연결 중...";
            _status.Text = string.Empty;
            using var api = new ProductionLineApiClient();
            var user = await api.LoginAsync(_id.Text.Trim(), _password.Text);
            if (user.Role != UserRole.User)
                throw new InvalidOperationException("일반 사용자 계정으로 로그인하세요.");

            Hide();
            using var main = new UserMainForm(api);
            main.ShowDialog(this);
            if (main.LogoutRequested)
            {
                _password.Clear();
                _status.Text = main.SessionExpired ? "로그인이 만료되었거나 계정이 변경되었습니다. 다시 로그인하세요." : "로그아웃되었습니다.";
                Show();
                _password.Focus();
            }
            else
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _loginButton.Enabled = true;
            _loginButton.Text = "로그인";
        }
    }

    private static TextBox Input(string text, bool password = false) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        UseSystemPasswordChar = password,
        Font = new Font("Noto Sans KR", 11),
        Margin = Padding.Empty
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = UserTheme.Text,
        Font = new Font("Noto Sans KR", 9, FontStyle.Bold),
        TextAlign = ContentAlignment.BottomLeft
    };

    private static Button PrimaryButton(string text) => StyledButton(text, UserTheme.Blue, Color.White);

    internal static Button StyledButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            BackColor = backColor,
            ForeColor = foreColor,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Noto Sans KR", 9.5f, FontStyle.Bold),
            Margin = Padding.Empty
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}

internal sealed class InspectionPreview : Control
{
    public InspectionResultType? Result { get; set; }
    public bool IsInspecting { get; set; }

    public InspectionPreview()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(14, 20, 28);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var area = ClientRectangle;
        using var gridPen = new Pen(Color.FromArgb(31, 45, 59), 1);
        for (var x = 0; x < area.Width; x += 42) g.DrawLine(gridPen, x, 0, x, area.Height);
        for (var y = 0; y < area.Height; y += 42) g.DrawLine(gridPen, 0, y, area.Width, y);

        var conveyor = new Rectangle(28, Math.Max(40, area.Height - 74), Math.Max(80, area.Width - 56), 36);
        using var conveyorBrush = new SolidBrush(Color.FromArgb(52, 63, 76));
        g.FillRectangle(conveyorBrush, conveyor);
        using var rollerPen = new Pen(Color.FromArgb(114, 128, 143), 2);
        for (var x = conveyor.Left + 18; x < conveyor.Right; x += 38)
            g.DrawEllipse(rollerPen, x, conveyor.Top + 8, 20, 20);

        var size = Math.Min(150, Math.Max(90, area.Height - 115));
        var product = new Rectangle((area.Width - size) / 2, Math.Max(20, (area.Height - size) / 2 - 10), size, size);
        var color = Result switch
        {
            InspectionResultType.Normal => UserTheme.Green,
            InspectionResultType.Defect => UserTheme.Red,
            InspectionResultType.Undetermined => UserTheme.Amber,
            _ => Color.FromArgb(66, 130, 194)
        };
        using var productBrush = new SolidBrush(Color.FromArgb(55, color));
        using var productPen = new Pen(color, 3);
        g.FillRoundedRectangle(productBrush, product, 18);
        g.DrawRoundedRectangle(productPen, product, 18);
        using var crossPen = new Pen(Color.FromArgb(140, color), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawLine(crossPen, product.Left - 24, product.Top, product.Right + 24, product.Top);
        g.DrawLine(crossPen, product.Left - 24, product.Bottom, product.Right + 24, product.Bottom);
        g.DrawLine(crossPen, product.Left, product.Top - 18, product.Left, product.Bottom + 18);
        g.DrawLine(crossPen, product.Right, product.Top - 18, product.Right, product.Bottom + 18);

        var status = IsInspecting ? "IMAGE PROCESSING..." : "LIVE  ●";
        using var statusFont = new Font("Consolas", 9, FontStyle.Bold);
        using var statusBrush = new SolidBrush(IsInspecting ? UserTheme.Amber : Color.FromArgb(92, 219, 158));
        g.DrawString(status, statusFont, statusBrush, 12, 10);
        using var guideBrush = new SolidBrush(Color.FromArgb(168, 182, 198));
        g.DrawString("형태·윤곽 / 색상 검사 영역", Font, guideBrush, 12, area.Height - 25);
    }
}

internal sealed class IssueReportDialog : Form
{
    private readonly ComboBox _type = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _title = new() { Dock = DockStyle.Fill, PlaceholderText = "예: 카메라 영상이 표시되지 않음" };
    private readonly TextBox _description = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        PlaceholderText = "발생 시점, 증상, 확인한 내용을 입력하세요."
    };

    public IssueType IssueType => _type.SelectedIndex == 1 ? IssueType.InspectionSystem : IssueType.ProductionLine;
    public string IssueTitle => _title.Text.Trim();
    public string Description => _description.Text.Trim();

    public IssueReportDialog()
    {
        Text = "라인 문제 등록";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 390);
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Font = new Font("Noto Sans KR", 9.5f);
        BackColor = UserTheme.Canvas;
        Padding = new Padding(20);
        _type.Items.AddRange(["설비·라인 문제", "검사 시스템 문제"]);
        _type.SelectedIndex = 0;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(DialogLabel("문제 구분"), 0, 0);
        layout.Controls.Add(_type, 0, 1);
        layout.Controls.Add(DialogLabel("문제 제목"), 0, 2);
        layout.Controls.Add(_title, 0, 3);
        layout.Controls.Add(DialogLabel("상세 내용"), 0, 4);
        layout.Controls.Add(_description, 0, 5);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(220, 8, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var cancel = UserLoginForm.StyledButton("취소", Color.FromArgb(226, 231, 238), UserTheme.Text);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(0, 0, 5, 0);
        var submit = UserLoginForm.StyledButton("등록", UserTheme.Blue, Color.White);
        submit.Margin = new Padding(5, 0, 0, 0);
        submit.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_title.Text))
            {
                MessageBox.Show("문제 제목을 입력하세요.");
                return;
            }
            DialogResult = DialogResult.OK;
        };
        actions.Controls.Add(cancel, 0, 0);
        actions.Controls.Add(submit, 1, 0);
        layout.Controls.Add(actions, 0, 6);
        Controls.Add(layout);
        AcceptButton = submit;
        CancelButton = cancel;
    }

    private static Label DialogLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        ForeColor = UserTheme.Text,
        Font = new Font("Noto Sans KR", 9, FontStyle.Bold)
    };
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = RoundedPath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = RoundedPath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
