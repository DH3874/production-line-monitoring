using ProductionLine.Shared;
using ProductionLine.ChatUI;

namespace ProductionLine.UserApp;

public sealed partial class UserMainForm : Form
{
    private readonly ProductionLineApiClient _api;
    private readonly GroupChatPanel _groupChat;
    private readonly TabControl _pages = new PageHost { Dock = DockStyle.Fill };
    private readonly ComboBox _lines = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _connection = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 14, 0) };
    private readonly Label _counts = new() { Dock = DockStyle.Top, Height = 64, Font = new Font("Noto Sans KR", 14, FontStyle.Bold) };
    private readonly Label _lineInfo = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label _result = new() { Dock = DockStyle.Top, Height = 80 };
    private readonly Label[] _liveMetricValues = Enumerable.Range(0, 4).Select(_ => new Label { Dock = DockStyle.Fill, Text = "—", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 20, FontStyle.Bold) }).ToArray();
    private readonly Label[] _liveHardwareValues = Enumerable.Range(0, 4).Select(_ => new Label { Dock = DockStyle.Fill, Text = "—", TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White }).ToArray();
    private readonly Label _liveAlert = new() { Dock = DockStyle.Fill, Text = "미해결 문제를 확인 중입니다.", TextAlign = ContentAlignment.MiddleLeft, ForeColor = IndustrialPalette.Amber, Padding = new Padding(12, 0, 0, 0) };
    private readonly Label _videoStatus = new() { Dock = DockStyle.Bottom, Height = 48, Text = "카메라 미연결 · 카메라 연결 버튼을 누르세요." };
    private readonly PictureBox _video = new CameraCanvas { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(16, 28, 43) };
    private readonly DataGridView _history = Grid();
    private readonly DataGridView _issues = Grid();
    private readonly TextBox _issueDetail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None };
    private Button _acknowledge = null!, _saveAction = null!, _requestCompletion = null!;
    private bool _savingAction;
    private readonly TextBox _action = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.None, PlaceholderText = "실제로 점검하거나 조치한 내용을 입력하세요." };
    private readonly RichTextBox _aiMessages = Messages();
    private readonly TextBox _aiInput = ChatInput();
    private readonly Button _aiSend = new() { Text = "AI에게 보내기", Width = 140, Height = 42 };
    // AI 상담 상단 상태 영역: 서버·AI 연결, 응답 결과·시간, 연결 확인/장비 상태 확인 버튼 (공도현 병합 패키지 AiChatStatusPanel)
    private AiChatStatusPanel _aiStatus = null!;
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _frames = new() { Interval = 500 };
    private readonly List<ChatEntry> _conversations = new();
    private readonly Dictionary<Guid, string> _actionDrafts = new();
    private Guid? _actionIssueId;
    private bool _loading, _binding;
    private int _lineId;
    public bool LogoutRequested { get; private set; }
    public bool SessionExpired { get; private set; }

    public UserMainForm(ProductionLineApiClient api)
    {
        _api = api;
        Text = "LINE / OPS · 생산라인 검사 및 설비 모니터링";
        Size = new Size(1500, 940);
        MinimumSize = new Size(1240, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Noto Sans KR", 10);
        BackColor = UserTheme.Canvas;
        Controls.Add(BuildModernShell());
        _groupChat = new GroupChatPanel(api) { Dock = DockStyle.Fill, DarkMode = true, BackColor = IndustrialPalette.Canvas, ForeColor = IndustrialPalette.Text };
        AddPage("실시간 검사", InspectionPage());
        AddPage("검사 결과 · 사진", HistoryPage());
        AddPage("설비 · 센서", EquipmentPage());
        AddPage("문제 조치", IssuePage());
        AddPage("AI 상담", ChatPage());
        AddPage("통계", StatisticsPage());
        AddPage("설정", SettingsPage());
        var groupMenu = _navigation[3];
        _groupChat.UnreadChanged += (_, _) => groupMenu.Text = _groupChat.UnreadCount > 0
            ? $"⚠   문제 조치  ({_groupChat.UnreadCount})"
            : "⚠   문제 조치";
        ApplyModernControls(this);
        _lines.SelectedIndexChanged += async (_, _) =>
        {
            if (_binding || _lines.SelectedItem is not LineStatusDto line) return;
            DisconnectCamera();
            _lineId = line.Id;
            ResetRecognizedResult();
            _video.Image?.Dispose(); _video.Image = null;
            _videoStatus.Text = "카메라 미연결 · 선택한 라인의 카메라를 연결하세요.";
            await RefreshAsync();
        };
        _history.CurrentCellChanged += async (_, _) => { if (!_bindingHistory) await ShowInspectionImageAsync(); };
        _issues.CurrentCellChanged += (_, _) => ShowIssue();
        _aiSend.Click += async (_, _) => await SendAiChatAsync();
        _messageRefresh.Tick += async (_, _) => await PollAdminMessagesAsync();
        Shown += async (_, _) => { await PollAdminMessagesAsync(); if (!IsDisposed) _messageRefresh.Start(); };
        FormClosed += (_, _) => _messageRefresh.Dispose();
        _refresh.Tick += async (_, _) => await RefreshAsync(false);
        _frames.Tick += (_, _) => DisplayCameraFrame();
        Shown += async (_, _) => { _groupChat.Start(); await RefreshAsync(); if (IsDisposed || SessionExpired) return; _refresh.Start(); _frames.Start(); StartRemoteWorker(); };
        FormClosed += (_, _) => { _runningInspection = false; _camera?.Dispose(); _conveyorHeartbeat.Dispose(); _conveyor?.Dispose(); if (!_inspectionBusy) _yoloInspector?.Dispose(); _inspectionTimer.Dispose(); _refresh.Dispose(); _frames.Dispose(); _video.Image?.Dispose(); _recognizedVideo.Image?.Dispose(); _historyImageRequest?.Cancel(); _historyImage.Image?.Dispose(); _groupChat.Dispose(); };
    }

    private Control InspectionPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = Padding.Empty, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        var cameraSide = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 10, 0) };
        cameraSide.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); cameraSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        var camera = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        camera.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); camera.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        camera.Controls.Add(_video, 0, 0); camera.Controls.Add(_videoStatus, 0, 1);
        cameraSide.Controls.Add(Surface(null, camera), 0, 0);
        cameraSide.Controls.Add(Surface(null, BuildLiveOperationControls()), 0, 1);
        layout.Controls.Add(cameraSide, 0, 0);

        var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 32)); summary.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); summary.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        summary.Controls.Add(Surface("현재 검사", BuildCurrentInspectionCard()), 0, 0);
        summary.Controls.Add(Surface("오늘 생산", BuildLiveMetricCards()), 0, 1);
        summary.Controls.Add(Surface("설비 요약", BuildLiveEquipmentSummary()), 0, 2);
        layout.Controls.Add(summary, 1, 0);
        return layout;
    }

    private Control BuildCurrentInspectionCard()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.Controls.Add(_recognizedVideo, 0, 0); layout.SetRowSpan(_recognizedVideo, 2);
        _result.Font = new Font("Noto Sans KR", 10, FontStyle.Bold); _result.Dock = DockStyle.Fill; _result.Height = 0;
        layout.Controls.Add(_result, 1, 0); layout.Controls.Add(_recognizedCaption, 1, 1);
        return layout;
    }

    private Control BuildLiveMetricCards()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        string[] names = ["검사", "정상", "불량", "불량률"];
        Color[] colors = [Color.FromArgb(88,174,255), IndustrialPalette.Green, IndustrialPalette.Red, IndustrialPalette.Red];
        for (int i = 0; i < 4; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var box = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(4), BackColor = Color.FromArgb(23, 39, 55) };
            box.RowStyles.Add(new RowStyle(SizeType.Percent, 42)); box.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            box.Controls.Add(new Label { Dock = DockStyle.Fill, Text = names[i], ForeColor = UiMuted, TextAlign = ContentAlignment.BottomCenter }, 0, 0);
            _liveMetricValues[i].ForeColor = colors[i]; box.Controls.Add(_liveMetricValues[i], 0, 1); grid.Controls.Add(box, i % 2, i / 2);
        }
        return grid;
    }

    private Control BuildLiveEquipmentSummary()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        string[] names = ["온도", "습도", "가스 A0", "분류기"];
        for (int i = 0; i < 4; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var cell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(3), BackColor = Color.FromArgb(23,39,55) };
            cell.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); cell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cell.Controls.Add(new Label { Dock = DockStyle.Fill, Text = names[i], ForeColor = UiMuted, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
            cell.Controls.Add(_liveHardwareValues[i], 0, 1); row.Controls.Add(cell, i, 0);
        }
        root.Controls.Add(row, 0, 0);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "센서 상세 정보는 설비·센서 화면에서 확인", ForeColor = Color.FromArgb(74, 174, 244), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        root.Controls.Add(_liveAlert, 0, 2); return root;
    }

    private Control EquipmentPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = Padding.Empty, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var arduino = Surface("Arduino Mega 2560 연결", BuildArduinoConnectionPanel()); layout.Controls.Add(arduino, 0, 0); layout.SetColumnSpan(arduino, 2);
        var sensors = Surface("환경 센서", _sensorTrends); sensors.Margin = new Padding(0, 0, 10, 10); layout.Controls.Add(sensors, 0, 1);
        layout.Controls.Add(Surface("구동 장치", BuildActuatorPanel()), 1, 1);
        _hardwareSummary.Font = new Font("Noto Sans KR", 9.5f); _hardwareSummary.ForeColor = IndustrialPalette.Green;
        var footer = Surface(null, _hardwareSummary); layout.Controls.Add(footer, 0, 2); layout.SetColumnSpan(footer, 2);
        return layout;
    }

    private Control BuildArduinoConnectionPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0) };
        controls.Controls.Add(new Label { Text = "●  장치 연결", AutoSize = true, ForeColor = IndustrialPalette.Green, Font = new Font("Noto Sans KR", 14, FontStyle.Bold), Padding = new Padding(0, 6, 20, 0) });
        controls.Controls.Add(new Label { Text = "COM 포트", AutoSize = true, ForeColor = UiMuted, Padding = new Padding(0, 8, 4, 0) });
        controls.Controls.Add(_conveyorPort); controls.Controls.Add(Button("포트 갱신", RefreshConveyorPorts)); controls.Controls.Add(_conveyorConnect);
        controls.Controls.Add(Button("연결 해제", async () => await DisconnectConveyorAsync()));
        root.Controls.Add(controls, 0, 0);
        var info = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 0, 0, 0), Margin = Padding.Empty };
        info.Controls.Add(new Label { AutoSize = true, Text = "보드 정보      Arduino Mega 2560", ForeColor = UiMuted });
        info.Controls.Add(new Label { AutoSize = true, Text = "펌웨어 정보   컨베이어 제어 V7", ForeColor = UiMuted });
        info.Controls.Add(_arduinoSetup); root.Controls.Add(info, 1, 0);
        _remoteControlAreas.Add(controls); _remoteControlAreas.Add(info);
        // 벨트 연결 실패·COM 선택 안내·통신 오류 글자를 다시 표시합니다.
        _conveyorState.Dock = DockStyle.Fill; _conveyorState.AutoSize = false; _conveyorState.AutoEllipsis = true;
        _conveyorState.TextAlign = ContentAlignment.MiddleLeft; _conveyorState.ForeColor = UiMuted; _conveyorState.Padding = Padding.Empty;
        root.Controls.Add(_conveyorState, 0, 1); root.SetColumnSpan(_conveyorState, 2);
        return root;
    }

    private Control BuildActuatorPanel()
    {
        UpdateSortTimingSummary();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        for (int i = 1; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        root.Controls.Add(DeviceHeader(), 0, 0);
        root.Controls.Add(DeviceRow("▣  컨베이어 모터", "PUL D5 / DIR D6", _motorStatus, _actuatorRun = Button("벨트 구동", async () => await RunConveyorAsync()), Button("벨트 정지", async () => await StopConveyorAsync("벨트 정지 응답 확인"))), 0, 1);
        var lightStatus = StatusLabel("자동 켜짐", IndustrialPalette.Green);
        void SyncLightStatus() { lightStatus.Text = _lightState.Text.Contains("실패") ? "●  연결 실패" : _lightState.Text.Contains("진행 중") ? "●  작업 중" : _lightState.Text.Contains("꺼") ? "●  꺼짐" : "●  자동 켜짐"; lightStatus.ForeColor = _lightState.Text.Contains("실패") ? IndustrialPalette.Red : IndustrialPalette.Green; }
        _lightState.TextChanged += (_, _) => SyncLightStatus(); SyncLightStatus();
        root.Controls.Add(DeviceRow("●  검사 조명", "D7", lightStatus, Button("조명 켜기", async () => await SetInspectionLightAsync(true)), Button("조명 끄기", async () => await SetInspectionLightAsync(false))), 0, 2);
        var sortStatus = StatusLabel("이동·유지 설정", UiMuted);
        void SyncSortStatus() { sortStatus.Text = $"이동 {_sortTravelMs.Value / 1000m:0.#}s\r\n유지 {_sortHoldMs.Value / 1000m:0.#}s"; }
        _sortTimingSummary.TextChanged += (_, _) => SyncSortStatus(); SyncSortStatus();
        root.Controls.Add(DeviceRow("◫  불량 분류 서보", "D11", sortStatus, _automaticSort), 0, 3);
        root.Controls.Add(DeviceRow("●  긴급 정지 버튼", "NC D12", _emergencyStatus, _emergencyReset = Button("잠금 해제", async () => await ResetEmergencyAsync())), 0, 4);
        return root;
    }

    private static Control DeviceHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        string[] titles = ["장치", "핀 정보", "현재 상태", "제어"];
        for (int i = 0; i < titles.Length; i++)
            header.Controls.Add(new Label { Dock = DockStyle.Fill, Text = titles[i], ForeColor = UiMuted, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(i == 0 ? 10 : 4, 0, 0, 0) }, i, 0);
        return header;
    }

    // 구동 장치 표의 컨베이어·긴급 정지 상태: 고정 글자 대신 실제 장비 보고로 갱신합니다(UpdateLiveHardware).
    private readonly Label _motorStatus = StatusLabel("미확인", UiMuted);
    private readonly Label _emergencyStatus = StatusLabel("미확인", UiMuted);
    private Button? _actuatorRun, _emergencyReset;

    private static Label StatusLabel(string text, Color color) => new()
    {
        Dock = DockStyle.Fill, Text = $"●  {text}", ForeColor = color,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty
    };

    private Control DeviceRow(string name, string pin, Label status, params Control[] actions)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 2, 0, 2), BackColor = Color.FromArgb(21, 36, 51), Padding = new Padding(0, 0, 4, 0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        row.Controls.Add(new Label { Dock = DockStyle.Fill, Text = name, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Noto Sans KR", 9.3f, FontStyle.Bold), Padding = new Padding(10, 0, 0, 0), AutoEllipsis = true }, 0, 0);
        row.Controls.Add(new Label { Dock = DockStyle.Fill, Text = pin, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiMuted, Padding = new Padding(4, 0, 0, 0), AutoEllipsis = true }, 1, 0);
        status.Dock = DockStyle.Fill; status.AutoSize = false; status.AutoEllipsis = true; status.Margin = Padding.Empty; status.Padding = new Padding(4, 0, 4, 0); status.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(status, 2, 0);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = Math.Max(1, actions.Length), RowCount = 1, Margin = Padding.Empty, Padding = new Padding(4, 8, 0, 8) };
        for (int i = 0; i < actions.Length; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / actions.Length));
            actions[i].Dock = DockStyle.Fill; actions[i].Tag = "compact"; actions[i].Margin = new Padding(i == 0 ? 0 : 4, 0, 0, 0);
            panel.Controls.Add(actions[i], i, 0);
        }
        _remoteControlAreas.Add(panel);
        row.Controls.Add(panel, 3, 0); return row;
    }

    private Control SettingsPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(2), Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.Controls.Add(Surface("YOLO 검사 모델", BuildYoloSettingsControls()), 0, 0);
        var guide = new Label { Dock = DockStyle.Fill, ForeColor = UiMuted, Font = new Font("Noto Sans KR", 10), Padding = new Padding(10), Text = "설정 적용 안내\r\n\r\n• 모델 변경은 검사를 중지한 뒤 적용합니다.\r\n• 카메라 번호는 실시간 검사 화면에서 선택합니다.\r\n• COM 포트와 펌웨어는 설비·센서 화면에서 관리합니다.\r\n• 관리자 원격 설정은 작업자 앱에 즉시 반영됩니다." };
        layout.Controls.Add(Surface("운영 안내", guide), 1, 0);
        return layout;
    }

    private Control BuildEquipmentGuide() => new Label
    {
        Dock = DockStyle.Fill, ForeColor = UiMuted, Font = new Font("Noto Sans KR", 9.5f), Padding = new Padding(8),
        Text = "연결 장치\r\n• 컨베이어 PUL D5 / DIR D6\r\n• 검사 조명 D7\r\n• MQ-9 가스 AO → A0\r\n• DHT11 온습도 DATA → D30\r\n• MG996R 분류 서보 D11\r\n• NC 긴급정지 D12"
    };

    private Control HistoryPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _lineInfo.AutoSize = true;
        _lineInfo.AutoEllipsis = false;
        _lineInfo.Margin = Padding.Empty;
        var information = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 1, Margin = Padding.Empty };
        information.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        information.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        information.Controls.Add(_lineInfo, 0, 0);
        var informationCard = new RoundedSurface { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 12, 14, 12), Margin = new Padding(0, 0, 0, 10) };
        informationCard.Controls.Add(information);
        layout.Controls.Add(informationCard, 0, 0);
        layout.Controls.Add(Surface("검사 기록 · 선택하면 오른쪽에 사진 표시", HistoryWithImage()), 0, 1);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "서버에 저장된 개별 검사 결과와 당시 제품 사진을 최대 100건 조회합니다.", ForeColor = UiMuted }, 0, 2);
        return layout;
    }

    private Control IssuePage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(2) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); list.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var register = Button("새 문제 등록", async () => await RegisterIssueAsync()); register.Margin = new Padding(0, 0, 0, 8);
        list.Controls.Add(register, 0, 0); list.Controls.Add(GridWithDarkScrollBar(_issues), 0, 1);
        var listCard = Surface("문제 목록", list); listCard.Margin = new Padding(0, 0, 12, 0); layout.Controls.Add(listCard, 0, 0);
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); detail.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        detail.Controls.Add(Surface("선택한 문제", _issueDetail), 0, 0);
        detail.Controls.Add(Surface("점검 및 처리 내용", _action), 0, 1);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        _acknowledge = IssueActionButton("문제 확인", IssueStatus.Acknowledged);
        _saveAction = IssueActionButton("점검 시작", IssueStatus.InProgress);
        _requestCompletion = IssueActionButton("완료 요청", IssueStatus.CompletionRequested);
        foreach (var button in new[] { _acknowledge, _saveAction, _requestCompletion }) { button.AutoSize = false; button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 0, 8, 0); }
        actions.Controls.Add(_acknowledge, 0, 0); actions.Controls.Add(_saveAction, 1, 0); actions.Controls.Add(_requestCompletion, 2, 0);
        detail.Controls.Add(actions, 0, 2);
        var detailTabs = new DarkTabControl { Dock = DockStyle.Fill };
        var actionTab = new TabPage("점검·조치"); actionTab.Controls.Add(detail);
        var chatTab = new TabPage("관리자 채팅"); chatTab.Controls.Add(BuildIssueChatPanel());
        var groupTab = new TabPage("단체 채팅"); groupTab.Controls.Add(_groupChat);
        detailTabs.TabPages.Add(actionTab); detailTabs.TabPages.Add(chatTab); detailTabs.TabPages.Add(groupTab);
        layout.Controls.Add(detailTabs, 1, 0);
        UpdateIssueActions(); return layout;
    }

    private Control ChatPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(2) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill,
            Text = "공정에서 궁금한 점이나 문제 증상을 바로 질문하세요.\r\n장비의 최근 보고와 매뉴얼을 참고합니다. 장비 상태 확인에서 연결·미확인 항목을 볼 수 있습니다." }, 0, 0);
        _aiStatus = new AiChatStatusPanel(_api, () => _lineId) { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Padding = new Padding(4, 0, 0, 0), MinimumSize = Size.Empty };
        layout.Controls.Add(Surface(null, _aiStatus), 0, 1);
        layout.Controls.Add(Surface("AI와의 대화", _aiMessages), 0, 2);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        _aiInput.PlaceholderText = "문제를 등록하지 않아도 바로 질문할 수 있습니다.";
        _aiInput.MaxLength = 4000;
        composer.Controls.Add(_aiInput, 0, 0); composer.Controls.Add(_aiSend, 1, 0);
        layout.Controls.Add(composer, 0, 3); RenderAiChat();
        // AI 상담 탭을 열 때마다 서버 연결·AI 설정을 확인 (외부 AI를 시험 호출하지는 않음)
        _pages.SelectedIndexChanged += async (_, _) => { if (_pages.SelectedTab?.Controls.Contains(layout) == true) await _aiStatus.CheckConnectionAsync(); };
        return layout;
    }

    private Control StatisticsPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildMetricCards(), 0, 0);
        _counts.Dock = DockStyle.Fill; _counts.Height = 0; _counts.TextAlign = ContentAlignment.MiddleLeft; _counts.ForeColor = UiInk; _counts.Padding = new Padding(0, 2, 0, 0);
        layout.Controls.Add(Surface("누적 검사 요약", _counts), 0, 1);
        layout.Controls.Add(Surface("운영 통계 안내", new Label { Dock = DockStyle.Fill, Text = "검사 결과와 불량률은 서버의 실제 라인 누적값을 기준으로 1초마다 갱신됩니다.\r\n개별 검사 기록과 제품 사진은 ‘검사 결과·사진’ 화면에서 확인할 수 있습니다.", ForeColor = UiMuted, Padding = new Padding(8) }), 0, 2);
        return layout;
    }

    private async Task RefreshAsync(bool showError = true)
    {
        if (_loading || IsDisposed) return;
        _loading = true;
        _lines.Enabled = false;
        try
        {
            var lines = await _api.GetLinesAsync();
            if (IsDisposed) return;
            _binding = true;
            try
            {
                if (!lines.Any(x => x.Id == _lineId)) _lineId = lines.FirstOrDefault()?.Id ?? 0;
                // v6 화면에서는 라인 선택 목록을 창에 넣지 않으므로, 숨긴 목록에도 바인딩이 동작하도록 BindingContext를 직접 지정합니다.
                _lines.BindingContext ??= new BindingContext();
                _lines.DataSource = lines; _lines.DisplayMember = nameof(LineStatusDto.Name);
                _lines.SelectedItem = lines.FirstOrDefault(x => x.Id == _lineId);
            }
            finally { _binding = false; }
            if (_lineId == 0) throw new InvalidOperationException("허용된 라인이 없습니다. 관리자에게 권한을 요청하세요.");
            var requestedLine = _lineId;
            var inspectionsTask = _api.GetInspectionsAsync(requestedLine);
            var issuesTask = _api.GetIssuesAsync();
            await Task.WhenAll(inspectionsTask, issuesTask);
            if (IsDisposed) return;
            var inspections = inspectionsTask.Result.OrderByDescending(x => x.InspectedAt).ToList();
            var issues = issuesTask.Result.Where(x => x.LineId == requestedLine).ToList();
            BindInspectionHistory(inspections);
            BindIssues(issues);
            var line = lines.First(x => x.Id == requestedLine);
            _lineName.Text = line.Name;
            _topOperation.Text = line.OperationStatus.Contains("가동") ? "●  정상 가동" : $"●  {line.OperationStatus}";
            _topOperation.ForeColor = line.OperationStatus.Contains("가동") ? IndustrialPalette.Green : IndustrialPalette.Amber;
            _topCom.Text = _conveyor != null ? $"◇  {_conveyorPort.SelectedItem ?? "COM"} 연결" : "◇  COM 연결 확인";
            _lineInfo.Text = $"{line.Name} · {(line.IsReal ? "실제 라인" : "가상 라인")} · 가동: {line.OperationStatus} · 검사: {line.InspectionStatus}\r\n서버 기록 갱신: {line.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            if (line.IsReal) _lineInfo.Text += "\r\n" + HardwareStatusText.Format(line.Hardware);
            if (_conveyor == null)
            {
                _hardwareSummary.Text = HardwareStatusText.Format(line.Hardware);
                if (line.Hardware != null) _sensorTrends.Append(line.Hardware);
            }
            var total = line.NormalCount + line.DefectCount;
            _counts.Text = $"정상 {line.NormalCount:N0}개     불량 {line.DefectCount:N0}개     판정 불가 {line.UndeterminedCount:N0}개     정상률 {(total == 0 ? 0 : line.NormalCount * 100.0 / total):0.0}%";
            UpdateMetricCards(line);
            UpdateLiveHardware(line.Hardware);
            _liveAlert.Text = issues.Count == 0 ? "●  미해결 문제 없음" : $"⚠  미해결 문제 {issues.Count:N0}건 · 문제 조치 화면에서 확인";
            _liveAlert.ForeColor = issues.Count == 0 ? IndustrialPalette.Green : IndustrialPalette.Amber;
            var latest = inspections.FirstOrDefault();
            if (!_runningInspection && _camera == null && _recognizedVideo.Image == null)
            {
                _result.Text = latest == null ? "검사 결과 대기 중" : latest.DetectionConfidence.HasValue
                    ? $"최근 YOLO 결과: {ResultText(latest.Result)} · 검출 신뢰도 {latest.DetectionConfidence:P1}\r\n{latest.Reason} · {latest.InspectedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                    : $"최근 판정: {ResultText(latest.Result)}\r\n{latest.Reason}    |    {latest.InspectedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
                _result.ForeColor = latest?.Result == InspectionResultType.Defect ? UserTheme.Red : UserTheme.Text;
            }
            _connection.Text = $"연결됨 · 서버 {_api.BaseAddress.Authority} · {DateTime.Now:HH:mm:ss}";
            ShowIssue();
        }
        catch (Exception ex) { await HandleServerFailureAsync(ex, "조회 실패"); if (showError && !IsDisposed && !SessionExpired) MessageBox.Show(this, ex.Message, "조회 오류"); }
        finally { _loading = false; if (!IsDisposed) _lines.Enabled = !_remoteExecuting; }
    }

    private void UpdateLiveHardware(LineHardwareStatusDto? hardware)
    {
        _liveHardwareValues[0].Text = hardware?.TemperatureCelsius is double t ? $"{t:0.0}℃" : "--";
        _liveHardwareValues[1].Text = hardware?.HumidityPercent is double h ? $"{h:0.0}%" : "--";
        _liveHardwareValues[2].Text = hardware?.GasAnalogValue?.ToString() ?? "--";
        _liveHardwareValues[3].Text = hardware?.SorterAngle is int angle ? $"{angle}°" : "--";
        var connected = hardware?.Connected == true;
        _motorStatus.Text = !connected ? "●  미확인" : hardware!.BeltRunning ? "●  구동 중" : "●  정지";
        _motorStatus.ForeColor = connected && hardware!.BeltRunning ? IndustrialPalette.Green : UiMuted;
        // 긴급 정지 잠금 중에는 벨트 구동을 막고, 버튼이 원위치돼 0.2초 지나야 [잠금 해제]를 누를 수 있게 합니다.
        var latched = connected && hardware!.EmergencyStopped;
        var switchReady = (_conveyor as ConveyorSerialController)?.EmergencySwitchReady;
        _emergencyStatus.Text = !connected ? "●  미확인" : !latched ? "●  정상" : switchReady == false ? "●  버튼 눌림" : "●  잠금 중";
        _emergencyStatus.ForeColor = !connected ? UiMuted : !latched ? IndustrialPalette.Green : switchReady == false ? IndustrialPalette.Red : IndustrialPalette.Amber;
        if (_emergencyReset != null) _emergencyReset.Enabled = !latched || switchReady != false;
        if (_actuatorRun != null) _actuatorRun.Enabled = !latched;
        if (latched) _conveyorRun.Enabled = false;
        else if (_conveyor != null && !_conveyorCommandBusy) _conveyorRun.Enabled = true;
    }

    private async Task RegisterIssueAsync()
    {
        if (_lineId == 0) return;
        using var dialog = new IssueReportDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { await SubmitIssueAsync(new CreateIssueRequest(_lineId, dialog.IssueType, dialog.IssueTitle, dialog.Description)); }
        catch (Exception ex) { await HandleServerFailureAsync(ex, "문제 등록 실패"); }
    }

    private async Task SubmitIssueAsync(CreateIssueRequest request)
    {
        await _api.CreateIssueAsync(request);
        await RefreshAfterChangeAsync();
    }

    private async Task SaveActionAsync(IssueStatus status)
    {
        if (_savingAction) return;
        var issue = SelectedIssue();
        if (issue == null) { MessageBox.Show(this, "문제를 선택하세요."); return; }
        if (!CanChangeIssueStatus(issue.Status, status))
        { _connection.Text = "변경할 수 없는 단계입니다. 확인 → 조치 중 → 완료 요청 순서로 처리하세요. 종결은 관리자 담당입니다."; return; }
        var note = status switch
        {
            IssueStatus.Acknowledged => "사용자가 문제를 확인함",
            IssueStatus.InProgress => "사용자가 점검을 시작함",
            _ => _action.Text.Trim()
        };
        if (note.Length == 0) { MessageBox.Show(this, "점검 또는 처리 내용을 입력하세요."); return; }
        _savingAction = true;
        UpdateIssueActions();
        try
        {
            // 과거 조치 이력은 서버가 관리합니다. 이번에 입력한 내용만 전송합니다.
            await _api.UpdateIssueAsync(issue.Id, new UpdateIssueStatusRequest(status, note));
            if (status == IssueStatus.CompletionRequested)
            {
                _actionDrafts.Remove(issue.Id);
                if (_actionIssueId == issue.Id) _action.Clear();
            }
            await RefreshAfterChangeAsync();
        }
        catch (Exception ex) { await HandleServerFailureAsync(ex, "상태 변경 실패 · 입력 유지됨", refreshIssues: true); }
        finally { _savingAction = false; if (!IsDisposed) UpdateIssueActions(); }
    }

    private async Task SendAiChatAsync()
    {
        var question = _aiInput.Text.Trim(); if (question.Length == 0) return;
        _aiSend.Enabled = false;
        try
        {
            // 상태 영역으로 감싸 대기·성공·실패와 응답 시간을 표시 (요청 자체는 기존 자유 상담 그대로)
            var response = await _aiStatus.RunAsync(token => AiConsultationApi.ConsultAsync(_api, new AiConsultRequest(null, question), token));
            if (IsDisposed) return;
            _conversations.Add(new(Guid.Empty, false, "나", question, DateTimeOffset.Now));
            _conversations.Add(new(Guid.Empty, false, "AI", AiReplyStatus.DisplayAnswer(response.Answer) + "\r\n\r\n근거: " + response.ManualSource + (response.NeedAdmin ? "\r\n담당 관리자에게 확인하세요." : ""), DateTimeOffset.Now));
            if (_aiInput.Text.Trim() == question) _aiInput.Clear(); RenderAiChat();
        }
        catch (OperationCanceledException) when (IsDisposed) { }
        catch (Exception ex) { await HandleServerFailureAsync(ex, "AI 상담 전송 실패 · 입력 유지"); }
        finally { if (!IsDisposed) _aiSend.Enabled = true; }
    }

    private void RenderAiChat()
    {
        var text = string.Concat(_conversations.Where(x => x.IssueId == Guid.Empty && !x.Admin).Select(x => $"[{x.Sender} · {x.Time.ToLocalTime():MM-dd HH:mm}]\r\n{x.Text}\r\n\r\n"));
        if (text.Length == 0) text = "AI에게 점검 방법과 문제 해결 순서를 질문해 보세요.";
        if (_aiMessages.Text == text) return;
        _aiMessages.Text = text; _aiMessages.SelectionStart = _aiMessages.TextLength; _aiMessages.ScrollToCaret();
    }

    private void ConfigureIssues()
    {
        foreach (DataGridViewColumn column in _issues.Columns) column.Visible = false;
        foreach (var (name, title) in new[] { (nameof(IssueDto.Title), "문제"), (nameof(IssueDto.Status), "상태"), (nameof(IssueDto.CreatedAt), "발생 시각") })
        { _issues.Columns[name].Visible = true; _issues.Columns[name].HeaderText = title; }
        _issues.CellFormatting -= FormatIssue;
        _issues.CellFormatting += FormatIssue;
    }
    private static void FormatIssue(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.Value is IssueStatus status) { e.Value = StatusText(status); e.FormattingApplied = true; }
        else if (e.Value is DateTimeOffset time) { e.Value = time.ToLocalTime().ToString("MM-dd HH:mm:ss"); e.FormattingApplied = true; }
    }
    private IssueDto? SelectedIssue() => _issues.CurrentRow?.DataBoundItem as IssueDto;
    private void ShowIssue()
    {
        if (_binding) return;
        var issue = SelectedIssue();
        bool issueChanged = _actionIssueId != issue?.Id;
        if (issueChanged)
        {
            if (_actionIssueId is Guid oldId) _actionDrafts[oldId] = _action.Text;
            _actionIssueId = issue?.Id;
            _action.Text = issue != null && _actionDrafts.TryGetValue(issue.Id, out var draft) ? draft : string.Empty;
        }
        var detailText = issue == null ? "문제를 선택하세요." :
            $"[{StatusText(issue.Status)}] {issue.Title}\r\n문제 번호: {issue.Id} · 라인: {issue.LineId} · 종류: {(issue.Type == IssueType.ProductionLine ? "생산라인 문제" : "검사 시스템 문제")}\r\n" +
            $"등록자: {issue.CreatedBy} · 등록: {issue.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · 갱신: {issue.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\r\n" +
            $"증상: {issue.Description}\r\n마지막 조치 메모: {issue.ActionNote ?? "없음"}\r\n" +
            "현재 정보입니다. 누가·언제 변경했는지에 대한 전체 처리 이력은 아직 조회할 수 없습니다.";
        SetIssueDetailText(detailText, preservePosition: !issueChanged);
        ChangeIssueChat();
        UpdateIssueActions();
    }

    private static bool CanChangeIssueStatus(IssueStatus current, IssueStatus next) => (current, next) switch
    {
        (IssueStatus.Occurred, IssueStatus.Acknowledged) => true,
        (IssueStatus.Acknowledged, IssueStatus.InProgress) => true,
        (IssueStatus.InProgress, IssueStatus.CompletionRequested) => true,
        _ => false
    };

    private Button IssueActionButton(string title, IssueStatus status)
    {
        var button = new Button { Text = title, AutoSize = true, Height = 36, Padding = new Padding(8, 2, 8, 2) };
        button.Click += async (_, _) => await SaveActionAsync(status);
        return button;
    }

    private void UpdateIssueActions()
    {
        var issue = SelectedIssue();
        _acknowledge.Enabled = !_savingAction && issue != null && CanChangeIssueStatus(issue.Status, IssueStatus.Acknowledged);
        _saveAction.Enabled = !_savingAction && issue != null && CanChangeIssueStatus(issue.Status, IssueStatus.InProgress);
        _requestCompletion.Enabled = !_savingAction && issue != null && CanChangeIssueStatus(issue.Status, IssueStatus.CompletionRequested);
        _action.ReadOnly = _savingAction || issue == null || issue.Status is IssueStatus.CompletionRequested or IssueStatus.Closed;
    }

    private void AddPage(string title, Control content) { var page = new TabPage(title) { BackColor = UiCanvas }; page.Controls.Add(content); _pages.TabPages.Add(page); }
    private static Button Button(string text, Action action) { var b = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(8, 2, 8, 2) }; b.Click += (_, _) => action(); return b; }
    private static Button Button(string text, Func<Task> action) { var b = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(8, 2, 8, 2) }; b.Click += async (_, _) => { b.Enabled = false; try { await action(); } finally { if (!b.IsDisposed) b.Enabled = true; } }; return b; }
    private static ComboBox IssuePicker() => new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private static RichTextBox Messages() => new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, Font = new Font("Noto Sans KR", 11) };
    private static TextBox ChatInput() => new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.None, PlaceholderText = "메시지를 입력하세요." };
    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToAddRows = false, RowHeadersVisible = false, ScrollBars = ScrollBars.None, BackgroundColor = IndustrialPalette.Surface };
    private static Control GridWithDarkScrollBar(DataGridView grid)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 14));
        layout.Controls.Add(grid, 0, 0); layout.Controls.Add(new DarkGridScrollBar(grid) { Dock = DockStyle.Fill }, 1, 0);
        return layout;
    }
    private static string ResultText(InspectionResultType result) => result switch { InspectionResultType.Normal => "정상", InspectionResultType.Defect => "불량", _ => "판정 불가" };
    private static string StatusText(IssueStatus status) => status switch { IssueStatus.Occurred => "발생", IssueStatus.Acknowledged => "확인", IssueStatus.InProgress => "조치 중", IssueStatus.CompletionRequested => "완료 요청", IssueStatus.Closed => "종결", _ => status.ToString() };
    public sealed record ChatEntry(Guid IssueId, bool Admin, string Sender, string Text, DateTimeOffset Time);
}
