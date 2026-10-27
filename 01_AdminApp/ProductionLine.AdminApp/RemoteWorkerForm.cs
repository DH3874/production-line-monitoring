using ProductionLine.ChatUI;
using ProductionLine.Shared;

namespace _01_AdminApp;

public sealed class RemoteWorkerForm : Form
{
    private readonly ProductionLineApiClient _api;
    private readonly int _lineId;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Label _connection = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(8), Text = "작업자 프로그램 연결 확인 중" };
    private readonly Label _feedback = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "명령은 실제 작업자 실행 응답을 받은 뒤 완료로 표시됩니다." };
    private readonly TextBox _workerStatus = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly PictureBox _live = Picture(), _product = Picture(), _historyPhoto = Picture();
    private readonly Label _videoAge = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly SensorTrendPanel _trends = new() { Dock = DockStyle.Fill };
    private readonly Label _hardware = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly NumericUpDown _camera = new() { Minimum = 0, Maximum = 9, Width = 60 };
    private readonly ComboBox _port = Choice(110), _model = Choice(380);
    private readonly CheckBox _sorting = new() { Text = "불량 자동 분류", AutoSize = true, Checked = true };
    private readonly NumericUpDown _boostSeconds = new() { Minimum = 0.5m, Maximum = 60, DecimalPlaces = 1, Increment = 0.5m, Value = 18, Width = 80 };
    private readonly NumericUpDown _travel = new() { Minimum = 500, Maximum = 60000, Increment = 100, Value = 15000, Width = 85 };
    private readonly NumericUpDown _hold = new() { Minimum = 200, Maximum = 60000, Increment = 100, Value = 7000, Width = 85 };
    private readonly Panel _operations = new() { Dock = DockStyle.Fill, AutoScroll = true, Enabled = false };
    private readonly DataGridView _commands = Grid(), _history = Grid();
    private WorkerConnectionDto? _worker;
    private Guid? _initializedClient;
    private bool _busy, _sending, _bindingHistory;
    private int _ticks, _photoGeneration;
    public RemoteWorkerForm(ProductionLineApiClient api, int lineId = 1)
    {
        AdminTheme.AttachOnLoad(this);
        _api = api; _lineId = lineId;
        _model.FormattingEnabled = true;
        _model.Format += (_, e) => { if (e.ListItem as string == "__all__") e.Value = "전체 학습 모델 사용"; };
        Text = $"LINE {lineId} · 작업자 원격 운영";
        ClientSize = new Size(1380,960); MinimumSize = new Size(1100,800);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("맑은 고딕",10);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,65)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var liveTab = new TabPage("영상 · 원격 제어"); liveTab.Controls.Add(BuildOperations()); tabs.TabPages.Add(liveTab);
        var sensors = new TabPage("온습도 · 가스 그래프");
        var sensorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(12) };
        sensorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));sensorLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        sensorLayout.Controls.Add(_hardware,0,0);sensorLayout.Controls.Add(_trends,0,1);sensors.Controls.Add(sensorLayout);tabs.TabPages.Add(sensors);
        var records = new TabPage("검사 기록 · 제품 사진");
        var recordLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        recordLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));recordLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
        foreach(var (property,title) in new[] { ("InspectedAt","검사 시각"),("Result","판정"),("Reason","검출 결과") })
            _history.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=property,HeaderText=title });
        _history.CellFormatting += (_,e) => { if(e.Value is InspectionResultType result) { e.Value = result switch { InspectionResultType.Normal=>"정상",InspectionResultType.Defect=>"불량",_=>"판정 불가" }; e.FormattingApplied=true; } };
        _history.CurrentCellChanged += async (_,_) => { if(!_bindingHistory) await LoadHistoryPhotoAsync(); };
        recordLayout.Controls.Add(_history,0,0);recordLayout.Controls.Add(_historyPhoto,1,0);records.Controls.Add(recordLayout);tabs.TabPages.Add(records);
        var audit = new TabPage("원격 명령 실행 이력");
        foreach(var title in new[] { "시각","관리자","명령","처리 상태","실행 결과" }) _commands.Columns.Add(title,title);
        audit.Controls.Add(_commands);tabs.TabPages.Add(audit);
        root.Controls.Add(_connection,0,0);root.Controls.Add(tabs,0,1);root.Controls.Add(_feedback,0,2);Controls.Add(root);
        _timer.Tick += async (_,_) => await ReloadAsync();
        Shown += async (_,_) => { await ReloadAsync(); if(!IsDisposed) _timer.Start(); };
        FormClosed += (_,_) => { _timer.Dispose(); _live.Image?.Dispose();_product.Image?.Dispose();_historyPhoto.Image?.Dispose(); };
    }
    private Control BuildOperations()
    {
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=4,Padding=new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,292));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,84));
        var videos=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=2 };
        videos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));videos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        videos.RowStyles.Add(new RowStyle(SizeType.Absolute,28));videos.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        videos.Controls.Add(new Label { Text="작업자 실시간 검사 영상 · 약 1초 갱신",Dock=DockStyle.Fill },0,0);
        videos.Controls.Add(new Label { Text="마지막 인식 제품",Dock=DockStyle.Fill },1,0);
        videos.Controls.Add(_live,0,1);videos.Controls.Add(_product,1,1);
        var rows=new FlowLayoutPanel { Dock=DockStyle.Top,Height=280,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=false };
        FlowLayoutPanel Row(params Control[] controls) { var row=new FlowLayoutPanel { Width=1180,Height=42,WrapContents=false,AutoScroll=true,Margin=Padding.Empty };row.Controls.AddRange(controls);rows.Controls.Add(row);return row; }
        Label Label(string text)=>new() { Text=text,AutoSize=true,Padding=new Padding(0,6,0,0) };
        Row(Label("카메라"),_camera,Command("연결",RemoteAction.ConnectCamera),Command("해제",RemoteAction.DisconnectCamera),Command("검사 시작",RemoteAction.StartInspection),Command("검사 중지",RemoteAction.StopInspection),Command("검사·벨트 정지",RemoteAction.StopAll));
        Row(Label("COM"),_port,Command("포트 갱신",RemoteAction.RefreshPorts),Command("벨트 연결",RemoteAction.ConnectConveyor),Command("연결 해제",RemoteAction.DisconnectConveyor),_remoteRun=Command("벨트 구동",RemoteAction.RunConveyor),Command("벨트 정지",RemoteAction.StopConveyor));
        Row(Command("조명 켜기",RemoteAction.SetLight,true),Command("조명 끄기",RemoteAction.SetLight,false),_remoteReset=Command("긴급 정지 잠금 해제",RemoteAction.ResetEmergency),_estopHint=Label(EstopIdleHint));
        Row(Label("인식 후 2배 가속 유지(초)"),_boostSeconds,Command("가속 시간 적용",RemoteAction.SetBoostDuration),Label("다음 제품 인식부터 적용 · V7 업로드 필요"));
        Row(_sorting,Label("기본속도 이동(ms)"),_travel,Label("55° 유지(ms)"),_hold,Command("분류 설정 적용",RemoteAction.SetSorting));
        Row(Label("모델"),_model,Command("모델 적용",RemoteAction.SelectModel),Label("검사 중지 후 적용"));
        _operations.Controls.Add(rows);
        layout.Controls.Add(videos,0,0);layout.Controls.Add(_videoAge,0,1);layout.Controls.Add(_operations,0,2);layout.Controls.Add(_workerStatus,0,3);return layout;
    }
    // 긴급 정지 잠금 중에는 원격 벨트 구동을 막고, 현장 버튼이 원위치된 뒤에만 원격 잠금 해제를 허용합니다.
    private const string EstopIdleHint="물리 버튼 해제 후 가능 · 자동 재출발 없음";
    private Button _remoteRun=null!, _remoteReset=null!;
    private Label _estopHint=null!;
    private void UpdateEmergencyControls(WorkerConnectionDto? worker)
    {
        var latched=worker is { IsOnline: true } && worker.State.EmergencyStopped;
        var switchReady=worker?.State.EmergencySwitchReady;
        _remoteRun.Enabled=!latched;
        _remoteReset.Enabled=!latched || switchReady!=false;
        _estopHint.Text=!latched?EstopIdleHint:switchReady==false?"긴급 정지 버튼 눌림 · 현장에서 버튼 원위치 필요":"긴급 정지 잠금 중 · 잠금 해제 후 벨트 구동";
        _estopHint.ForeColor=!latched?Color.DimGray:switchReady==false?Color.Firebrick:Color.DarkOrange;
    }
    private Button Command(string text, RemoteAction action, bool? enabled=null)
    {
        var button=new Button { Text=text,AutoSize=true,Height=32 };
        if(action==RemoteAction.StopAll) { button.BackColor=Color.Firebrick;button.ForeColor=Color.White; }
        button.Click += async (_,_) => await SendAsync(action,enabled);
        return button;
    }
    private async Task SendAsync(RemoteAction action,bool? enabled)
    {
        if(_sending || _worker?.IsOnline!=true) { _feedback.Text="작업자 프로그램의 온라인 연결을 먼저 확인하세요.";return; }
        if(action==RemoteAction.StopAll && MessageBox.Show(this,
            $"LINE {_lineId}의 검사와 실제 벨트를 모두 정지할까요?\r\n자동 분류도 해제되며, 다시 구동하려면 별도로 벨트 구동을 눌러야 합니다.",
            "검사·벨트 전체 정지 확인",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
        _sending=true;_operations.Enabled=false;
        try
        {
            var command=await _api.CreateRemoteCommandAsync(_lineId,new(Guid.NewGuid(),_worker.ClientId,action,
                CameraIndex:(int)_camera.Value,Port:_port.SelectedItem as string,Model:_model.SelectedItem as string,
                Enabled:action==RemoteAction.SetSorting?_sorting.Checked:enabled,TravelMs:(int)_travel.Value,HoldMs:(int)_hold.Value,BoostMs:(int)(_boostSeconds.Value*1000)));
            if(!IsDisposed) _feedback.Text=$"{RemoteActionText.Name(action)} · {RemoteActionText.Status(command.Status)} · 실행 이력에서 실제 결과를 확인하세요.";
        }
        catch(Exception ex) { if(!IsDisposed) _feedback.Text="명령 전달 실패: "+ex.Message+" · 자동 재전송하지 않습니다."; }
        finally { _sending=false;if(!IsDisposed) _operations.Enabled=_worker?.IsOnline==true; }
    }
    private static void UpdateChoices(ComboBox box,IEnumerable<string> values,string? current)
    {
        var array=values.ToArray();
        if(box.Items.Cast<string>().SequenceEqual(array)) return;
        var selected=box.SelectedItem as string;
        box.Items.Clear();box.Items.AddRange(array);
        if(selected!=null && box.Items.Contains(selected)) box.SelectedItem=selected;
        else if(current!=null && box.Items.Contains(current)) box.SelectedItem=current;
        else if(box.Items.Count>0) box.SelectedIndex=0;
    }
    private async Task ReloadAsync()
    {
        if(_busy || IsDisposed) return;
        _busy=true;
        try
        {
            var snapshot=await _api.GetRemoteLineAsync(_lineId);
            var samples=await _api.GetHardwareHistoryAsync(_lineId);
            if(IsDisposed) return;
            _worker=snapshot.Worker;
            _operations.Enabled=_worker?.IsOnline==true && !_sending;
            UpdateEmergencyControls(_worker);
            _connection.Text=_worker==null?"작업자 미연결 · 작업자 앱을 실행하고 같은 서버로 로그인하세요."
                :$"{(_worker.IsOnline?"온라인":"오프라인 · 원격 제어 불가")} · {_worker.UserId} / {_worker.State.MachineName} · 마지막 수신 {_worker.ReceivedAt.ToLocalTime():HH:mm:ss}\r\n이더넷/LAN 서버 경유 · 물리 긴급 정지는 원격으로 우회할 수 없습니다.";
            _connection.ForeColor=_worker?.IsOnline==true?Color.FromArgb(0,110,90):Color.Firebrick;
            _trends.SetSamples(samples);
            _hardware.Text=HardwareStatusText.Format(samples.LastOrDefault());
            if(_worker is { } worker)
            {
                var s=worker.State;
                UpdateChoices(_port,s.Ports,s.Port);UpdateChoices(_model,s.Models,s.Model);
                if(_initializedClient!=worker.ClientId)
                {
                    _initializedClient=worker.ClientId;
                    _boostSeconds.Value=Math.Clamp(s.BoostDurationMs/1000m,0.5m,60m);_camera.Value=s.CameraIndex;_sorting.Checked=s.AutoSorting;_travel.Value=s.TravelMs;_hold.Value=s.HoldMs;
                }
                SetImage(_live,worker.IsOnline?s.LiveJpeg:null);SetImage(_product,s.LastProductJpeg);
                _videoAge.Text=worker.IsOnline && s.LiveCapturedAt.HasValue && DateTimeOffset.UtcNow-s.LiveCapturedAt.Value < TimeSpan.FromSeconds(4)
                    ?$"원본 프레임 수신 {s.LiveCapturedAt.Value.ToLocalTime():HH:mm:ss.fff} · 카메라 {s.CameraIndex} / 검사 {(s.Inspecting?"진행":"중지")} / 벨트 {(s.BeltRunning?s.SpeedBoosted?"가속 중":"구동":"정지")} / 조명 {(s.LightOn==true?"켜짐":s.LightOn==false?"꺼짐":"미확인")} / 자동 분류 {(s.AutoSorting?"켜짐":"꺼짐")}"
                    :"실시간 영상 없음 / 갱신 지연 · 마지막 인식 사진은 과거 결과입니다.";
                _workerStatus.Text=s.Status;
            }
            else {SetImage(_live,null);SetImage(_product,null);}
            _commands.Rows.Clear();
            foreach(var c in snapshot.Commands) _commands.Rows.Add(c.CreatedAt.ToLocalTime().ToString("HH:mm:ss"),c.Administrator,RemoteActionText.Name(c.Request.Action),RemoteActionText.Status(c.Status),c.Message);
            if(++_ticks%3==1)
            {
                var records=await _api.GetInspectionsAsync(_lineId);
                if(IsDisposed) return;
                var selected=(_history.CurrentRow?.DataBoundItem as InspectionDto)?.Id;
                _bindingHistory=true;
                try
                {
                    _history.DataSource=records.OrderByDescending(r=>r.InspectedAt).ToList();
                    foreach(DataGridViewRow row in _history.Rows) if(row.DataBoundItem is InspectionDto item && item.Id==selected) {_history.CurrentCell=row.Cells[0];break;}
                }
                finally {_bindingHistory=false;}
                if(selected!=(_history.CurrentRow?.DataBoundItem as InspectionDto)?.Id) await LoadHistoryPhotoAsync();
            }
        }
        catch(Exception ex)
        {
            if(!IsDisposed) {_operations.Enabled=false;SetImage(_live,null);_connection.ForeColor=Color.Firebrick;_connection.Text="서버/작업자 상태 확인 실패 · 원격 제어 중지: "+ex.Message;}
        }
        finally {_busy=false;}
    }
    private async Task LoadHistoryPhotoAsync()
    {
        var generation=++_photoGeneration;
        if(_history.CurrentRow?.DataBoundItem is not InspectionDto {HasImage:true} item) {SetImage(_historyPhoto,null);return;}
        try
        {
            var bytes=await _api.GetInspectionImageAsync(item.Id);
            if(!IsDisposed && generation==_photoGeneration) SetImage(_historyPhoto,bytes);
        }
        catch(Exception ex) {if(!IsDisposed && generation==_photoGeneration) {SetImage(_historyPhoto,null);_feedback.Text="기록 사진 조회 실패: "+ex.Message;}}
    }
    private static void SetImage(PictureBox box,byte[]? bytes)
    {
        Image? next=null;
        if(bytes is {Length:>0}) {using var stream=new MemoryStream(bytes);using var decoded=Image.FromStream(stream);next=new Bitmap(decoded);}
        var old=box.Image;box.Image=next;old?.Dispose();
    }
    private static PictureBox Picture()=>new() { Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(16,28,43) };
    private static ComboBox Choice(int width)=>new() { Width=width,DropDownStyle=ComboBoxStyle.DropDownList };
    private static DataGridView Grid()=>new() { Dock=DockStyle.Fill,ReadOnly=true,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
}
