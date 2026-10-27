using System.Reflection;
using _01_AdminApp;
using _01_AdminApp.Services;
using ProductionLine.Shared;

internal static class SimulationChecks
{
    private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Flags)!.GetValue(o)!;
    private static void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    private static IEnumerable<Control> Descendants(Control root)
    { foreach(Control child in root.Controls){yield return child;foreach(var next in Descendants(child))yield return next;} }
    private static string Frame(LineStatusCard card)
    {
        var view=Field<Control>(card,"_conveyor");var simulation=Field<object>(view,"_simulation");
        return $"{simulation.GetType().GetProperty("Stage")!.GetValue(simulation)}:{Field<double>(simulation,"_elapsed"):R}:{Field<double>(view,"_beltPhase"):R}";
    }
    public static async Task RunAsync(string output,ProductionLineApiClient? api=null)
    {
        RotaryGateChecks.Run(output);
        IDashboardService dashboard=api==null?new MockDashboardService():new ApiDashboardService(api);
        ILineDetailService detail=api==null?new MockLineDetailService():new ApiLineDetailService(api);
        IUserService users=api==null?new MockUserService():new ApiUserService(api);
        var before=api==null?null:await api.GetRemoteLineAsync(1);
        using var main=new AdminMainForm(new("admin","관리자",UserRole.Admin,true,new[]{1,2,3,4}),dashboard,detail,users,api)
            {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000)};
        main.Show();var cards=Field<Dictionary<int,LineStatusCard>>(main,"_cards");
        for(var i=0;cards.Count<4;i++){if(i>100)throw new TimeoutException("Simulation cards missing");await Task.Delay(50);}
        Check(!Descendants(main).OfType<Button>().Any(b=>b.Text=="문의 / 답변"),"removed inquiry/reply menu is absent from the dashboard");
        void CheckGrid(string size)
        {
            var panel=Field<Panel>(main,"_lineList");
            Check(cards[1].Top==cards[2].Top && cards[3].Top==cards[4].Top && cards[1].Left==cards[3].Left && cards[2].Left==cards[4].Left && cards[1].Top<cards[3].Top,
                "four lines use requested 1/2 above 3/4 grid at "+size);
            Check(!panel.AutoScroll && !panel.HorizontalScroll.Visible && cards.Values.All(c=>panel.ClientRectangle.Contains(c.Bounds)) &&
                cards.Values.All(c=>Field<Control>(c,"_conveyor").Height>=30),"all cards and simulation drawings fit without scrolling at "+size);
        }
        CheckGrid("default size");main.Size=main.MinimumSize;await Task.Delay(100);CheckGrid("minimum size");
        main.ClientSize=new Size(1600,1000);await Task.Delay(100);CheckGrid("larger size");
        main.ClientSize=new Size(1280,950);await Task.Delay(100);
        using(var live=new LineStatusCard())
        {
            var real=Field<LineStatusDto>(cards[1],"_line");
            var hardware=new LineHardwareStatusDto(true,25,50,null,false,false,0,false,DateTimeOffset.UtcNow);
            live.UpdateLine(real with {Hardware=hardware},0,0);live.SetSimulationPlaying(true);
            var view=Field<object>(live,"_conveyor");var frame=Frame(live);live.Animate();
            Check(Frame(live)==frame && !(bool)view.GetType().GetProperty("DemoMode")!.GetValue(view)!,"real line stays still when actual belt is stopped even with simulations playing");
            live.UpdateLine(real with {Hardware=hardware with {BeltRunning=true}},0,0);
            live.Animate();var normalPhase=Field<double>(view,"_beltPhase");
            live.UpdateLine(real with {Hardware=hardware with {BeltRunning=true,SpeedBoosted=true,SorterAngle=55,Sorting=true}},0,0);
            live.Animate();var boostedPhase=Field<double>(view,"_beltPhase");
            Check(Math.Abs(normalPhase-.06)<.00001 && Math.Abs(boostedPhase-normalPhase-.12)<.00001,
                "real conveyor graphic follows actual RUN and two-times boost telemetry");
            Check(((LineHardwareStatusDto)view.GetType().GetProperty("Hardware")!.GetValue(view)!).SorterAngle==55,
                "real sorting graphic receives measured servo state instead of scripted cycles");
            live.UpdateLine(real with {Hardware=hardware with {BeltRunning=true,ReceivedAt=DateTimeOffset.UtcNow.AddMinutes(-1)}},0,0);
            frame=Frame(live);live.Animate();Check(Frame(live)==frame,"stale hardware telemetry never keeps real line animation moving");
        }
        var toggle=Field<Button>(main,"_play");
        Check(toggle.Enabled && toggle.Text=="전체 정지","dashboard offers combined stop while simulations are playing");
        Check(new[]{2,3,4}.All(id=>Field<LineStatusDto>(cards[id],"_line").NormalCount+Field<LineStatusDto>(cards[id],"_line").DefectCount>0 && Field<LineStatusDto>(cards[id],"_line").Hardware==null),"virtual lines have separate sample counts without fake hardware readings");
        Check(Field<LineStatusDto>(cards[2],"_line").DefectCount==0 && Field<LineStatusDto>(cards[3],"_line").NormalCount==0,
            "normal-only and defect-only dashboard counters match their scenarios");
        var frames=cards.ToDictionary(p=>p.Key,p=>Frame(p.Value));await Task.Delay(350);
        Check(cards.Where(p=>p.Key!=1).All(p=>Frame(p.Value)!=frames[p.Key]),"virtual simulations advance independently of real hardware");
        async Task Stop() => await (Task)main.GetType().GetMethod("StopAllLinesAsync",Flags)!.Invoke(main,null)!;
        await Stop();frames=cards.ToDictionary(p=>p.Key,p=>Frame(p.Value));await Task.Delay(1500);
        Check(cards.Where(p=>p.Key!=1).All(p=>Frame(p.Value)==frames[p.Key]),"combined stop freezes all virtual simulations through automatic refresh");
        Check(Field<Label>(main,"_status").Text.Contains(api==null?"실제 벨트 정지 미확인":"전체 정지 완료"),"stop status remains explicit through automatic refresh: "+Field<Label>(main,"_status").Text);
        Check(new[]{2,3,4}.All(id=>Field<LineStatusDto>(cards[id],"_line").OperationStatus=="정지"),"virtual status labels show paused simulation");
        var ticks=Field<int>(main,"_simulationTicks");await Task.Delay(1100);
        Check(Field<int>(main,"_simulationTicks")==ticks,"virtual production time does not advance while paused");
        if(api==null) main.GetType().GetMethod("SetSimulationPlaying",Flags)!.Invoke(main,new object[]{true});
        else toggle.PerformClick();
        await Task.Delay(350);
        Check(cards.Where(p=>p.Key!=1).All(p=>Frame(p.Value)!=frames[p.Key]) && toggle.Text=="전체 정지","resume continues virtual simulations from retained state");
        Check(Field<Label[]>(main,"_values").Length==5 && Descendants(main).OfType<Button>().Any(c=>c.Text=="완료 요청 확인"),"completion request review button is available while dashboard keeps five metrics");
        if(api!=null)
        {
            var after=await api.GetRemoteLineAsync(1);
            var added=after.Commands.Where(c=>before!.Commands.All(old=>old.Id!=c.Id)).ToArray();
            Check(added.Length==1 && added[0].Request.Action==RemoteAction.StopAll && added[0].Status==RemoteCommandStatus.Succeeded,
                "combined stop sends exactly one acknowledged real stop; replay never sends RUN");
        }
        using(var image=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,"admin-simulation-playing.png"));}
        main.GetType().GetMethod("SetSimulationPlaying",Flags)!.Invoke(main,new object[]{false});using(var image=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,"admin-simulation-paused.png"));}
        main.Close();
    }
}
