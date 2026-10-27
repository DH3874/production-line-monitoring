using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using ProductionLine.Shared;
using ProductionLine.UserApp;
using ProductionLine.Server.Infrastructure;
using _01_AdminApp;
using _01_AdminApp.Services;

internal static class Program
{
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static int _checks;
    private static T Field<T>(object form,string name)=>(T)form.GetType().GetField(name,Flags)!.GetValue(form)!;
    private static void Set(object form,string name,object value)=>form.GetType().GetField(name,Flags)!.SetValue(form,value);
    private static void Check(bool ok,string message) { if(!ok) throw new Exception(message);Console.WriteLine("PASS: "+message);_checks++; }
    private static WorkerStateDto State()=>new("TEST-PC",false,0,false,true,"COM_TEST",false,true,false,2000,1000,"__all__",0,["COM_TEST"],["__all__"],"TEST",null,null,null);
    private static async Task Reject(Func<Task> action,string message)
    {try {await action();}catch(Exception) {Check(true,message);return;}throw new Exception("Unexpected success: "+message);}
    private static void UnitChecks()
    {
        var now=DateTimeOffset.UtcNow;
        var store=new RemoteLineStore(()=>now);
        var client=Guid.NewGuid();
        store.Heartbeat(1,"worker",new(client,State(),true));
        var request=new CreateRemoteCommandRequest(Guid.NewGuid(),client,RemoteAction.RunConveyor);
        var command=store.Enqueue(1,"admin",request);
        Check(store.Enqueue(1,"admin",request).Id==command.Id && store.Snapshot(1).Commands.Count==1,"request id deduplicates command submissions");
        var dispatch=store.Heartbeat(1,"worker",new(client,State(),true)).Command;
        Check(dispatch?.Id==command.Id && dispatch.Status==RemoteCommandStatus.Executing,"command dispatch changes state atomically");
        Check(store.Heartbeat(1,"worker",new(client,State(),true)).Command==null,"lost HTTP reply never causes command redelivery");
        var result=store.Complete(1,"worker",command.Id,new(client,true,"OK"));
        Check(store.Complete(1,"worker",command.Id,new(client,true,"OK"))==result,"completion acknowledgments are idempotent");
        var stale=store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.RunConveyor));
        now=now.AddSeconds(7);
        store.Heartbeat(1,"worker",new(client,State(),true));
        Check(store.Snapshot(1).Commands.Single(c=>c.Id==stale.Id).Status==RemoteCommandStatus.Cancelled,"reconnection cancels old queued movement");
        var run=store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.RunConveyor));
        store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.StopAll));
        Check(store.Snapshot(1).Commands.Single(c=>c.Id==run.Id).Status==RemoteCommandStatus.Cancelled,"stop cancels previously queued start commands");
        Check(store.Heartbeat(1,"worker",new(client,State(),false)).Command?.Request.Action == RemoteAction.StopAll,
            "stop-all is dispatched even while worker is executing a long action");
        store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.SetLight,Enabled:true));
        now=now.AddSeconds(11);
        Check(store.Snapshot(1).Commands.Any(c=>c.Status==RemoteCommandStatus.Expired),"pending commands expire rather than execute later");
        try {store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.RunConveyor));throw new Exception("Offline accepted");}
        catch(InvalidOperationException) {Check(true,"offline worker cannot accept movement commands");}
        store.Heartbeat(1,"worker",new(client,State(),true));
        try {store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.SelectModel,Model:"../../bad.onnx"));throw new Exception("Path accepted");}
        catch(ArgumentException) {Check(true,"arbitrary remote model paths are rejected");}
        try {store.Heartbeat(1,"worker",new(Guid.NewGuid(),State(),true));throw new Exception("Second owner accepted");}
        catch(InvalidOperationException) {Check(true,"two live worker instances cannot own the same line");}
        store.Heartbeat(1,"worker",new(client,State() with { TravelMs=60000, HoldMs=60000 },true));
        Check(store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.SetSorting,Enabled:true,TravelMs:60000,HoldMs:60000)).Status==RemoteCommandStatus.Pending,
            "remote worker state and sorting command support 60-second timing");
        try { store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.SetSorting,Enabled:true,TravelMs:60000,HoldMs:60001));throw new Exception("Limit bypassed"); }
        catch(ArgumentException) {Check(true,"remote server rejects hold above 60 seconds");}
        Check(store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.SetBoostDuration,BoostMs:5000)).Status==RemoteCommandStatus.Pending,
            "remote acceleration duration command accepts five seconds");
        try { store.Enqueue(1,"admin",new(Guid.NewGuid(),client,RemoteAction.SetBoostDuration,BoostMs:60001));throw new Exception("Limit bypassed"); }
        catch(ArgumentException) {Check(true,"remote server rejects acceleration duration above 60 seconds");}
    }
    [STAThread]
    private static void Main(string[] args)
    {
        UnitChecks();
        if (args.Contains("--timing"))
        {
            ProductionLine.Verification.HardwareProtocolChecks.Run();
            ProductionLine.Verification.ConveyorProtocolChecks.RunAsync().GetAwaiter().GetResult();
            RecognitionChecks.Run();
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if(args.Contains("--simulation") || args.Contains("--virtual-inspections"))
        {
            var folder=Path.Combine(AppContext.BaseDirectory,"simulation-preview");Directory.CreateDirectory(folder);
            using var host=new Form {ShowInTaskbar=false,Opacity=0};
            host.Shown+=async(_,_)=>{try{if(args.Contains("--virtual-inspections"))await VirtualInspectionChecks.RunAsync(folder);else await SimulationChecks.RunAsync(folder);Console.WriteLine("DASHBOARD CHECKS PASSED");}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}finally{host.Close();}};
            Application.Run(host);return;
        }
        var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        var data=Path.Combine(output,"isolated-server-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
        using var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
        var url=$"http://127.0.0.1:{port}";
        var start=new ProcessStartInfo("dotnet") {WorkingDirectory=data,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(Path.GetFullPath(args[0]));start.ArgumentList.Add("--ServerUrl");start.ArgumentList.Add(url);
        start.Environment["Gemini__Enabled"]="false";start.Environment["Gemini__UseSavedKey"]="false";
        using var server=Process.Start(start)!;var log=server.StandardOutput.ReadToEndAsync();var errors=server.StandardError.ReadToEndAsync();
        using var runner=new Form {ShowInTaskbar=false,Opacity=0,Size=new Size(100,100)};
        runner.Shown+=async(_,_)=>
        {
            try
            {
                using var http=new HttpClient();
                for(int i=0;;i++) {try {if((await http.GetAsync(url)).IsSuccessStatusCode) break;}catch(HttpRequestException) {}if(i>100)throw new TimeoutException("Test server startup failed");await Task.Delay(100);}
                await IntegrationChecks(url,output);
                Console.WriteLine($"{_checks} REMOTE/GRAPH CHECKS PASSED. No physical serial ports were used.");
            }
            catch(Exception ex) {Console.Error.WriteLine(ex);Environment.ExitCode=1;}
            finally {runner.Close();}
        };
        try {Application.Run(runner);}
        finally {if(!server.HasExited)server.Kill(true);server.WaitForExit();File.WriteAllText(Path.Combine(output,"test-server.log"),log.GetAwaiter().GetResult()+errors.GetAwaiter().GetResult());}
    }
    private static async Task IntegrationChecks(string url,string output)
    {
        using var workerApi=new ProductionLineApiClient(url);using var adminApi=new ProductionLineApiClient(url);using var anonymous=new ProductionLineApiClient(url);
        await workerApi.LoginAsync("worker1","worker123!");await adminApi.LoginAsync("admin","admin123!");
        await Reject(()=>anonymous.GetRemoteLineAsync(1),"anonymous cannot read worker video or controls");
        await Reject(()=>workerApi.GetRemoteLineAsync(1),"worker cannot use administrator remote endpoint");
        await Reject(()=>workerApi.CreateRemoteCommandAsync(1,new(Guid.NewGuid(),Guid.NewGuid(),RemoteAction.RunConveyor)),"worker cannot submit remote commands");
        await Reject(()=>adminApi.SendWorkerHeartbeatAsync(1,new(Guid.NewGuid(),State(),true)),"administrator cannot impersonate worker runtime");
        await Reject(()=>adminApi.GetRemoteLineAsync(2),"virtual lines do not expose real hardware control");
        using var form=new UserMainForm(workerApi) {ShowInTaskbar=false,Location=new Point(-3000,-3000),StartPosition=FormStartPosition.Manual};
        var hardware=new TestHardware();Set(form,"_conveyor",hardware);
        using var cameraImage = new Bitmap(320,180);
        using(var g=Graphics.FromImage(cameraImage)) {g.Clear(Color.DarkSlateBlue);g.DrawString("TEST CAMERA",SystemFonts.DefaultFont,Brushes.White,10,10);}
        using var cameraBytes = new MemoryStream();cameraImage.Save(cameraBytes,System.Drawing.Imaging.ImageFormat.Jpeg);
        Set(form,"_camera",new TestCamera(cameraBytes.ToArray()));
        var recognized = Field<PictureBox>(form,"_recognizedVideo");recognized.Image = new Bitmap(40,40);
        using(var g=Graphics.FromImage(recognized.Image))g.Clear(Color.Blue);
        Field<ComboBox>(form,"_conveyorPort").DataSource=new[] {"COM_TEST"};
        Field<System.Windows.Forms.Timer>(form,"_conveyorHeartbeat").Start();
        form.Show();
        async Task<RemoteLineSnapshotDto> Wait(Func<RemoteLineSnapshotDto,bool> predicate)
        {
            for(int i=0;i<100;i++) {var snapshot=await adminApi.GetRemoteLineAsync(1);if(predicate(snapshot))return snapshot;await Task.Delay(150);}
            throw new TimeoutException("Remote worker result missing");
        }
        var first=await Wait(s=>s.Worker?.IsOnline==true);
        var client=first.Worker!.ClientId;
        Check(first.Worker.State.Models.Length>=1,"operator publishes bounded model catalog and machine identity");
        var streamed = await Wait(s=>s.Worker?.State.LiveJpeg is {Length:>0} && s.Worker.State.LastProductJpeg is {Length:>0});
        using(var stream = new MemoryStream(streamed.Worker!.State.LastProductJpeg!))
        using(var picture = new Bitmap(stream))Check(picture.GetPixel(5,5).B>200,"live and last-product previews are transmitted as decodable separate images");
        async Task<RemoteCommandDto> Send(RemoteAction action,bool? enabled=null,int? page=null)
        {
            var c=await adminApi.CreateRemoteCommandAsync(1,new(Guid.NewGuid(),client,action,Enabled:enabled,TravelMs:2100,HoldMs:900,Page:page,BoostMs:5000));
            var snapshot=await Wait(s=>s.Commands.Any(v=>v.Id==c.Id && v.Status is RemoteCommandStatus.Succeeded or RemoteCommandStatus.Failed));
            return snapshot.Commands.Single(v=>v.Id==c.Id);
        }
        Check((await Send(RemoteAction.SetLight,false)).Status==RemoteCommandStatus.Succeeded && hardware.IsLightOn==false,"administrator light command executes in actual worker handler");
        Check((await Send(RemoteAction.SetBoostDuration)).Status==RemoteCommandStatus.Succeeded && Field<NumericUpDown>(form,"_boostSeconds").Value==5,
            "administrator acceleration duration reaches worker input as five seconds");
        Check((await Send(RemoteAction.RunConveyor)).Status==RemoteCommandStatus.Succeeded && hardware.IsRunning && hardware.Runs==1,"administrator RUN reaches worker once and waits for hardware acknowledgment");
        Set(form,"_remoteExecuting",true);
        Check((await Send(RemoteAction.StopAll)).Status==RemoteCommandStatus.Succeeded && !hardware.IsRunning,
            "actual worker stop handler runs while another remote action is busy");
        Set(form,"_remoteExecuting",false);
        Check((await Send(RemoteAction.RunConveyor)).Status==RemoteCommandStatus.Succeeded && hardware.IsRunning,"separate explicit run is required after remote stop");
        Check((await Send(RemoteAction.SetSorting,true)).Status==RemoteCommandStatus.Succeeded && Field<CheckBox>(form,"_automaticSort").Checked && Field<NumericUpDown>(form,"_sortTravelMs").Value==2100,"administrator sorting settings update worker controls");
        Check((await Send(RemoteAction.ChangePage,page:1)).Status==RemoteCommandStatus.Succeeded && Field<TabControl>(form,"_pages").SelectedIndex==1,"administrator can select worker application page");
        Check((await Send(RemoteAction.StopAll)).Status==RemoteCommandStatus.Succeeded && !hardware.IsRunning && !Field<CheckBox>(form,"_automaticSort").Checked,"remote stop stops belt and disables automatic sorting");
        hardware.Emergency=true;
        Check((await Send(RemoteAction.RunConveyor)).Status==RemoteCommandStatus.Failed && !hardware.IsRunning,"physical emergency rejection is reported as failure, never success");
        Check((await Send(RemoteAction.ResetEmergency)).Status==RemoteCommandStatus.Failed,"remote reset cannot bypass physical emergency contact");
        hardware.Emergency=false;
        Check((await Send(RemoteAction.DisconnectCamera)).Status==RemoteCommandStatus.Succeeded,"remote camera disconnect releases worker feed");
        Check((await Send(RemoteAction.StartInspection)).Status==RemoteCommandStatus.Failed,"camera prerequisite failure is visible in administrator command history");
        var history=await adminApi.GetHardwareHistoryAsync(1);
        Check(history.Count>0 && history.Any(p=>p.TemperatureCelsius==24.5),"real server shares temperature samples for both applications");
        Check(history.Any(p=>p.GasAnalogValue==348),"A0 analog value reaches server history and administrator graphs");
        Check(Field<Control>(form,"_sensorTrends").Visible || Field<TabControl>(form,"_pages").SelectedIndex!=0,"operator sensor graph control is installed");
        Field<TabControl>(form,"_pages").SelectedIndex=0;
        var graph=Field<Control>(form,"_sensorTrends");
        var seed=Enumerable.Range(0,280).Select(i=>new LineHardwareStatusDto(true,24+2*Math.Sin(i/30d),50+5*Math.Cos(i/20d),i>200 && i<240,false,false,0,false,DateTimeOffset.UtcNow.AddSeconds((i-279)*2))).ToArray();
        graph.GetType().GetMethod("SetSamples")!.Invoke(graph,new object[]{seed});
        using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"worker-graphs.png"));}
        using var remote=new RemoteWorkerForm(adminApi) {ShowInTaskbar=false,Location=new Point(-3000,-3000),StartPosition=FormStartPosition.Manual};
        byte[] Photo(Color color) {using var b=new Bitmap(64,48);using(var g=Graphics.FromImage(b))g.Clear(color);using var stream=new MemoryStream();b.Save(stream,System.Drawing.Imaging.ImageFormat.Jpeg);return stream.ToArray();}
        var redRecord=await workerApi.SaveCameraInspectionAsync(new(1,InspectionResultType.Normal,"remote-test-red",.95,true,Photo(Color.Red)));
        var blueRecord=await workerApi.SaveCameraInspectionAsync(new(1,InspectionResultType.Normal,"remote-test-blue",.95,true,Photo(Color.Blue)));
        await InspectionPhotoChecks.RunAsync(adminApi,workerApi,redRecord,blueRecord,output);
        remote.Show();await Task.Delay(1600);
        Check(Field<Panel>(remote,"_operations").Enabled,"administrator controls enabled only for online worker");
        var historyGrid=Field<DataGridView>(remote,"_history");
        foreach(DataGridViewRow row in historyGrid.Rows)if(row.DataBoundItem is InspectionDto item && item.Id==redRecord.Id){historyGrid.CurrentCell=row.Cells[0];break;}
        for(int i=0;i<30;i++)
        {
            var loaded=Field<PictureBox>(remote,"_historyPhoto").Image as Bitmap;
            if(loaded!=null && loaded.GetPixel(5,5).R>200 && loaded.GetPixel(5,5).B<50)break;
            await Task.Delay(100);
        }
        var recordImage=Field<PictureBox>(remote,"_historyPhoto").Image;
        Check(recordImage!=null && ((Bitmap)recordImage).GetPixel(5,5).R>200 && ((Bitmap)recordImage).GetPixel(5,5).B<50,"administrator history selection loads the matching inspection photo");
        using(var bitmap=new Bitmap(remote.Width,remote.Height)) {remote.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"admin-remote.png"));}
        var tabs=remote.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabControl>().Single();
        tabs.SelectedIndex=1;
        var adminGraph=Field<Control>(remote,"_trends");adminGraph.GetType().GetMethod("SetSamples")!.Invoke(adminGraph,new object[]{seed});
        using(var bitmap=new Bitmap(remote.Width,remote.Height)) {remote.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"admin-graphs.png"));}
        remote.Close();
        await AdminChatChecks.RunAsync(form,workerApi,adminApi);
        // The earlier rejected emergency RUN intentionally disconnected the simulated controller.
        Set(form,"_conveyor",hardware);
        Field<System.Windows.Forms.Timer>(form,"_conveyorHeartbeat").Start();
        await hardware.RunAsync();
        Check(hardware.IsRunning,"simulated physical belt is running before dashboard combined stop");
        var runsBeforeDashboardStop=hardware.Runs;
        await SimulationChecks.RunAsync(output,adminApi);
        Check(!hardware.IsRunning && hardware.Runs==runsBeforeDashboardStop,"dashboard stop reaches worker hardware and replay does not restart the belt");
        await VirtualInspectionChecks.RunAsync(output,adminApi);
        Field<System.Windows.Forms.Timer>(form,"_remoteTimer").Stop();
        await Task.Delay(6600);
        await Reject(()=>adminApi.CreateRemoteCommandAsync(1,new(Guid.NewGuid(),client,RemoteAction.RunConveyor)),"offline worker rejects stale administrator movement command");
        form.Close();
    }
    private sealed class TestHardware:IConveyorController,IConveyorLightingController,IHardwareConveyorController
    {
        public bool IsRunning {get;private set;}
        public bool? IsLightOn {get;private set;}=true;
        public bool Emergency;
        public int Runs;
        public bool SupportsHardware=>true;
        public Task RunAsync() {if(Emergency)throw new IOException("ERROR ESTOP_ACTIVE");IsRunning=true;Runs++;return Task.CompletedTask;}
        public Task StopAsync() {IsRunning=false;return Task.CompletedTask;}
        public Task PingAsync()=>Task.CompletedTask;
        public Task SetLightAsync(bool enabled) {IsLightOn=enabled;return Task.CompletedTask;}
        public Task RefreshLightAsync()=>Task.CompletedTask;
        public Task<UpdateLineHardwareRequest> ReadHardwareAsync()=>Task.FromResult(new UpdateLineHardwareRequest(true,24.5,53,null,Emergency,IsRunning,0,false,GasAnalogValue:348));
        public Task QueueSortAsync(Guid id,DateTimeOffset due,int hold)=>Task.CompletedTask;
        public Task ResetEmergencyAsync() {if(Emergency)throw new IOException("ERROR ESTOP_STILL_PRESSED");return Task.CompletedTask;}
        public void Dispose() {}
    }
    private sealed class TestCamera(byte[] jpeg):ICameraFeed
    {
        private long _sequence;
        public string? Error=>null;
        public CameraFrame? Latest=>new(jpeg,++_sequence,DateTime.UtcNow);
        public Task Completion=>Task.CompletedTask;
        public void Dispose() {}
    }
}
