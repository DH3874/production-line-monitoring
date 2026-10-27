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
    // 서버에서 받은 데이터 안에서 검색. 공용 DTO와 API는 변경하지 않음.
    internal static class InspectionIssueFilter
    {
        public static List<InspectionDto> Inspections(
            IEnumerable<InspectionDto> source, int? lineId,
            InspectionResultType? result, string keyword)
        {
            return source.Where(x =>
                (!lineId.HasValue || x.LineId == lineId.Value) &&
                (!result.HasValue || x.Result == result.Value) &&
                (x.Id.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 x.Reason.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.InspectedAt).ToList();
        }

        public static List<IssueDto> Issues(
            IEnumerable<IssueDto> source, int? lineId,
            IssueStatus? status, string keyword)
        {
            return source.Where(x =>
                (!lineId.HasValue || x.LineId == lineId.Value) &&
                (!status.HasValue || x.Status == status.Value) &&
                (x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 x.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 x.CreatedBy.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 (x.ActionNote ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 x.Id.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.UpdatedAt).ToList();
        }
    }

    public sealed class InspectionIssueForm : Form
    {
        private readonly IDashboardService _dashboard;
        private readonly ILineDetailService _detail;
        private readonly ComboBox _line = Combo();
        private readonly ComboBox _result = Combo();
        private readonly ComboBox _issueStatus = Combo();
        private readonly TextBox _keyword = new TextBox();
        private readonly Button _reload = new Button();
        private readonly Button _apply = new Button();
        private readonly DataGridView _inspectionGrid = Grid();
        private readonly DataGridView _issueGrid = Grid();
        private readonly TextBox _description = new TextBox();
        private readonly InspectionPhotoPanel _photo;
        private readonly Label _status = new Label();
        private readonly TabControl _tabs = new TabControl();
        private List<LineStatusDto> _lines = new List<LineStatusDto>();
        private List<InspectionDto> _inspections = new List<InspectionDto>();
        private List<IssueDto> _issues = new List<IssueDto>();
        private bool _loading;
        private bool _binding;
        private DateTime? _loadedAt;
        private readonly System.Windows.Forms.Timer _autoRefresh = new() { Interval = 1000 };

        public InspectionIssueForm(IDashboardService dashboard, ILineDetailService detail)
        {
            AdminTheme.AttachOnLoad(this);
            _dashboard = dashboard;
            _detail = detail;
            _photo = new InspectionPhotoPanel(detail);
            Text = "검사 결과 및 문제 조회";
            ClientSize = new Size(1180, 780);
            MinimumSize = new Size(1000, 720);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("맑은 고딕", 10F);
            BackColor = Color.FromArgb(244, 247, 251);
            BuildScreen();
            _autoRefresh.Tick += async (_, _) => { if (Enabled) await ReloadAsync(true); };
            Shown += async (_, _) => { await ReloadAsync(); if (!IsDisposed && !Disposing) _autoRefresh.Start(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _autoRefresh.Stop(); _autoRefresh.Dispose(); }
            base.Dispose(disposing);
        }

        private void BuildScreen()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(18),
                ColumnCount = 1, RowCount = 6
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.Controls.Add(new Label
            {
                Text = "검사 결과 및 문제 조회", Dock = DockStyle.Fill,
                Font = new Font("맑은 고딕", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 45, 70)
            }, 0, 0);

            var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _line.Width = 230;
            _result.Width = 105;
            _issueStatus.Width = 125;
            filter.Controls.Add(Caption("라인"));
            filter.Controls.Add(_line);
            filter.Controls.Add(Caption("판정"));
            filter.Controls.Add(_result);
            filter.Controls.Add(Caption("문제 상태"));
            filter.Controls.Add(_issueStatus);
            root.Controls.Add(filter, 0, 1);

            _result.DataSource = new List<Choice<InspectionResultType>>
            {
                new Choice<InspectionResultType>("전체 판정", null),
                new Choice<InspectionResultType>("정상", InspectionResultType.Normal),
                new Choice<InspectionResultType>("불량", InspectionResultType.Defect),
                new Choice<InspectionResultType>("판정 불가", InspectionResultType.Undetermined)
            };
            _issueStatus.DataSource = new List<Choice<IssueStatus>>
            {
                new Choice<IssueStatus>("전체 상태", null),
                new Choice<IssueStatus>("발생", IssueStatus.Occurred),
                new Choice<IssueStatus>("확인", IssueStatus.Acknowledged),
                new Choice<IssueStatus>("조치 중", IssueStatus.InProgress),
                new Choice<IssueStatus>("완료 요청", IssueStatus.CompletionRequested),
                new Choice<IssueStatus>("종결", IssueStatus.Closed)
            };

            var search = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _keyword.Width = 360;
            _keyword.PlaceholderText = "검사 사유 / 문제 제목·내용·등록자 검색";
            _apply.Text = "검색 적용";
            _apply.AutoSize = true;
            _apply.Enabled = false;
            _apply.Click += (_, _) => ApplyFilters();
            _reload.Text = "서버 / 임시 데이터 새로고침";
            _reload.AutoSize = true;
            _reload.Click += async (_, _) => await ReloadAsync();
            search.Controls.Add(_keyword);
            search.Controls.Add(_apply);
            search.Controls.Add(_reload);
            root.Controls.Add(search, 0, 2);
            AcceptButton = _apply;

            AddColumn(_inspectionGrid, "LineId", "라인", 150);
            AddColumn(_inspectionGrid, "Result", "판정", 75);
            AddColumn(_inspectionGrid, "Reason", "판정 사유", 230);
            AddColumn(_inspectionGrid, "InspectedAt", "검사 시각", 150, "MM-dd HH:mm:ss");
            AddColumn(_issueGrid, "LineId", "라인", 150);
            AddColumn(_issueGrid, "Title", "문제 제목", 200);
            AddColumn(_issueGrid, "Type", "문제 유형", 110);
            AddColumn(_issueGrid, "Status", "처리 상태", 100);
            AddColumn(_issueGrid, "CreatedBy", "등록자", 90);
            AddColumn(_issueGrid, "UpdatedAt", "변경 시각", 150, "MM-dd HH:mm:ss");
            _inspectionGrid.CellFormatting += FormatCell;
            _issueGrid.CellFormatting += FormatCell;
            _inspectionGrid.CurrentCellChanged += (_, _) => ShowDetails();
            _issueGrid.SelectionChanged += (_, _) => ShowDetails();

            _tabs.Dock = DockStyle.Fill;
            var inspectionTab = new TabPage("검사 결과");
            inspectionTab.Controls.Add(_inspectionGrid);
            var issueTab = new TabPage("문제 목록");
            issueTab.Controls.Add(_issueGrid);
            _tabs.TabPages.Add(inspectionTab);
            _tabs.TabPages.Add(issueTab);
            _tabs.SelectedIndexChanged += (_, _) =>
            {
                _result.Enabled = _tabs.SelectedIndex == 0;
                _issueStatus.Enabled = _tabs.SelectedIndex == 1;
                ShowDetails();
            };
            _issueStatus.Enabled = false;
            root.Controls.Add(_tabs, 0, 3);

            _description.Multiline = true;
            _description.ReadOnly = true;
            _description.ScrollBars = ScrollBars.Vertical;
            _description.Dock = DockStyle.Fill;
            _description.BackColor = Color.White;
            var detailArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            detailArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            detailArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            detailArea.Controls.Add(_description,0,0); detailArea.Controls.Add(_photo,1,0);
            root.Controls.Add(detailArea, 0, 4);
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_status, 0, 5);
            Controls.Add(root);
        }

        private async Task ReloadAsync(bool automatic = false)
        {
            if (_loading || IsDisposed || Disposing) return;
            _loading = true;
            _reload.Enabled = _apply.Enabled = false;
            if (!automatic) _status.Text = "라인별 최근 검사 결과와 문제 목록을 불러오는 중입니다...";
            try
            {
                var linesTask = _dashboard.GetLinesAsync();
                var issuesTask = _dashboard.GetIssuesAsync();
                // 두 요청을 함께 기다려 한 요청이 실패해도 다른 오류를 놓치지 않음
                await Task.WhenAll(linesTask, issuesTask);
                var lines = await linesTask;
                var issues = await issuesTask;
                var batches = await Task.WhenAll(lines.Select(
                    x => _detail.GetInspectionsAsync(x.Id)));
                if (IsDisposed || Disposing) return;

                int? previous = (_line.SelectedItem as Choice<int>)?.Value;
                var choices = new List<Choice<int>> { new Choice<int>("전체 라인", null) };
                choices.AddRange(lines.Select(x => new Choice<int>(x.Name, x.Id)));
                _line.DataSource = choices;
                _line.SelectedIndex = Math.Max(0, choices.FindIndex(x => x.Value == previous));
                // 모든 요청에 성공한 경우에만 기존 목록 교체
                _lines = lines;
                _issues = issues;
                _inspections = batches.SelectMany(x => x).ToList();
                _loadedAt = DateTime.Now;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    _status.Text = "조회 실패: " + ex.Message +
                        (_loadedAt.HasValue ? "\r\n화면에는 이전 조회 결과가 남아 있습니다." : "");
            }
            finally
            {
                _loading = false;
                if (!IsDisposed && !Disposing)
                {
                    _reload.Enabled = true;
                    _apply.Enabled = _loadedAt.HasValue;
                }
            }
        }

        private void ApplyFilters()
        {
            int? lineId = (_line.SelectedItem as Choice<int>)?.Value;
            var result = (_result.SelectedItem as Choice<InspectionResultType>)?.Value;
            var status = (_issueStatus.SelectedItem as Choice<IssueStatus>)?.Value;
            string keyword = _keyword.Text.Trim();
            var inspections = InspectionIssueFilter.Inspections(_inspections, lineId, result, keyword);
            var issues = InspectionIssueFilter.Issues(_issues, lineId, status, keyword);
            _binding = true;
            try
            {
                InspectionGridBinding.Bind(_inspectionGrid, inspections, x => x.Id);
                InspectionGridBinding.Bind(_issueGrid, issues, x => x.Id);
            }
            finally { _binding = false; }
            ShowDetails();
            _status.Text = $"자동 새로고침 1초 · 검색 결과: 검사 {inspections.Count}건 / 문제 {issues.Count}건  |  데이터 조회: {_loadedAt:HH:mm:ss}" +
                "\r\n검사 이력은 서버 기준 라인별 최근 최대 100건 안에서 검색합니다. 전체 기간 조회가 아닙니다.";
        }

        private void ShowDetails()
        {
            if (_binding || IsDisposed) return;
            _ = _photo.ShowInspectionAsync(_tabs.SelectedIndex == 0 ? _inspectionGrid.CurrentRow?.DataBoundItem as InspectionDto : null);
            if (_tabs.SelectedIndex == 0 && _inspectionGrid.CurrentRow?.DataBoundItem is InspectionDto inspection)
            {
                _description.Text = $"검사 ID: {inspection.Id}\r\n라인: {LineName(inspection.LineId)}" +
                    $"  |  판정: {ResultText(inspection.Result)}  |  검사 시각: {inspection.InspectedAt:yyyy-MM-dd HH:mm:ss}" +
                    $"\r\n판정 사유: {inspection.Reason}";
            }
            else if (_tabs.SelectedIndex == 1 && _issueGrid.CurrentRow?.DataBoundItem is IssueDto issue)
            {
                _description.Text = $"문제 ID: {issue.Id}\r\n{issue.Title}  |  {LineName(issue.LineId)}" +
                    $"  |  상태: {StatusText(issue.Status)}  |  등록자: {issue.CreatedBy}" +
                    $"\r\n발생: {issue.CreatedAt:yyyy-MM-dd HH:mm:ss}  |  변경: {issue.UpdatedAt:yyyy-MM-dd HH:mm:ss}" +
                    $"\r\n문제 내용: {issue.Description}\r\n조치 내용: {issue.ActionNote ?? "미입력"}";
            }
            else _description.Text = "목록에서 항목을 선택하면 상세 내용이 표시됩니다.";
        }

        private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || sender is not DataGridView grid) return;
            string property = grid.Columns[e.ColumnIndex].DataPropertyName;
            if (property == "LineId" && e.Value is int id) e.Value = LineName(id);
            else if (e.Value is InspectionResultType result)
            {
                e.Value = ResultText(result);
                e.CellStyle!.ForeColor = result == InspectionResultType.Normal ? Color.SeaGreen :
                    result == InspectionResultType.Defect ? Color.Firebrick : Color.DarkOrange;
            }
            else if (e.Value is IssueStatus status) e.Value = StatusText(status);
            else if (e.Value is IssueType type) e.Value = type == IssueType.ProductionLine ? "생산라인" : "검사 시스템";
            else return;
            e.FormattingApplied = true;
        }

        private string LineName(int id) => _lines.FirstOrDefault(x => x.Id == id)?.Name ?? $"LINE {id}";
        private static string ResultText(InspectionResultType value) => value switch
        {
            InspectionResultType.Normal => "정상",
            InspectionResultType.Defect => "불량",
            _ => "판정 불가"
        };
        private static string StatusText(IssueStatus value) => value switch
        {
            IssueStatus.Occurred => "발생", IssueStatus.Acknowledged => "확인",
            IssueStatus.InProgress => "조치 중", IssueStatus.CompletionRequested => "완료 요청",
            IssueStatus.Closed => "종결", _ => value.ToString()
        };
        private static ComboBox Combo() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(8, 7, 3, 0) };
        private static DataGridView Grid() => new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White, RowTemplate = { Height = 36 }
        };
        private static void AddColumn(DataGridView grid, string property, string title, float width, string? format = null)
        {
            var column = new DataGridViewTextBoxColumn
            {
                DataPropertyName = property, HeaderText = title, FillWeight = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            if (format != null) column.DefaultCellStyle.Format = format;
            grid.Columns.Add(column);
        }
        private sealed class Choice<T> where T : struct
        {
            public string Name { get; }
            public T? Value { get; }
            public Choice(string name, T? value) { Name = name; Value = value; }
            public override string ToString() => Name;
        }
    }
}
