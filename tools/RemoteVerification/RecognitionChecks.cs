using System.Reflection;
using ProductionLine.UserApp;
using ProductionLine.Shared;

internal static class RecognitionChecks
{
    public static void Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool ok,string text) { if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text); }
        using var api = new ProductionLineApiClient();
        using var form = new UserMainForm(api);
        void Set(string name,object value) => typeof(UserMainForm).GetField(name,flags)!.SetValue(form,value);
        var device = new Device(); Set("_conveyor",device);
        var automatic = (CheckBox)typeof(UserMainForm).GetField("_automaticSort",flags)!.GetValue(form)!;
        void Recognize(InspectionResultType result) => ((Task)typeof(UserMainForm).GetMethod("HandleRecognizedConveyorAsync",flags)!.Invoke(form,new object[]{DateTime.UtcNow,result})!).GetAwaiter().GetResult();
        Recognize(InspectionResultType.Normal);
        Check(device.Products.Count==0,"a stopped belt never starts automatically from recognition");
        device.Running=true;
        ((NumericUpDown)typeof(UserMainForm).GetField("_boostSeconds",flags)!.GetValue(form)!).Value=5;
        Recognize(InspectionResultType.Normal);
        Check(device.Products[^1].BoostMs==5000,"selected five-second acceleration duration reaches recognition command");
        Check(device.Products.Count==1 && !device.Products[^1].Sort,"normal recognition requests acceleration without sorting");
        Set("_latestHardware",new UpdateLineHardwareRequest(true,25,50,false,false,true,0,false));
        Set("_latestHardwareAt",DateTimeOffset.UtcNow);
        automatic.Checked=true;
        Recognize(InspectionResultType.Defect);
        Check(device.Products.Count==2 && device.Products[^1].Sort && device.LegacySorts==0,"defect recognition requests combined distance sorting and acceleration once");
        Recognize(InspectionResultType.Normal);
        Check(device.Products.Count==3 && !device.Products[^1].Sort,"normal products still accelerate with automatic defect sorting enabled");
        Check(device.Products.Select(p=>p.Id).Distinct().Count()==3,"each accepted product has a unique id for firmware deduplication");
        var now=DateTimeOffset.UtcNow;
        var packet=HardwareSerialProtocol.CreateProductCommand(Guid.NewGuid(),now.UtcDateTime.AddMilliseconds(-500),60000,60000,true,now);
        var age=int.Parse(packet.Split(' ')[2]);
        Check(age>500 && age<650 && packet.EndsWith("60000 60000 1"),"recognition packet includes inference age and exact serial transit allowance");
        var state=HardwareSerialProtocol.Parse("HW {\"temperatureCelsius\":25,\"humidityPercent\":50,\"gasDetected\":false,\"emergencyStopped\":false,\"beltRunning\":true,\"speedBoosted\":true,\"sorterAngle\":0,\"sorting\":false}");
        Check(state.SpeedBoosted,"actual acceleration state is parsed for both dashboards");
        var travelControl = (NumericUpDown)typeof(UserMainForm).GetField("_sortTravelMs",flags)!.GetValue(form)!;
        var holdControl = (NumericUpDown)typeof(UserMainForm).GetField("_sortHoldMs",flags)!.GetValue(form)!;
        var timingLabel = (Label)typeof(UserMainForm).GetField("_sortTimingSummary",flags)!.GetValue(form)!;
        var apply = typeof(UserMainForm).GetMethod("ApplyRemoteActionAsync",flags)!;
        ((Task)apply.Invoke(form,new object[]{new CreateRemoteCommandRequest(Guid.NewGuid(),Guid.NewGuid(),RemoteAction.SetSorting,Enabled:false,TravelMs:20000,HoldMs:25000)})!).GetAwaiter().GetResult();
        Check(travelControl.Value==20000 && holdControl.Value==25000 && timingLabel.Text.Contains("20,000") && timingLabel.Text.Contains("25,000"),"administrator timing updates worker read-only summary immediately above old limits");
        Check(travelControl.Parent==null && holdControl.Parent==null,"worker timing values remain unavailable for local editing");
        automatic.Checked=true;
        Set("_latestHardwareAt",DateTimeOffset.UtcNow);
        Recognize(InspectionResultType.Defect);
        Check(device.LastTravel==20000 && device.LastHold==25000,"next recognized defect receives administrator-selected travel and hold");
        try
        {
            ((Task)apply.Invoke(form,new object[]{new CreateRemoteCommandRequest(Guid.NewGuid(),Guid.NewGuid(),RemoteAction.SetSorting,Enabled:false,TravelMs:60001,HoldMs:25000)})!).GetAwaiter().GetResult();
            throw new Exception("out-of-range timing accepted");
        }
        catch(InvalidOperationException) { Check(travelControl.Value==20000 && holdControl.Value==25000,"invalid timing leaves current worker settings unchanged"); }
        var runButton=(Button)typeof(UserMainForm).GetField("_conveyorRun",flags)!.GetValue(form)!;
        Check(runButton.Parent!=null,"worker belt run button is restored to the inspection controls");
        device.Running=false; runButton.Enabled=true;
        runButton.GetType().GetMethod("OnClick",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(runButton,new object[]{EventArgs.Empty});
        Check(device.Running,"worker belt button invokes existing run controller using a simulated device");
        var template=(Control)typeof(UserMainForm).GetField("_sensorTrends",flags)!.GetValue(form)!;
        using var chart=(Control)Activator.CreateInstance(template.GetType())!;
        chart.Size=new Size(300,320);
        var samples=Enumerable.Range(0,120).Select(i=>new LineHardwareStatusDto(true,25,50,null,false,false,0,false,
            DateTimeOffset.UtcNow.AddSeconds((i-119)*2),GasAnalogValue:350+(int)(100*Math.Sin(i/12d)))).ToArray();
        chart.GetType().GetMethod("SetSamples")!.Invoke(chart,new object[]{samples});
        var folder=Path.Combine(AppContext.BaseDirectory,"previews");Directory.CreateDirectory(folder);
        using var image=new Bitmap(chart.Width,chart.Height);chart.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
        image.Save(Path.Combine(folder,"gas-a0-graph.png"));
        Check(true,"A0 graph renders with a 0..1023 scale");
    }
    private sealed class Device:IConveyorController,IRecognitionConveyorController,IHardwareConveyorController
    {
        public bool Running;
        public bool IsRunning=>Running;
        public bool SupportsRecognitionBoost=>true;
        public bool SupportsConfigurableBoost=>true;
        public bool SupportsHardware=>true;
        public List<(Guid Id,bool Sort,int BoostMs)> Products=new();
        public int LegacySorts;
        public int LastTravel, LastHold;
        public Task RecognizeAsync(Guid id,DateTime captured,int travel,int hold,bool sort,int boostMs=3000) {LastTravel=travel;LastHold=hold;Products.Add((id,sort,boostMs));return Task.CompletedTask;}
        public Task QueueSortAsync(Guid id,DateTimeOffset due,int hold){LegacySorts++;return Task.CompletedTask;}
        public Task<UpdateLineHardwareRequest> ReadHardwareAsync()=>Task.FromResult(new UpdateLineHardwareRequest(true,25,50,false,false,Running,0,false));
        public Task ResetEmergencyAsync()=>Task.CompletedTask;
        public Task RunAsync(){Running=true;return Task.CompletedTask;}
        public Task StopAsync(){Running=false;return Task.CompletedTask;}
        public Task PingAsync()=>Task.CompletedTask;
        public void Dispose(){}
    }
}
