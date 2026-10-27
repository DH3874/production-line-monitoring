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
    internal sealed class InspectionTotals
    {
        public int Total { get; }
        public int Normal { get; }
        public int Defect { get; }
        public int Undetermined { get; }
        public double? DefectRate => Normal + Defect == 0 ? null : Defect * 100.0 / (Normal + Defect);
        public InspectionTotals(IEnumerable<InspectionDto> source)
        {
            var items = source.ToList();
            Total = items.Count;
            Normal = items.Count(x => x.Result == InspectionResultType.Normal);
            Defect = items.Count(x => x.Result == InspectionResultType.Defect);
            Undetermined = items.Count(x => x.Result == InspectionResultType.Undetermined);
        }
    }

    internal static class InspectionHistoryFilter
    {
        public static List<InspectionDto> Scope(IEnumerable<InspectionDto> items,
            IEnumerable<LineStatusDto> lines, int? lineId, bool real,
            DateTime? from, DateTime? to)
        {
            if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
                throw new InvalidOperationException("시작일은 종료일보다 늦을 수 없습니다.");
            var allowed = lines.Where(x => lineId.HasValue ? x.Id == lineId.Value : x.IsReal == real)
                .Select(x => x.Id).ToHashSet();
            return items.Where(x => allowed.Contains(x.LineId) &&
                (!from.HasValue || x.InspectedAt.LocalDateTime.Date >= from.Value.Date) &&
                (!to.HasValue || x.InspectedAt.LocalDateTime.Date <= to.Value.Date))
                .OrderByDescending(x => x.InspectedAt).ToList();
        }
    }

    public sealed class InspectionHistoryForm : Form
    {
        private readonly IDashboardService _dashboard;
        private readonly ILineDetailService _detail;
        private readonly ComboBox _scope = Combo();
        private readonly ComboBox _result = Combo();
        private readonly DateTimePicker _from = new DateTimePicker();
        private readonly DateTimePicker _to = new DateTimePicker();
        private readonly CheckBox _useDate = new CheckBox();
        private readonly Button _apply = new Button();
        private readonly Button _reload = new Button();
        private readonly Label _summary = new Label();
        private readonly Label _status = new Label();
        private readonly DataGridView _history = Grid();
        private readonly DataGridView _daily = Grid();
        private readonly TextBox _description = new TextBox();
        private readonly InspectionPhotoPanel _photo;
        private List<LineStatusDto> _lines = new List<LineStatusDto>();
        private List<InspectionDto> _items = new List<InspectionDto>();
        private DateTime? _loadedAt;
        private bool _loading;
        private bool _binding;
        private readonly System.Windows.Forms.Timer _autoRefresh = new() { Interval = 1000 };

        public InspectionHistoryForm(IDashboardService dashboard, ILineDetailService detail)
        {
            AdminTheme.AttachOnLoad(this);
            _dashboard = dashboard;
            _detail = detail;
            _photo = new InspectionPhotoPanel(detail);
            Text = "검사 이력과 기본 통계";
            ClientSize = new Size(1180, 820);
            MinimumSize = new Size(1030, 760);
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
                Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 8
            };
            float[] rows = { 50, 45, 45, 90 };
            foreach (float height in rows) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.Controls.Add(new Label
            {
                Text = "검사 이력과 기본 통계", Dock = DockStyle.Fill,
                Font = new Font("맑은 고딕", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 45, 70)
            }, 0, 0);

            var scopeBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _scope.Width = 260;
            _useDate.Text = "기간 적용";
            _useDate.AutoSize = true;
            _useDate.Margin = new Padding(12, 6, 4, 0);
            _from.Format = _to.Format = DateTimePickerFormat.Short;
            _from.Width = _to.Width = 140;
            _from.Value = DateTime.Today.AddDays(-6);
            _to.Value = DateTime.Today;
            _from.Enabled = _to.Enabled = false;
            _useDate.CheckedChanged += (_, _) => _from.Enabled = _to.Enabled = _useDate.Checked;
            scopeBar.Controls.Add(_scope);
            scopeBar.Controls.Add(_useDate);
            scopeBar.Controls.Add(_from);
            scopeBar.Controls.Add(new Label { Text = "~", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
            scopeBar.Controls.Add(_to);
            root.Controls.Add(scopeBar, 0, 1);

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            toolbar.Controls.Add(new Label { Text = "이력 판정 필터", AutoSize = true, Margin = new Padding(0, 7, 5, 0) });
            _result.Width = 130;
            _result.Items.AddRange(new object[] { "전체 판정", "정상", "불량", "판정 불가" });
            _result.SelectedIndex = 0;
            toolbar.Controls.Add(_result);
            _apply.Text = "검색 적용";
            _apply.AutoSize = true;
            _apply.Enabled = false;
            _apply.Click += (_, _) => ApplySafely();
            _reload.Text = "데이터 새로고침";
            _reload.AutoSize = true;
            _reload.Click += async (_, _) => await ReloadAsync();
            toolbar.Controls.Add(_apply);
            toolbar.Controls.Add(_reload);
            root.Controls.Add(toolbar, 0, 2);
            AcceptButton = _apply;

            _summary.Dock = DockStyle.Fill;
            _summary.BackColor = Color.White;
            _summary.Padding = new Padding(12);
            _summary.ForeColor = Color.FromArgb(25, 65, 110);
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_summary, 0, 3);

            AddColumn(_history, "LineId", "라인", 150);
            AddColumn(_history, "InspectedAt", "검사 시각", 160, "yyyy-MM-dd HH:mm:ss");
            AddColumn(_history, "Result", "판정", 85);
            AddColumn(_history, "Reason", "판정 사유", 250);
            _history.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                string key = _history.Columns[e.ColumnIndex].DataPropertyName;
                if (key == "LineId" && e.Value is int id)
                {
                    e.Value = LineName(id); e.FormattingApplied = true;
                }
                else if (e.Value is InspectionResultType result)
                {
                    e.Value = ResultText(result);
                    e.CellStyle!.ForeColor = result == InspectionResultType.Normal ? Color.SeaGreen :
                        result == InspectionResultType.Defect ? Color.Firebrick : Color.DarkOrange;
                    e.FormattingApplied = true;
                }
            };
            _history.CurrentCellChanged += (_, _) => ShowDetails();
            AddColumn(_daily, "Date", "날짜", 140, "yyyy-MM-dd");
            AddColumn(_daily, "Total", "검사 수", 100);
            AddColumn(_daily, "Normal", "정상", 100);
            AddColumn(_daily, "Defect", "불량", 100);
            AddColumn(_daily, "Undetermined", "판정 불가", 100);
            AddColumn(_daily, "DefectRate", "불량률", 100);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var historyPage = new TabPage("검사 이력"); historyPage.Controls.Add(_history);
            var dailyPage = new TabPage("일별 통계"); dailyPage.Controls.Add(_daily);
            tabs.TabPages.Add(historyPage); tabs.TabPages.Add(dailyPage);
            root.Controls.Add(tabs, 0, 4);

            _description.Dock = DockStyle.Fill;
            _description.Multiline = true;
            _description.ReadOnly = true;
            _description.ScrollBars = ScrollBars.Vertical;
            _description.BackColor = Color.White;
            var detailArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            detailArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            detailArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            detailArea.Controls.Add(_description,0,0); detailArea.Controls.Add(_photo,1,0);
            root.Controls.Add(detailArea, 0, 5);
            root.Controls.Add(new Label
            {
                Text = "통계는 선택한 라인·기간의 전체 판정 기준입니다. 판정 필터는 검사 이력 목록에만 적용됩니다.\r\n불량률 = 불량 ÷ (정상 + 불량) × 100. 판정 불가는 분모에서 제외합니다.",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
            }, 0, 6);
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_status, 0, 7);
            Controls.Add(root);
        }

        private async Task ReloadAsync(bool automatic = false)
        {
            if (_loading || IsDisposed || Disposing) return;
            _loading = true;
            _apply.Enabled = _reload.Enabled = false;
            if (!automatic) _status.Text = "라인별 최근 검사 이력을 조회하는 중입니다...";
            try
            {
                var lines = await _dashboard.GetLinesAsync();
                var batches = await Task.WhenAll(lines.Select(x => _detail.GetInspectionsAsync(x.Id)));
                if (IsDisposed || Disposing) return;
                string previous = (_scope.SelectedItem as ScopeChoice)?.Key ?? "real";
                var choices = new List<ScopeChoice>
                {
                    new ScopeChoice("real", "실제 라인 전체", null, true),
                    new ScopeChoice("virtual", "가상 라인 전체 (별도 집계)", null, false)
                };
                choices.AddRange(lines.Select(x => new ScopeChoice("line:" + x.Id, x.Name, x.Id, x.IsReal)));
                _scope.DataSource = choices;
                _scope.SelectedIndex = Math.Max(0, choices.FindIndex(x => x.Key == previous));
                _lines = lines;
                _items = batches.SelectMany(x => x).GroupBy(x => x.Id).Select(x => x.First()).ToList();
                _loadedAt = DateTime.Now;
                ApplySafely();
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    _status.Text = "조회 실패: " + ex.Message + (_loadedAt.HasValue ? " (이전 결과 유지)" : "");
            }
            finally
            {
                _loading = false;
                if (!IsDisposed && !Disposing)
                {
                    _reload.Enabled = true; _apply.Enabled = _loadedAt.HasValue;
                }
            }
        }

        private void ApplySafely()
        {
            try { ApplyFilters(); }
            catch (InvalidOperationException ex) { _status.Text = ex.Message + " (이전 결과 유지)"; }
        }

        private void ApplyFilters()
        {
            if (_scope.SelectedItem is not ScopeChoice scope) return;
            var scoped = InspectionHistoryFilter.Scope(_items, _lines, scope.LineId, scope.IsReal,
                _useDate.Checked ? _from.Value.Date : null, _useDate.Checked ? _to.Value.Date : null);
            var totals = new InspectionTotals(scoped);
            string rate = Rate(totals.DefectRate);
            _summary.Text = $"{scope.Name}  |  조회된 이력 기준\r\n검사 {totals.Total:N0}건   정상 {totals.Normal:N0}건   불량 {totals.Defect:N0}건   판정 불가 {totals.Undetermined:N0}건   불량률 {rate}";
            InspectionResultType? result = _result.SelectedIndex switch
            {
                1 => InspectionResultType.Normal, 2 => InspectionResultType.Defect,
                3 => InspectionResultType.Undetermined, _ => null
            };
            var visible = scoped.Where(x => !result.HasValue || x.Result == result.Value).ToList();
            _binding = true;
            try { InspectionGridBinding.Bind(_history, visible, x => x.Id); }
            finally { _binding = false; }
            _daily.DataSource = scoped.GroupBy(x => x.InspectedAt.LocalDateTime.Date)
                .OrderByDescending(x => x.Key).Select(group =>
                {
                    var count = new InspectionTotals(group);
                    return new { Date = group.Key, count.Total, count.Normal, count.Defect,
                        count.Undetermined, DefectRate = Rate(count.DefectRate) };
                }).ToList();
            ShowDetails();
            _status.Text = $"자동 새로고침 1초 · 조회: {_loadedAt:HH:mm:ss}  |  이력 표시 {visible.Count}건\r\n서버 기준 라인별 최근 최대 100건 안에서 검색·집계합니다. 전체 기간 통계가 아닙니다.";
        }

        private void ShowDetails()
        {
            if (_binding || IsDisposed) return;
            _ = _photo.ShowInspectionAsync(_history.CurrentRow?.DataBoundItem as InspectionDto);
            _description.Text = _history.CurrentRow?.DataBoundItem is InspectionDto x
                ? $"검사 ID: {x.Id}\r\n{LineName(x.LineId)}  |  {x.InspectedAt:yyyy-MM-dd HH:mm:ss}  |  {ResultText(x.Result)}" +
                  $"\r\n판정 사유: {x.Reason}"
                : "표시할 검사 이력이 없습니다.";
        }

        private static string Rate(double? value) => value.HasValue ? $"{value.Value:0.0}%" : "-";
        private string LineName(int id) => _lines.FirstOrDefault(x => x.Id == id)?.Name ?? $"LINE {id}";
        private static string ResultText(InspectionResultType value) => value switch
        {
            InspectionResultType.Normal => "정상", InspectionResultType.Defect => "불량", _ => "판정 불가"
        };
        private static ComboBox Combo() => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private static DataGridView Grid() => new DataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true,
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
        private sealed class ScopeChoice
        {
            public string Key { get; }
            public string Name { get; }
            public int? LineId { get; }
            public bool IsReal { get; }
            public ScopeChoice(string key, string name, int? lineId, bool real)
            { Key = key; Name = name; LineId = lineId; IsReal = real; }
            public override string ToString() => Name;
        }
    }
}
