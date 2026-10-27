using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using _01_AdminApp.Services;
using ProductionLine.Shared;

namespace _01_AdminApp
{
    public sealed class UserManagementForm : Form
    {
        private readonly IUserService _users;
        private readonly IDashboardService _dashboard;
        private readonly DataGridView _grid = new DataGridView();
        private readonly TextBox _search = new TextBox();
        private readonly TextBox _id = new TextBox();
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _password = new TextBox();
        private readonly CheckBox _active = new CheckBox { Text = "활성 계정", Checked = true, AutoSize = true };
        private readonly CheckedListBox _allowed = new CheckedListBox();
        private readonly FlowLayoutPanel _toolbar = new FlowLayoutPanel();
        private readonly TableLayoutPanel _editor = new TableLayoutPanel();
        private readonly Label _status = new Label();
        private readonly Button _save = new Button();
        private readonly Button _deactivate = new Button();
        private UserDto? _selected;
        private List<UserDto> _allUsers = new List<UserDto>();  // 검색과 상관없는 전체 사용자 (아이디 중복 확인용)
        private bool _busy;
        private bool _binding;

        public UserManagementForm(IUserService users, IDashboardService dashboard)
        {
            AdminTheme.AttachOnLoad(this);
            _users = users;
            _dashboard = dashboard;
            Text = "사용자 관리";
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("맑은 고딕", 10F);
            BackColor = Color.FromArgb(244, 247, 251);
            BuildScreen();
            Shown += async (_, _) => await RunAsync(async () =>
            {
                var lines = await _dashboard.GetLinesAsync();
                if (IsDisposed) return;
                _allowed.DataSource = lines;
                _allowed.DisplayMember = nameof(LineStatusDto.Name);
                await ReloadAsync();
            }, "사용자와 접근 라인 목록을 불러왔습니다.");
        }

        private void BuildScreen()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(18),
                RowCount = 4, ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            root.Controls.Add(new Label
            {
                Text = "사용자 조회 · 추가 · 수정 · 비활성화",
                Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 18F, FontStyle.Bold)
            }, 0, 0);

