using System;
using System.Drawing;
using System.Windows.Forms;
using _01_AdminApp.Services;
using ProductionLine.Shared;

namespace _01_AdminApp
{
    public partial class AdminLoginForm : Form
    {
        private readonly ProductionLineApiClient? _api;
        private readonly IAdminAuthService _auth;
        private readonly IDashboardService _dashboardService;
        private readonly ILineDetailService _detailService;
        private readonly IUserService _userService;

        private readonly TextBox _id = new TextBox();
        private readonly TextBox _password = new TextBox();
        private readonly Button _loginButton = new Button();
        private readonly Label _message = new Label();
        private readonly ServerConnectionControl _connection;

        public UserDto? LoggedInUser { get; private set; }

        // 디자이너용 생성자
        public AdminLoginForm()
            : this(new MockAdminAuthService(), new MockDashboardService(),
                new MockLineDetailService(), new MockUserService())
        {
        }

        public AdminLoginForm(
            IAdminAuthService auth,
            IDashboardService dashboardService,
            ILineDetailService detailService,
            IUserService userService, ProductionLineApiClient? api = null)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
            _detailService = detailService ?? throw new ArgumentNullException(nameof(detailService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));

            _api = api;
            _connection = new ServerConnectionControl(api?.BaseAddress);
            InitializeComponent();
            BuildScreen();
            AdminTheme.Attach(this);
        }

        private static void CloseChildren(Form form)
        {
            foreach (Form child in form.OwnedForms) CloseChildren(child);
            form.Close();
        }

        private void BuildScreen()
        {
            Text = "생산라인 관리자 로그인";
            ClientSize = new Size(460, 580);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("맑은 고딕", 10F);

            var title = new Label
            {
                Text = "관리자 로그인",
                Location = new Point(40, 35),
                Size = new Size(380, 45),
                Font = new Font("맑은 고딕", 22F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 45, 70),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var subtitle = new Label
            {
                Text = "생산라인 통합 관리 시스템",
                Location = new Point(40, 85),
                Size = new Size(380, 25),
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var idLabel = new Label
            {
                Text = "관리자 ID",
                Location = new Point(60, 135),
                AutoSize = true
            };

            _id.SetBounds(60, 165, 340, 30);
            _id.TabIndex = 0;

            var passwordLabel = new Label
            {
                Text = "비밀번호",
                Location = new Point(60, 210),
                AutoSize = true
            };

            _password.SetBounds(60, 240, 340, 30);
            _password.UseSystemPasswordChar = true;
            _password.TabIndex = 1;

            _loginButton.Text = "로그인";
            _loginButton.SetBounds(60, 295, 340, 45);
            _loginButton.BackColor = Color.FromArgb(35, 100, 180);
            _loginButton.ForeColor = Color.White;
            _loginButton.FlatStyle = FlatStyle.Flat;
            _loginButton.FlatAppearance.BorderSize = 0;
            _loginButton.Cursor = Cursors.Hand;
            _loginButton.TabIndex = 2;
            _loginButton.Click += LoginClicked;

            _message.SetBounds(30, 350, 400, 95);
            _message.ForeColor = Color.Firebrick;
            _message.TextAlign = ContentAlignment.MiddleCenter;

            _connection.SetBounds(25, 455, 410, 110);
            Controls.Add(_connection);

            Controls.AddRange(new Control[]
            {
                title,
                subtitle,
                idLabel,
                _id,
                passwordLabel,
                _password,
                _loginButton,
                _message
            });

            AcceptButton = _loginButton;
            Shown += (_, _) => _id.Focus();
        }

        private async void LoginClicked(object? sender, EventArgs e)
        {
            if (!_loginButton.Enabled)
                return;

            _message.Text = "";
            LoggedInUser = null;

            if (string.IsNullOrWhiteSpace(_id.Text))
            {
                _message.Text = "관리자 ID를 입력하세요.";
                _id.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_password.Text))
            {
                _message.Text = "비밀번호를 입력하세요.";
                _password.Focus();
                return;
            }

            _loginButton.Enabled = false;
            _loginButton.Text = "로그인 중...";
            _id.Enabled = false;
            _password.Enabled = false;

            try
            {
                var user = await _auth.LoginAsync(
                    _id.Text.Trim(),
                    _password.Text);

                if (IsDisposed || Disposing)
                    return;

                if (!user.IsActive)
                {
                    throw new InvalidOperationException(
                        "비활성화된 계정입니다.");
                }

                if (user.Role != UserRole.Admin)
                {
                    throw new InvalidOperationException(
                        "관리자 계정만 로그인할 수 있습니다.");
                }

                LoggedInUser = user;

                _password.Clear();

                using var main = new AdminMainForm(user, _dashboardService, _detailService, _userService, _api);
                Hide();
                bool expired = false;
                void Expire()
                {
                    if (expired || main.IsDisposed) return;
                    expired = true;
                    main.BeginInvoke(new Action(() => CloseChildren(main)));
                }
                ServerCall.SessionExpired += Expire;

                try
                {
                    main.ShowDialog();
                }
                finally
                {
                    ServerCall.SessionExpired -= Expire;
                    if (expired)
                    {
                        LoggedInUser = null;
                        Show();
                        _message.Text = "로그인이 만료되었습니다. 다시 로그인하세요.";
                    }
                    else Close();
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    LoggedInUser = null;
                    _message.Text = ex is System.Net.Http.HttpRequestException
                        ? $"서버에 연결하지 못했습니다.\r\n연결 주소: {_api?.BaseAddress}\r\n내 PC에서는 '내PC에서_한번에실행.cmd'로 실행하세요."
                        : ex is System.Threading.Tasks.TaskCanceledException
                        ? "서버 응답 시간이 초과되었습니다.\r\n서버 실행 상태와 주소를 확인하세요."
                        : ex.Message;
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    _password.Clear();
                    _id.Enabled = true;
                    _password.Enabled = true;
                    _loginButton.Enabled = true;
                    _loginButton.Text = "로그인";

                    if (LoggedInUser == null)
                        _password.Focus();
                }
            }
        }
    }
}


