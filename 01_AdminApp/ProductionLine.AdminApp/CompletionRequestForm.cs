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
    internal static class CompletionRequestFilter
    {
        public static List<IssueDto> Apply(IEnumerable<IssueDto> issues, int? lineId, string keyword, IssueStatus? status = null)
        {
            return issues.Where(x =>
                (status.HasValue ? x.Status == status : x.Status is IssueStatus.InProgress or IssueStatus.CompletionRequested) &&
                (!lineId.HasValue || x.LineId == lineId.Value) &&
                (x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 x.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 x.CreatedBy.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 (x.ActionNote ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.UpdatedAt).ToList();
        }
    }

    public sealed class CompletionRequestForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
        private readonly IDashboardService _service;
        private readonly ComboBox _line = new ComboBox();
        private readonly ComboBox _issueStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        private readonly IssueConversationPanel _chat;
        private Guid? _selectedIssueId;
        private readonly TextBox _keyword = new TextBox();
        private readonly Button _search = new Button();
        private readonly Button _refresh = new Button();
        private readonly DataGridView _grid = new DataGridView();
        private readonly TextBox _details = new TextBox();
        private readonly Label _count = new Label();
        private readonly Label _status = new Label();
        private readonly TextBox _reviewNote = new TextBox();
        private readonly Button _closeIssue = new Button();
        private readonly Button _rejectIssue = new Button();
        private List<LineStatusDto> _lines = new List<LineStatusDto>();
        private List<IssueDto> _issues = new List<IssueDto>();
        private bool _loading;
        private bool _binding;
        private DateTime? _loadedAt;
        private bool _processing;

        public CompletionRequestForm(IDashboardService service, ProductionLineApiClient? api = null)
        {
            AdminTheme.AttachOnLoad(this);
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _chat = new IssueConversationPanel(api);
            Text = "사용자 완료 요청 확인";
            ClientSize = new Size(1150, 900);
            MinimumSize = new Size(980, 820);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("맑은 고딕", 10F);
            BackColor = Color.FromArgb(244, 247, 251);
            BuildScreen();
            Shown += async (_, _) => { await ReloadAsync(); if (!IsDisposed) _timer.Start(); };
            _timer.Tick += async (_, _) => { if (!_processing && !_loading && string.IsNullOrWhiteSpace(_reviewNote.Text)) await ReloadAsync(); };
            FormClosed += (_, _) => _timer.Dispose();
        }

        private void BuildScreen()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(18),
                ColumnCount = 1, RowCount = 7
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            root.Controls.Add(new Label
            {
                Text = "사용자 완료 요청 확인", Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(25, 45, 70),
                Font = new Font("맑은 고딕", 20F, FontStyle.Bold)
            }, 0, 0);

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _line.DropDownStyle = ComboBoxStyle.DropDownList;
            _line.Width = 230;
            _keyword.Width = 280;
            _keyword.PlaceholderText = "제목·내용·등록자·조치 내용 검색";
            _search.Text = "검색 적용";
            _search.AutoSize = true;
            _search.Enabled = false;
            _search.Click += (_, _) => ApplyFilters();
            _refresh.Text = "새로고침";
            _refresh.AutoSize = true;
            _refresh.Click += async (_, _) => await ReloadAsync();
            toolbar.Controls.Add(_line);
            _issueStatus.Items.AddRange(new object[] { "조치 중·완료 요청", "완료 요청", "조치 중" });
            _issueStatus.SelectedIndex = 0;
            _issueStatus.SelectedIndexChanged += (_, _) => ApplyFilters();
            toolbar.Controls.Add(_issueStatus);
            toolbar.Controls.Add(_keyword);
            toolbar.Controls.Add(_search);
            toolbar.Controls.Add(_refresh);
            root.Controls.Add(toolbar, 0, 1);
            AcceptButton = _search;

            _count.Dock = DockStyle.Fill;
            _count.TextAlign = ContentAlignment.MiddleLeft;
            _count.ForeColor = Color.RoyalBlue;
            root.Controls.Add(_count, 0, 2);

            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.BackgroundColor = Color.White;
            _grid.RowTemplate.Height = 38;
            AddColumn("LineId", "라인", 140);
            AddColumn("Title", "문제", 230);
            AddColumn("Status", "상태", 100);
            AddColumn("Type", "유형", 100);
            AddColumn("CreatedBy", "문제 등록자", 110);
            AddColumn("ActionNote", "사용자 조치 내용", 250);
            AddColumn("UpdatedAt", "최근 변경 시각", 150, "MM-dd HH:mm:ss");
            _grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                string key = _grid.Columns[e.ColumnIndex].DataPropertyName;
                if (key == "LineId" && e.Value is int id)
                {
                    e.Value = LineName(id);
                    e.FormattingApplied = true;
                }
                else if (key == "Type" && e.Value is IssueType type)
                {
                    e.Value = type == IssueType.ProductionLine ? "생산라인" : "검사 시스템";
                    e.FormattingApplied = true;
                }
                else if (key == "Status" && e.Value is IssueStatus status)
                { e.Value = UiText.Status(status); e.FormattingApplied = true; }
            };
            _grid.SelectionChanged += (_, _) => ShowDetails();
            root.Controls.Add(_grid, 0, 3);

            _details.Dock = DockStyle.Fill;
            _details.Multiline = true;
            _details.ReadOnly = true;
            _details.BackColor = Color.White;
            _details.ScrollBars = ScrollBars.Vertical;
            var detailsAndChat = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            detailsAndChat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            detailsAndChat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            detailsAndChat.Controls.Add(_details, 0, 0); detailsAndChat.Controls.Add(_chat, 1, 0);
            root.Controls.Add(detailsAndChat, 0, 4);

            var review = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            review.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            review.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            review.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            review.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _reviewNote.Dock = DockStyle.Fill;
            _reviewNote.Multiline = true;
            _reviewNote.ScrollBars = ScrollBars.Vertical;
            _reviewNote.PlaceholderText = "종결 사유 또는 반려 사유를 입력하세요. (필수)";
            review.Controls.Add(_reviewNote, 0, 0);
            review.SetRowSpan(_reviewNote, 2);
            _closeIssue.Text = "선택 문제 종결";
            _closeIssue.Dock = DockStyle.Fill;
            _closeIssue.Click += async (_, _) => await ReviewAsync(IssueStatus.Closed);
            _rejectIssue.Text = "반려 · 재조치 요청";
            _rejectIssue.Dock = DockStyle.Fill;
            _rejectIssue.Click += async (_, _) => await ReviewAsync(IssueStatus.InProgress);
            review.Controls.Add(_closeIssue, 1, 0);
            review.Controls.Add(_rejectIssue, 1, 1);
            root.Controls.Add(review, 0, 5);
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_status, 0, 6);
            Controls.Add(root);
            UpdateButtons();
        }

        private async Task ReloadAsync()
        {
            if (_loading || _processing) return;
            _loading = true;
            _refresh.Enabled = _search.Enabled = false;
            UpdateButtons();
            _status.Text = "완료 요청 목록을 불러오는 중입니다...";
            try
            {
                var linesTask = _service.GetLinesAsync();
                var issuesTask = _service.GetIssuesAsync();
                await Task.WhenAll(linesTask, issuesTask);
                var lines = await linesTask;
                var issues = await issuesTask;
                if (IsDisposed || Disposing) return;

                int? previous = (_line.SelectedItem as LineChoice)?.Id;
                var choices = new List<LineChoice> { new LineChoice(null, "전체 라인") };
                choices.AddRange(lines.Select(x => new LineChoice(x.Id, x.Name)));
                _line.DataSource = choices;
                _line.SelectedIndex = Math.Max(0, choices.FindIndex(x => x.Id == previous));
                _lines = lines;
                _issues = issues;
                _loadedAt = DateTime.Now;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    _status.Text = "조회 실패: " + ex.Message +
                        (_loadedAt.HasValue ? "\r\n이전 조회 결과를 표시하고 있습니다." : "");
            }
            finally
            {
                _loading = false;
                if (!IsDisposed && !Disposing)
                {
                    _refresh.Enabled = true;
                    _search.Enabled = _loadedAt.HasValue;
                    UpdateButtons();
                }
            }
        }

        private void ApplyFilters()
        {
            int? lineId = (_line.SelectedItem as LineChoice)?.Id;
            var status = _issueStatus.SelectedIndex == 1 ? IssueStatus.CompletionRequested :
                _issueStatus.SelectedIndex == 2 ? (IssueStatus?)IssueStatus.InProgress : null;
            var filtered = CompletionRequestFilter.Apply(_issues, lineId, _keyword.Text.Trim(), status);
            int total = _issues.Count(x => x.Status == IssueStatus.CompletionRequested);
            _binding = true;
            try
            {
                _grid.DataSource = filtered;
                foreach (DataGridViewRow row in _grid.Rows)
                    if (row.DataBoundItem is IssueDto item && item.Id == _selectedIssueId)
                    { _grid.CurrentCell = row.Cells[0]; break; }
            }
            finally { _binding = false; }
            _count.Text = $"전체 완료 요청 {total}건 / 조치 중 {_issues.Count(x => x.Status == IssueStatus.InProgress)}건 / 현재 검색 결과 {filtered.Count}건";
            ShowDetails();
            _status.Text = $"조회 시각: {_loadedAt:HH:mm:ss}  |  조치 중인 문제에서 작업자와 채팅할 수 있습니다." +
                "\r\n처리 사유 입력 후 종결 또는 반려를 선택하세요. 반려는 조치 중 상태로 되돌립니다.";
        }

        private void ShowDetails()
        {
            if (_binding || IsDisposed) return;
            // 다른 문제에 이전 입력 사유를 잘못 적용하지 않도록 초기화
            var selected = _grid.CurrentRow?.DataBoundItem as IssueDto;
            if (_selectedIssueId != selected?.Id) _reviewNote.Clear();
            _selectedIssueId = selected?.Id;
            _chat.SetIssue(selected);
            UpdateButtons();
            if (_grid.CurrentRow?.DataBoundItem is not IssueDto issue)
            {
                _details.Text = "완료 요청이 없거나 검색 결과가 없습니다.";
                return;
            }
            _details.Text = $"문제 ID: {issue.Id}\r\n제목: {issue.Title}" +
                $"\r\n라인: {LineName(issue.LineId)}  |  상태: {UiText.Status(issue.Status)}" +
                $"\r\n문제 등록자: {issue.CreatedBy}" +
                $"\r\n문제 발생: {issue.CreatedAt:yyyy-MM-dd HH:mm:ss}" +
                $"\r\n최근 변경: {issue.UpdatedAt:yyyy-MM-dd HH:mm:ss}" +
                $"\r\n문제 내용: {issue.Description}" +
                $"\r\n사용자 조치 내용: {(string.IsNullOrWhiteSpace(issue.ActionNote) ? "미입력" : issue.ActionNote)}";
        }

        private void UpdateButtons()
        {
            bool available = !_loading && !_processing &&
                _grid.CurrentRow?.DataBoundItem is IssueDto issue &&
                issue.Status == IssueStatus.CompletionRequested;
            _closeIssue.Enabled = _rejectIssue.Enabled = available;
            _reviewNote.Enabled = available;
        }

        private async Task ReviewAsync(IssueStatus target)
        {
            if (_processing || _loading) return;
            if (_grid.CurrentRow?.DataBoundItem is not IssueDto issue) return;
            string reason = _reviewNote.Text.Trim();
            if (reason.Length == 0)
            {
                MessageBox.Show(this, "처리 사유를 입력하세요.", "입력 확인");
                _reviewNote.Focus();
                return;
            }
            string action = target == IssueStatus.Closed ? "종결" : "반려";
            if (MessageBox.Show(this,
                $"문제: {issue.Title}\r\n사유: {reason}\r\n\r\n이 문제를 {action}할까요?",
                action + " 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _processing = true;
            _grid.Enabled = _line.Enabled = _keyword.Enabled = false;
            _refresh.Enabled = _search.Enabled = false;
            UpdateButtons();
            _status.Text = action + " 처리 중입니다...";
            try
            {
                var updated = await _service.UpdateIssueAsync(issue.Id,
                    new UpdateIssueStatusRequest(target, reason));
                if (IsDisposed || Disposing) return;
                int index = _issues.FindIndex(x => x.Id == updated.Id);
                if (index >= 0) _issues[index] = updated;
                _reviewNote.Clear();
                ApplyFilters();
                _status.Text = action + " 처리 완료. 완료 요청 목록에서 제외했습니다." +
                    "\r\n검사 결과 / 문제 조회에서 변경된 상태와 처리 사유를 확인할 수 있습니다.";
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    _status.Text = "처리 결과를 확인하지 못했습니다: " + ex.Message +
                        "\r\n통신 오류이면 서버에 반영되었을 수도 있으니 새로고침 후 확인하세요.";
            }
            finally
            {
                _processing = false;
                string resultMessage = _status.Text;
                if (!IsDisposed && !Disposing) await ReloadAsync();
                if (!IsDisposed && !Disposing)
                {
                    _status.Text = resultMessage;
                    _grid.Enabled = _line.Enabled = _keyword.Enabled = true;
                    _refresh.Enabled = true;
                    _search.Enabled = _loadedAt.HasValue;
                    UpdateButtons();
                }
            }
        }

        private string LineName(int id) => _lines.FirstOrDefault(x => x.Id == id)?.Name ?? $"LINE {id}";
        private void AddColumn(string property, string title, float width, string? format = null)
        {
            var column = new DataGridViewTextBoxColumn
            {
                DataPropertyName = property, HeaderText = title,
                FillWeight = width, SortMode = DataGridViewColumnSortMode.NotSortable
            };
            if (format != null) column.DefaultCellStyle.Format = format;
            _grid.Columns.Add(column);
        }
        private sealed class LineChoice
        {
            public int? Id { get; }
            public string Name { get; }
            public LineChoice(int? id, string name) { Id = id; Name = name; }
            public override string ToString() => Name;
        }
    }
}