            _toolbar.Dock = DockStyle.Fill;
            _toolbar.WrapContents = false;
            _search.Width = 180;
            _search.PlaceholderText = "ID 또는 이름 검색";
            _toolbar.Controls.Add(_search);
            var search = Button("검색 / 새로고침");
            search.Click += async (_, _) => await RunAsync(
                () => ReloadAsync(), "목록을 갱신했습니다.");
            var add = Button("신규 사용자");
            add.Click += (_, _) => ResetEditor();
            _toolbar.Controls.Add(search);
            _toolbar.Controls.Add(add);
            root.Controls.Add(_toolbar, 0, 1);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.MultiSelect = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.BackgroundColor = Color.White;
            AddColumn("Id", "ID");
            AddColumn("Name", "이름");
            AddColumn("Role", "권한");
            AddColumn("IsActive", "상태");
            _grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                string key = _grid.Columns[e.ColumnIndex].DataPropertyName;
                if (key == "Role" && e.Value is UserRole role)
                {
                    e.Value = role == UserRole.Admin ? "관리자" : "작업자";
                    e.FormattingApplied = true;
                }
                if (key == "IsActive" && e.Value is bool active)
                {
                    e.Value = active ? "활성" : "비활성";
                    e.CellStyle!.ForeColor = active ? Color.SeaGreen : Color.Firebrick;
                    e.FormattingApplied = true;
                }
            };
            _grid.SelectionChanged += (_, _) =>
            {
                if (!_binding && _grid.CurrentRow?.DataBoundItem is UserDto user)
                    SelectUser(user);
            };
            body.Controls.Add(_grid, 0, 0);

            _editor.Dock = DockStyle.Fill;
            _editor.Padding = new Padding(15, 0, 0, 0);
            _editor.ColumnCount = 1;
            _editor.RowCount = 11;
            float[] heights = { 26, 34, 26, 34, 48, 34, 35, 28 };
            foreach (float height in heights)
                _editor.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            _editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _editor.Controls.Add(Label("사용자 ID (등록 후 변경 불가)"), 0, 0);
            _editor.Controls.Add(_id, 0, 1);
            _editor.Controls.Add(Label("사용자 이름"), 0, 2);
            _editor.Controls.Add(_name, 0, 3);
            _editor.Controls.Add(Label("비밀번호 (6자 이상)\r\n수정 시 비우면 기존 비밀번호 유지"), 0, 4);
            _password.UseSystemPasswordChar = true;
            _editor.Controls.Add(_password, 0, 5);
            _editor.Controls.Add(_active, 0, 6);
            _editor.Controls.Add(Label("접근 가능한 라인"), 0, 7);
            _allowed.Dock = DockStyle.Fill;
            _allowed.CheckOnClick = true;
            _editor.Controls.Add(_allowed, 0, 8);
            foreach (var input in new[] { _id, _name, _password }) input.Dock = DockStyle.Fill;
            _save.Dock = DockStyle.Fill;
            _save.Text = "사용자 추가";
            _save.Click += async (_, _) => await SaveAsync();
            _deactivate.Dock = DockStyle.Fill;
            _deactivate.Text = "선택 사용자 비활성화";
            _deactivate.Enabled = false;
            _deactivate.Click += async (_, _) => await DeactivateAsync();
            _editor.Controls.Add(_save, 0, 9);
            _editor.Controls.Add(_deactivate, 0, 10);
            body.Controls.Add(_editor, 1, 0);
            root.Controls.Add(body, 0, 2);
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_status, 0, 3);
            Controls.Add(root);
            ResetEditor();
        }

        private async Task ReloadAsync(bool resetEditor = true)
        {
            var users = await _users.GetUsersAsync();
            if (IsDisposed) return;
            _allUsers = users;
            string query = _search.Text.Trim();
            var filtered = users.Where(x =>
                x.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            _binding = true;
            try
            {
                _grid.DataSource = filtered;
                _grid.ClearSelection();
                _grid.CurrentCell = null;
            }
            finally { _binding = false; }
            if (resetEditor) ResetEditor();
        }

        private void ResetEditor()
        {
            _selected = null;
            _id.Clear(); _name.Clear(); _password.Clear();
            _id.ReadOnly = false;
            _active.Checked = true;
            for (int i = 0; i < _allowed.Items.Count; i++) _allowed.SetItemChecked(i, false);
            SetEditable(true);
            // 서버의 신규 계정 등록은 항상 활성 상태로 생성
            _active.Enabled = false;
            _deactivate.Enabled = false;
            _save.Text = "사용자 추가";
            _status.Text = "신규 등록 시 ID·이름·비밀번호를 입력하세요. 기존 사용자는 목록에서 선택하세요.";
        }

        private void SelectUser(UserDto user)
        {
            _selected = user;
            _id.Text = user.Id;
            _id.ReadOnly = true;
            _name.Text = user.Name;
            _password.Clear();
            _active.Checked = user.IsActive;
            for (int i = 0; i < _allowed.Items.Count; i++)
                _allowed.SetItemChecked(i, user.AllowedLineIds.Contains(((LineStatusDto)_allowed.Items[i]).Id));
            bool editable = user.Role == UserRole.User;
            SetEditable(editable);
            _save.Text = "변경 내용 저장";
            _deactivate.Enabled = editable && user.IsActive;
            _status.Text = editable
                ? "비활성 계정은 활성 계정을 체크하고 저장하면 다시 활성화됩니다."
                : "관리자 계정은 조회만 가능합니다. 이 화면에서는 작업자 계정만 관리합니다.";
        }

        private void SetEditable(bool editable)
        {
            _name.Enabled = editable;
            _password.Enabled = editable;
            _active.Enabled = editable;
            _allowed.Enabled = editable;
            _save.Enabled = editable;
        }

        private async Task SaveAsync()
        {
            if (_busy) return;
            var selected = _selected;
            string id = _id.Text.Trim();
            string name = _name.Text.Trim();
            string password = _password.Text;
            bool active = _active.Checked;
            int[] lines = _allowed.CheckedItems.Cast<LineStatusDto>().Select(x => x.Id).ToArray();
            if (selected == null && !CheckNewUserId(id)) return;
            await RunAsync(async () =>
            {
                if (selected == null)
                    await _users.CreateUserAsync(new CreateUserRequest(id, name, password, lines));
                else
                {
                    if (selected.Role != UserRole.User)
                        throw new InvalidOperationException("작업자 계정만 수정할 수 있습니다.");
                    await _users.UpdateUserAsync(selected.Id, new UpdateUserRequest(
                        name, active, lines, password.Length == 0 ? null : password));
                }
                if (!IsDisposed)
                {
                    // 새 계정이 검색어 때문에 숨겨지지 않도록 전체 목록 표시
                    _search.Clear();
                    await ReloadAsync();
                }
            }, "저장 후 목록 갱신을 완료했습니다.", keepEditorOnError: selected == null);
        }

        // 신규 사용자 ID를 서버에 보내기 전에 규칙과 중복을 먼저 확인해 팝업으로 알림 (김하균 추가)
        // 규칙은 서버(AppStore.IsValidUserId)와 같음. 목록이 오래됐으면 서버가 409로 한 번 더 막음
        private bool CheckNewUserId(string id)
        {
            string? problem = null;
            if (id.Length is < 3 or > 20 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                problem = "사용자 ID는 영어·숫자·_·-만 사용해 3~20자로 입력하세요.\r\n(한글·전각 문자·띄어쓰기·특수문자는 사용할 수 없습니다.)";
            else if (_allUsers.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                problem = $"이미 사용 중인 사용자 ID입니다: {id}\r\n다른 아이디를 입력하세요. (대소문자만 다른 아이디도 같은 아이디로 봅니다.)";
            if (problem == null) return true;
            MessageBox.Show(this, problem, "사용자 추가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _status.Text = problem.Replace("\r\n", " ");
            _id.Focus();
            _id.SelectAll();
            return false;
        }

        private async Task DeactivateAsync()
        {
            var user = _selected;
            if (_busy || user == null || user.Role != UserRole.User || !user.IsActive) return;
            if (MessageBox.Show(this, $"{user.Name} ({user.Id}) 계정을 비활성화할까요?\r\n계정과 기존 업무 기록은 삭제하지 않습니다.",
                "비활성화 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await RunAsync(async () =>
            {
                // 편집 중인 입력값은 저장하지 않고 기존 계정의 활성 상태만 변경
                await _users.UpdateUserAsync(user.Id, new UpdateUserRequest(
                    user.Name, false, user.AllowedLineIds));
                if (!IsDisposed) await ReloadAsync();
            }, "비활성화 후 목록 갱신을 완료했습니다.");
        }

        private async Task RunAsync(Func<Task> action, string success, bool keepEditorOnError = false)
        {
            if (_busy) return;
            _busy = true;
            bool failed = false;
            _toolbar.Enabled = _editor.Enabled = _grid.Enabled = false;
            _status.Text = "처리 중입니다...";
            try
            {
                await action();
                if (!IsDisposed) _status.Text = success;
            }
            catch (Exception ex)
            {
                failed = true;
                // Refresh stale users after a rejected update; preserve the original error.
                // 신규 추가 실패(예: 서버 409 중복)는 입력칸을 비우지 않고 팝업으로 알림
                try { if (!IsDisposed && !Disposing) await ReloadAsync(resetEditor: !keepEditorOnError); } catch { }
                if (!IsDisposed)
                {
                    _status.Text = "처리 또는 목록 갱신 실패: " + ex.Message;
                    if (keepEditorOnError)
                        MessageBox.Show(this, ex.Message, "사용자 추가 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    if (!(failed && keepEditorOnError)) _password.Clear();
                    _toolbar.Enabled = _editor.Enabled = _grid.Enabled = true;
                }
            }
        }

        private static Label Label(string text) => new Label { Text = text, Dock = DockStyle.Fill };
        private static Button Button(string text) => new Button { Text = text, AutoSize = true };
        private void AddColumn(string property, string title) => _grid.Columns.Add(
            new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title,
                SortMode = DataGridViewColumnSortMode.NotSortable });
    }
}
