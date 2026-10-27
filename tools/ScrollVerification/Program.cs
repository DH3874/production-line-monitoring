using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using ProductionLine.Shared;
using ProductionLine.UserApp;

internal static class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object o,string name) => (T)o.GetType().GetField(name,Flags)!.GetValue(o)!;
    static int Offset(Panel panel) => -(int)panel.GetType().GetProperty("ScrollOffset")!.GetValue(panel)!;
    static void Scroll(Panel panel,int offset) => panel.GetType().GetProperty("ScrollOffset")!.SetValue(panel,offset);
    static Task Refresh(UserMainForm form) => (Task)typeof(UserMainForm).GetMethod("RefreshAsync",Flags)!.Invoke(form,new object[]{false})!;
    static void Check(bool ok,string label) { if(!ok) throw new Exception(label); Console.WriteLine("PASS: "+label); }
    [STAThread] static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware); Application.EnableVisualStyles();
        using var server = new TestServer();
        server.StableHistory = Environment.GetCommandLineArgs().Contains("--history");
        server.CheckIssues = Environment.GetCommandLineArgs().Contains("--issues");
        using var api = new ProductionLineApiClient(server.Url);
        api.LoginAsync("scroll-test","test").GetAwaiter().GetResult();
        using var form = new UserMainForm(api);
        form.Size = new Size(1200,800);
        form.Shown += async (_,_) =>
        {
            try
            {
                while(Field<bool>(form,"_loading")) await Task.Delay(20);
                if(Environment.GetCommandLineArgs().Contains("--modern-ui"))
                {
                    await ModernChecks(form,server);
                    if(Environment.GetCommandLineArgs().Contains("--ui-check"))
                    {
                        Field<TabControl>(form,"_pages").SelectedIndex=0;
                        Field<System.Windows.Forms.Timer>(form,"_refresh").Start();Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Start();
                        await Task.Delay(60000);
                    }
                    return;
                }
                if(server.CheckIssues) { await IssueChecks(form,server); return; }
                if(server.StableHistory)
                {
                    await HistoryChecks(form,server);
                    if(Environment.GetCommandLineArgs().Contains("--ui-check"))
                    {
                        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Start();
                        Field<System.Windows.Forms.Timer>(form,"_refresh").Start();
                        await Task.Delay(60000);
                    }
                    return;
                }
                Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
                Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();
                await Task.Delay(200); // Let the initial Shown/focus/layout work finish before simulating user scroll.
                var pages=Field<TabControl>(form,"_pages");
                var scroll=(Panel)pages.TabPages[0].Controls[0];
                Check((bool)scroll.GetType().GetProperty("CanScroll")!.GetValue(scroll)!,"small window has vertical scroll");
                Scroll(scroll,120);
                await Task.Delay(30);
                int focusOffset=Offset(scroll);
                scroll.ScrollControlIntoView(Field<Label>(form,"_result"));
                Check(Offset(scroll)==focusOffset,"programmatic focus reveal does not jump to top");
                for(int i=0;i<5;i++)
                {
                    Scroll(scroll,120);
                    await Task.Delay(40);
                    int before=Offset(scroll);
                    await Refresh(form); await Task.Delay(60);
                    int after=Offset(scroll);
                    Console.WriteLine($"Refresh {i}: {before} -> {after}");
                    Check(before < 0 && Math.Abs(after-before)<=2,"refresh preserves inspection scroll");
                }
                Scroll(scroll,100);
                var pending=Refresh(form);
                await Task.Delay(30);
                Scroll(scroll,160);
                int during=Offset(scroll);
                await pending; await Task.Delay(80);
                Check(Math.Abs(Offset(scroll)-during)<=2,"scroll while waiting for server is retained");
                Field<System.Windows.Forms.Timer>(form,"_refresh").Start();
                Scroll(scroll,140);
                int timerPosition=Offset(scroll);
                await Task.Delay(3200);
                Check(Math.Abs(Offset(scroll)-timerPosition)<=2,"repeated automatic ticks preserve scroll");
                Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
                Scroll(scroll,0);
                typeof(Control).GetMethod("OnMouseWheel",Flags)!.Invoke(scroll,new object[]{new MouseEventArgs(MouseButtons.None,0,100,100,-120)});
                await Task.Delay(40);
                int wheelPosition=Offset(scroll);
                Check(wheelPosition<0,"mouse wheel scrolls down");
                await Refresh(form); await Task.Delay(70);
                Check(Math.Abs(Offset(scroll)-wheelPosition)<=2,"mouse wheel position survives refresh");
                var bar=scroll.Controls.OfType<VScrollBar>().Single();
                bar.Value=Math.Min(175,bar.Maximum-bar.LargeChange+1);
                Check(Offset(scroll)==-bar.Value,"scrollbar drag changes content position");
                await Refresh(form);
                Check(Offset(scroll)==-bar.Value,"scrollbar position survives changed data");
                var filter=(IMessageFilter)scroll;
                Scroll(scroll,0);
                var wheel=Message.Create(Field<PictureBox>(form,"_video").Handle,0x020A,new IntPtr(unchecked(-120<<16)),IntPtr.Zero);
                Check(filter.PreFilterMessage(ref wheel) && Offset(scroll)<0,"wheel routed from nested camera control");
                var childLayout=scroll.Controls.Cast<Control>().First(c=>c is TableLayoutPanel);
                int childTop=childLayout.Top;
                Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Start();
                Field<System.Windows.Forms.Timer>(form,"_refresh").Start();
                int heartbeats=server.Heartbeats, reads=server.LineReads;
                int overlapOffset=Offset(scroll);
                for(int i=0;i<20;i++)
                {
                    await Task.Delay(250);
                    Check(Offset(scroll)==overlapOffset && childLayout.Top==childTop,"one-second heartbeat + changing sensor/results retain offset "+i);
                }
                Check(server.Heartbeats-heartbeats>=3 && server.LineReads-reads>=3,"both one-second network loops actually executed");
                Check(Field<Label>(form,"_hardwareSummary").Text.Contains("온도"),"sensor text still updates");
                Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();
                Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
                Check(ReferenceEquals(scroll,childLayout.Parent),"content remains attached to viewport");
                form.Size=new Size(1400,900); await Task.Delay(100);
                Scroll(scroll,80);
                int resized=Offset(scroll);
                await Refresh(form); await Task.Delay(60);
                Console.WriteLine($"Resize refresh: {resized} -> {Offset(scroll)}");
                Check(Math.Abs(Offset(scroll)-resized)<=2,"refresh preserves scroll after resize");
                using(var preview=new Bitmap(form.Width,form.Height))
                {
                    form.DrawToBitmap(preview,new Rectangle(Point.Empty,form.Size));
                    preview.Save(Path.Combine(AppContext.BaseDirectory,"scroll-fixed.png"));
                }
                Console.WriteLine("ALL SCROLL CHECKS PASSED; no hardware or production DB used");
            }
            catch(Exception ex){ Console.WriteLine("FAIL: "+ex); Environment.ExitCode=1; }
            finally{ form.Close(); }
        };
        Application.Run(form);
    }
    static async Task HistoryChecks(UserMainForm form,TestServer server)
    {
        Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();
        Field<TabControl>(form,"_pages").SelectedIndex=1;
        await Task.Delay(200);
        var grid=Field<DataGridView>(form,"_history");
        Check(grid.Rows.Count==100,"history has 100 records");
        grid.CurrentCell=grid.Rows[0].Cells[0];
        grid.FirstDisplayedScrollingRowIndex=35;
        var dataSource=grid.DataSource;
        await Refresh(form);
        Check(grid.FirstDisplayedScrollingRowIndex==35,"unchanged one-second refresh retains history scroll");
        Check(ReferenceEquals(dataSource,grid.DataSource),"unchanged records do not rebuild the grid");
        grid.CurrentCell=grid.Rows[40].Cells["사유"];
        Guid selected=(Guid)grid.Rows[40].Cells["검사번호"].Value;
        grid.FirstDisplayedScrollingRowIndex=35;
        Guid top=(Guid)grid.Rows[35].Cells["검사번호"].Value;
        await Refresh(form);
        Check((Guid)grid.CurrentRow!.Cells["검사번호"].Value==selected && grid.CurrentCell.OwningColumn.Name=="사유","selected inspection and column retained");
        server.History.Insert(0,new(Guid.NewGuid(),1,InspectionResultType.Normal,"new inspection",DateTimeOffset.Now.AddMinutes(2)));
        await Refresh(form);
        Check((Guid)grid.Rows[grid.FirstDisplayedScrollingRowIndex].Cells["검사번호"].Value==top,"new inspection keeps same top visible record");
        Check((Guid)grid.CurrentRow!.Cells["검사번호"].Value==selected,"new inspection keeps selected photo record");
        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Start();
        Field<System.Windows.Forms.Timer>(form,"_refresh").Start();
        int heartbeats=server.Heartbeats, reads=server.LineReads;
        for(int i=0;i<16;i++)
        {
            await Task.Delay(250);
            Check((Guid)grid.Rows[grid.FirstDisplayedScrollingRowIndex].Cells["검사번호"].Value==top,"history stays scrolled with both one-second loops "+i);
        }
        Check(server.Heartbeats-heartbeats>=2 && server.LineReads-reads>=2,"history tested while both network loops executed");
        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();
        Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
        using var preview=new Bitmap(form.Width,form.Height);
        form.DrawToBitmap(preview,new Rectangle(Point.Empty,form.Size));
        preview.Save(Path.Combine(AppContext.BaseDirectory,"history-scroll-fixed.png"));
        Console.WriteLine("ALL HISTORY SCROLL CHECKS PASSED");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    static int TopLine(TextBox text)=>(int)SendMessage(text.Handle,0xCE,IntPtr.Zero,IntPtr.Zero);
    static async Task IssueChecks(UserMainForm form,TestServer server)
    {
        Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();
        Field<TabControl>(form,"_pages").SelectedIndex=2;
        await Task.Delay(200);
        var grid=Field<DataGridView>(form,"_issues");
        Check(grid.Rows.Count==100,"problem list has 100 records");
        grid.CurrentCell=grid.Rows[0].Cells[nameof(IssueDto.Title)];
        grid.FirstDisplayedScrollingRowIndex=35;
        var dataSource=grid.DataSource;
        await Refresh(form);
        Check(grid.FirstDisplayedScrollingRowIndex==35,"unchanged one-second refresh preserves problem scroll");
        Check(ReferenceEquals(dataSource,grid.DataSource),"unchanged problem list is not rebound");
        grid.CurrentCell=grid.Rows[40].Cells[nameof(IssueDto.Status)];
        var selected=((IssueDto)grid.CurrentRow!.DataBoundItem).Id;
        grid.FirstDisplayedScrollingRowIndex=35;
        var top=((IssueDto)grid.Rows[35].DataBoundItem).Id;
        var action=Field<TextBox>(form,"_action");
        string draft=string.Join("\r\n",Enumerable.Range(0,60).Select(i=>"작성 중인 조치 메모 "+i));
        action.Text=draft;action.SelectionStart=action.TextLength;action.ScrollToCaret();
        int actionLine=TopLine(action);
        var detail=Field<TextBox>(form,"_issueDetail");
        detail.SelectionStart=detail.TextLength;detail.ScrollToCaret();
        int detailLine=TopLine(detail),caret=detail.SelectionStart;
        Check(detailLine>0,"detail text is scrolled down");
        await Refresh(form);
        Console.WriteLine($"Detail before/after: line {detailLine}/{TopLine(detail)}, caret {caret}/{detail.SelectionStart}, selected {selected}/{((IssueDto)grid.CurrentRow!.DataBoundItem).Id}");
        Check(TopLine(detail)==detailLine && detail.SelectionStart==caret,"unchanged detail preserves read position and selection");
        server.Issues.Insert(0,new(Guid.NewGuid(),1,IssueType.InspectionSystem,"new problem","new symptoms",IssueStatus.Occurred,"scroll-test",null,DateTimeOffset.Now,DateTimeOffset.Now));
        await Refresh(form);
        Check(((IssueDto)grid.Rows[grid.FirstDisplayedScrollingRowIndex].DataBoundItem).Id==top,"new problem retains top visible problem");
        Check(((IssueDto)grid.CurrentRow!.DataBoundItem).Id==selected && grid.CurrentCell.OwningColumn.Name==nameof(IssueDto.Status),"new problem retains selected issue and column");
        Check(action.Text==draft,"problem refresh preserves unsaved action draft");
        int selectedIndex=server.Issues.FindIndex(i=>i.Id==selected);
        server.Issues[selectedIndex]=server.Issues[selectedIndex] with {Status=IssueStatus.CompletionRequested,ActionNote="관리자 검토 대기",UpdatedAt=DateTimeOffset.Now};
        await Refresh(form);
        Check(((IssueDto)grid.CurrentRow!.DataBoundItem).Status==IssueStatus.CompletionRequested && !Field<Button>(form,"_requestCompletion").Enabled && action.ReadOnly,"changed status still updates action permissions");
        Check(TopLine(detail)==detailLine,"changed status preserves detail scroll");
        Check(action.Text==draft && TopLine(action)==actionLine,"status change keeps draft text and input scroll");
        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Start();
        Field<System.Windows.Forms.Timer>(form,"_refresh").Start();
        int beats=server.Heartbeats,reads=server.LineReads;
        await Task.Delay(3500);
        Check(server.Heartbeats-beats>=2 && server.LineReads-reads>=2,"problem test executes both one-second network loops");
        Check(((IssueDto)grid.Rows[grid.FirstDisplayedScrollingRowIndex].DataBoundItem).Id==top && ((IssueDto)grid.CurrentRow!.DataBoundItem).Id==selected,"problem viewport and selection retained during automatic ticks");
        Check(TopLine(detail)==detailLine && action.Text==draft && TopLine(action)==actionLine,"detail and action input scroll retained during automatic ticks");
        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
        using(var preview=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(preview,new Rectangle(Point.Empty,form.Size));preview.Save(Path.Combine(AppContext.BaseDirectory,"issue-scroll-fixed.png"));}
        if(Environment.GetCommandLineArgs().Contains("--ui-check"))
        {
            Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Start();Field<System.Windows.Forms.Timer>(form,"_refresh").Start();
            await Task.Delay(60000);
            Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();
        }
        server.Issues.Clear();await Refresh(form);
        Check(grid.Rows.Count==0 && action.ReadOnly && !Field<Button>(form,"_requestCompletion").Enabled,"removed issue clears list and disables action");
        Console.WriteLine("ALL PROBLEM SCROLL CHECKS PASSED");
    }

    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach(Control child in parent.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
    static async Task ModernChecks(UserMainForm form,TestServer server)
    {
        Field<System.Windows.Forms.Timer>(form,"_refresh").Stop();Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();
        var pages=Field<TabControl>(form,"_pages");
        Check(pages.TabPages.Count==5 && !pages.TabPages.Cast<TabPage>().Any(p=>p.Text.Contains("관리자 상담")),"five pages without administrator consultation");
        Check(!Descendants(form).OfType<ComboBox>().Contains(Field<ComboBox>(form,"_lines")),"line selector removed from visual tree");
        Check(Field<Label>(form,"_lineName").Text=="LINE 1","line displayed as text");
        Console.WriteLine("Line header bounds: "+Field<Label>(form,"_lineName").Bounds);
        Check(Field<Label>(form,"_lineName").Height>=20,"line name has visible layout height");
        pages.SelectedIndex=0;
        var forbidden=new[]{"아두이노 COM","포트 갱신","벨트 연결","벨트 연결 해제","벨트 구동","새로고침","조명 켜기 (D7)","조명 끄기 (D7)","전체 학습 사용"};
        Check(!Descendants(form).Any(c=>c.Visible && forbidden.Contains(c.Text)),"removed port/conveyor/header refresh controls absent");
        Check(Field<NumericUpDown>(form,"_sortTravelMs").Value==15000 && Field<NumericUpDown>(form,"_sortHoldMs").Value==15000,"real sorting timing fixed at 15000ms");
        pages.SelectedIndex=4;
        var worker=(WorkerStateDto)typeof(UserMainForm).GetMethod("BuildWorkerState",Flags)!.Invoke(form,null)!;
        Check(worker.Page==5 && worker.TravelMs==15000 && worker.HoldMs==15000,"heartbeat retains shared group page ID and fixed timings");
        Check(worker.Model!="__all__" && !worker.Models.Contains("__all__"),"combined models are not advertised to administrator");
        async Task Apply(CreateRemoteCommandRequest request) => await (Task)typeof(UserMainForm).GetMethod("ApplyRemoteActionAsync",Flags)!.Invoke(form,new object[]{request})!;
        try { await Apply(new(Guid.NewGuid(),Guid.NewGuid(),RemoteAction.SelectModel,Model:"__all__"));throw new Exception("combined models accepted remotely"); }
        catch(IOException){Check(true,"remote combined model request rejected");}
        await Apply(new(Guid.NewGuid(),Guid.NewGuid(),RemoteAction.ChangePage,Page:5));
        Check(pages.SelectedIndex==4,"shared group page 5 opens new local page 4");
        try { await Apply(new(Guid.NewGuid(),Guid.NewGuid(),RemoteAction.ChangePage,Page:4));throw new Exception("removed administrator page accepted"); }
        catch(InvalidOperationException){Check(true,"removed administrator page cannot open group chat accidentally");}
        try { await Apply(new(Guid.NewGuid(),Guid.NewGuid(),RemoteAction.SetSorting,Enabled:false,TravelMs:2000,HoldMs:1000));throw new Exception("fixed values changed remotely"); }
        catch(InvalidOperationException){Check(true,"remote settings cannot override fixed timing");}
        var now=DateTimeOffset.UtcNow;var job=Guid.NewGuid();
        Check(HardwareSerialProtocol.CreateSortCommand(job,now.AddSeconds(15),15000,now)==$"SORT {job:N} 14920 15000","serial command carries actual fifteen second schedule");
        try { HardwareSerialProtocol.CreateSortCommand(job,now.AddSeconds(15),60001,now);throw new Exception("oversized hold accepted"); } catch(InvalidOperationException) { Check(true,"hold above V7 firmware limit rejected"); }
        using(var transport=new ProtocolTransport(5))
        using(var controller=new ConveyorSerialController(transport))
        {
            await controller.InitializeAsync(false);await controller.QueueSortAsync(job,DateTimeOffset.UtcNow.AddSeconds(15),15000);
            Check(controller.IsLightOn==true && transport.Written.Contains("LED_ON") && !transport.Written.Contains("RUN"),"serial connection turns light on without starting belt");
            Check(controller.SupportsHardware && transport.Written.Any(x=>x.StartsWith("SORT ") && x.EndsWith(" 15000")),"V5 handshake permits fifteen second sort");
        }
        using(var transport=new ProtocolTransport(4))
        using(var controller=new ConveyorSerialController(transport))
        {
            await controller.InitializeAsync(false);
            try {await controller.QueueSortAsync(job,DateTimeOffset.UtcNow.AddSeconds(15),15000);throw new Exception("legacy firmware accepted fixed sort");}
            catch(InvalidOperationException){Check(!transport.Written.Any(x=>x.StartsWith("SORT ")),"legacy firmware blocked before sorting command");}
        }
        var store=new ProductionLine.Server.Infrastructure.RemoteLineStore();var client=Guid.NewGuid();
        store.Heartbeat(1,"scroll-test",new(client,worker,true));
        Check(store.Snapshot(1).Worker?.State.HoldMs==15000,"server accepts fifteen second worker settings");
        store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.SetSorting,Enabled:false,TravelMs:15000,HoldMs:15000));
        Check(store.Snapshot(1).Commands.Count==1,"server accepts fixed sorting command");
        var renderRoot=Path.Combine(AppContext.BaseDirectory,"modern-ui");Directory.CreateDirectory(renderRoot);
        foreach(var size in new[]{new Size(1440,960),new Size(1200,800)})
        {
            form.Size=size;await Task.Delay(60);
            for(int i=0;i<pages.TabCount;i++)
            {
                pages.SelectedIndex=i;await Task.Delay(50);
                if(i==1)
                {
                    var lineInfo=Field<Label>(form,"_lineInfo");
                    lineInfo.Text="LINE 1 · 대표 라인 · 실제 라인 · 가동: 가동 · 검사: 작업자 오프라인\r\n서버 기록 갱신: 2026-10-21 10:09:41\r\n센서 미연결 · 온도 --°C / 습도 --% / 가스 상태 미확인\r\n긴급 정지 상태 미확인 · 벨트 정지 · 분류 모터 0°";
                    await Task.Delay(60);
                    Check(lineInfo.Height>=lineInfo.GetPreferredSize(new Size(lineInfo.Width,0)).Height,"multiline line status fits without clipping");
                    var metrics=Field<Label[]>(form,"_metricValues");
                    foreach(var metric in metrics)
                    {
                        Check(metric.Height>=metric.Font.Height,"metric number has full font height");
                        var card=metric.Parent!.Parent!;
                        var row=card.Parent!;
                        Check(card.Top>=0 && card.Bottom<=row.ClientSize.Height,"metric card stays inside its row");
                        Check(card.PointToScreen(new Point(0,card.Height)).Y<=lineInfo.PointToScreen(Point.Empty).Y,"metric and line status do not overlap");
                    }
                }
                foreach(var button in Descendants(pages.TabPages[i]).OfType<Button>().Where(b=>b.Visible && b.Dock==DockStyle.None))
                    Check(button.Width>=button.GetPreferredSize(Size.Empty).Width && button.Height>=button.GetPreferredSize(Size.Empty).Height,"button fits preferred text and padding: "+button.Text);
                using var preview=new Bitmap(form.Width,form.Height);form.DrawToBitmap(preview,new Rectangle(Point.Empty,form.Size));
                preview.Save(Path.Combine(renderRoot,$"page-{size.Width}-{i}.png"));
            }
        }
        Console.WriteLine("ALL MODERN UI AND FIXED TIMING CHECKS PASSED");
    }
    sealed class ProtocolTransport(int version):IConveyorTransport
    {
        readonly Queue<string> _replies=new(); public List<string> Written {get;}=new();
        public void WriteLine(string value)
        {
            Written.Add(value);
            _replies.Enqueue(value=="HELLO" ? $"PL_CONVEYOR_PUL_DIR_V{version} STOPPED" : value=="LED_ON" ? "LED_ON" : value.StartsWith("SORT ") ? "SORT_QUEUED "+value.Split(' ')[1] : "STOPPED");
        }
        public string ReadLine()=>_replies.Count>0 ? _replies.Dequeue() : throw new TimeoutException();
        public void Dispose(){}
    }

    sealed class TestServer:IDisposable
    {
        readonly HttpListener _listener=new();
        readonly JsonSerializerOptions _json=new(JsonSerializerDefaults.Web);
        public bool StableHistory { get; set; }
        public bool CheckIssues {get;set;}
        public List<IssueDto> Issues {get;}=Enumerable.Range(0,100).Select(i=>new IssueDto(Guid.NewGuid(),1,IssueType.InspectionSystem,"Problem "+i,string.Join("\r\n",Enumerable.Range(0,80).Select(n=>"Symptom line "+n)),IssueStatus.InProgress,"scroll-test","last action",DateTimeOffset.Now.AddSeconds(-i),DateTimeOffset.Now.AddSeconds(-i))).ToList();
        public List<InspectionDto> History { get; }=Enumerable.Range(0,100).Select(i=>new InspectionDto(Guid.NewGuid(),1,InspectionResultType.Normal,"Inspection "+i,DateTimeOffset.Now.AddSeconds(-i))).ToList();
        public string Url{get;}
        public int Heartbeats { get; private set; }
        public int LineReads { get; private set; }
        public TestServer()
        {
            var portPicker=new TcpListener(IPAddress.Loopback,0);portPicker.Start();int port=((IPEndPoint)portPicker.LocalEndpoint).Port;portPicker.Stop();
            Url=$"http://127.0.0.1:{port}/";_listener.Prefixes.Add(Url);_listener.Start();_=Task.Run(Serve);
        }
        async Task Serve()
        {
            while(_listener.IsListening)
            {
                HttpListenerContext c;try{c=await _listener.GetContextAsync();}catch{break;}
                try
                {
                    object result; var path=c.Request.Url!.AbsolutePath;
                    if(path=="/api/auth/login") result=new LoginResponse("test",new UserDto("scroll-test","Scroll test",UserRole.User,true,new[]{1}));
                    else if(path=="/api/lines") {await Task.Delay(120);LineReads++;result=new[]{new LineStatusDto(1,"LINE 1",true,"가동","검사 대기",10+LineReads,1,0,DateTimeOffset.Now,new(true,20+LineReads/10d,50+LineReads/10d,false,false,false,0,false,DateTimeOffset.Now))};}
                    else if(path=="/api/lines/1/remote/worker") {await JsonSerializer.DeserializeAsync<WorkerHeartbeatRequest>(c.Request.InputStream,_json);Heartbeats++;await Task.Delay(80);result=new WorkerHeartbeatResponse(null);}
                    else if(path=="/api/lines/1/hardware/history") result=Array.Empty<LineHardwareStatusDto>();
                    else if(path=="/api/issues")result=CheckIssues ? Issues.ToArray() : Array.Empty<IssueDto>();
                    else if(path.EndsWith("/messages")) result=Array.Empty<IssueMessageDto>();
                    else if(path=="/api/inspections")result=StableHistory ? History.Take(100).ToArray() : new[]{new InspectionDto(Guid.NewGuid(),1,InspectionResultType.Defect,"Changing inspection "+LineReads,DateTimeOffset.Now,.75)};
                    else{c.Response.StatusCode=404;result=new ApiMessage("Test endpoint not provided");}
                    c.Response.ContentType="application/json";await JsonSerializer.SerializeAsync(c.Response.OutputStream,result,result.GetType(),_json);
                }
                finally{c.Response.Close();}
            }
        }
        public void Dispose()=>_listener.Close();
    }
}

