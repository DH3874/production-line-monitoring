using System.Reflection;
using _01_AdminApp;
using ProductionLine.Shared;

internal static class RotaryGateChecks
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(object value,string name) => (T)value.GetType().GetField(name,Flags)!.GetValue(value)!;
    private static T Property<T>(object value,string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static void Check(bool ok,string message)
    { if(!ok) throw new Exception(message);Console.WriteLine("PASS: "+message); }

    public static void Run(string output)
    {
        var type = typeof(LineStatusCard).Assembly.GetType("_01_AdminApp.RotaryGateSimulation")!;
        var simulation = Activator.CreateInstance(type,true)!;
        var advance = type.GetMethod("Advance")!;
        bool normalPass=false,diagonal=false,deflected=false,returned=false;
        for(var i=0;i<600;i++)
        {
            var angle=Property<double>(simulation,"GateAngleDegrees");
            var defective=Property<bool>(simulation,"IsDefective");
            var stage=Property<object>(simulation,"Stage").ToString();
            if(angle < 0 || angle > 55.00001 || (!defective && Math.Abs(angle)>1e-8))
                throw new Exception("Normal products must never deploy the rotary gate.");
            if(!defective && stage=="Passing") normalPass=true;
            if(defective && stage=="Deflecting")
            {
                diagonal |= Math.Abs(angle-55)<1e-8;
                deflected |= Property<double>(simulation,"ProductY")<100;
            }
            if(diagonal && stage=="Gap" && Math.Abs(angle)<1e-8) returned=true;
            advance.Invoke(simulation,new object[]{.05d});
        }
        Check(normalPass && diagonal && deflected && returned,"normal passes with a fixed plate; rejects turn the plate 55 degrees, enter the bin, then return to horizontal");

        CheckProfiles();
        var virtualLine = new LineStatusDto(4,"LINE 4 · 가상",false,"가동","가상 검사",9,2,0,DateTimeOffset.Now);
        using var virtualCard = new LineStatusCard { Size=new Size(900,650) };
        virtualCard.UpdateLine(virtualLine,0,0);virtualCard.SetSimulationPlaying(true);
        var view = Field<object>(virtualCard,"_conveyor");
        var state = Field<object>(view,"_simulation");
        virtualCard.SetEmergencyStopped(true);
        var elapsed=Field<double>(state,"_elapsed");
        // The periodic server refresh must not release a local simulation emergency stop.
        virtualCard.UpdateLine(virtualLine,0,0);virtualCard.SetSimulationPlaying(true);virtualCard.Animate();
        Check(virtualCard.EmergencyStopped && !virtualCard.IsActive && Field<double>(state,"_elapsed")==elapsed,
            "virtual emergency stop freezes motion and survives periodic line refresh");
        virtualCard.SetEmergencyStopped(false);virtualCard.Animate();
        Check(Field<double>(state,"_elapsed")>elapsed,"explicit simulation reset resumes from retained state");

        void Save(LineStatusCard card,string name)
        {
            using var host=new Form {ClientSize=new Size(900,650),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-4000,-4000)};
            host.Controls.Add(card);card.Dock=DockStyle.Fill;host.Show();
            using var image=new Bitmap(card.Width,card.Height);
            card.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
            image.Save(Path.Combine(output,name));
            host.Controls.Remove(card);host.Close();
        }
        Save(virtualCard,"rotary-normal.png");
        for(var i=0;i<500 && !(Property<bool>(state,"IsDefective") && Property<object>(state,"Stage").ToString()=="Deflecting" && Property<double>(state,"ProductY")<130);i++)
            virtualCard.Animate();
        Check(Property<double>(view,"GateAngleDegrees")>54,"virtual card renderer uses the rotating state rather than a translating pusher");
        Save(virtualCard,"rotary-defect.png");
        virtualCard.SetEmergencyStopped(true);Save(virtualCard,"rotary-emergency.png");

        using var realCard=new LineStatusCard {Size=new Size(900,650)};
        var hardware=new LineHardwareStatusDto(true,24,50,null,false,true,55,true,DateTimeOffset.UtcNow);
        realCard.UpdateLine(virtualLine with {Id=1,Name="LINE 1 · 대표 라인",IsReal=true,Hardware=hardware},0,0);
        realCard.SetSimulationPlaying(true);realCard.Animate();
        var realView=Field<object>(realCard,"_conveyor");
        Check(Property<double>(realView,"GateAngleDegrees")==55,"representative line draws the same pivoted plate from actual 55-degree servo telemetry");
        Save(realCard,"rotary-real-hardware.png");
        Check(typeof(LineStatusCard).Assembly.GetType("_01_AdminApp.IssueMessagesForm")==null,
            "standalone inquiry/reply screen is removed from the built administrator app");
    }

    private static void CheckProfiles()
    {
        var cards=new Dictionary<int,LineStatusCard>();
        try
        {
            var seenMixedNormal=false;var seenMixedDefect=false;
            foreach(var id in new[]{2,3,4})
            {
                var card=new LineStatusCard();cards.Add(id,card);
                card.UpdateLine(new(id,$"LINE {id}",false,"가동","가상 검사",0,0,0,DateTimeOffset.Now),0,0);
                card.SetSimulationPlaying(true);
            }
            object State(int id)=>Field<object>(Field<object>(cards[id],"_conveyor"),"_simulation");
            Check(Property<object>(State(2),"Stage").ToString()!=Property<object>(State(3),"Stage").ToString(),
                "virtual lines begin at different positions instead of synchronized identical motion");
            for(var tick=0;tick<1500;tick++)
            {
                foreach(var card in cards.Values)card.Animate();
                if(Property<bool>(State(2),"IsDefective") || Property<double>(State(2),"GateAngleDegrees")!=0)
                    throw new Exception("Normal-only line incorrectly rejected a product.");
                if(!Property<bool>(State(3),"IsDefective"))
                    throw new Exception("Defect-only line incorrectly generated a normal product.");
                seenMixedNormal |= !Property<bool>(State(4),"IsDefective");
                seenMixedDefect |= Property<bool>(State(4),"IsDefective");
                if(tick%20==0)
                {
                    foreach(var (id,card) in cards)
                    {
                        var state=State(id);var elapsed=Field<double>(state,"_elapsed");
                        var normal=card.SimulationNormalCount;var defect=card.SimulationDefectCount;
                        card.UpdateLine(new(id,$"LINE {id}",false,"가동","가상 검사",0,0,0,DateTimeOffset.Now),0,0);
                        if(elapsed!=Field<double>(state,"_elapsed") || normal!=card.SimulationNormalCount || defect!=card.SimulationDefectCount)
                            throw new Exception("Refresh restarted a scenario or reset its completed-product counts.");
                    }
                }
            }
            Check(cards[2].SimulationNormalCount>3 && cards[2].SimulationDefectCount==0,
                "LINE 2 repeatedly passes normal products with a stationary gate and normal-only totals");
            Check(cards[3].SimulationDefectCount>3 && cards[3].SimulationNormalCount==0,
                "LINE 3 repeatedly sorts defective products with defect-only totals");
            Check(seenMixedNormal && seenMixedDefect && cards[4].SimulationNormalCount>2 && cards[4].SimulationDefectCount>2,
                "LINE 4 alternates normal passage and defective sorting across multiple full cycles");
            Check(true,"periodic refresh preserves each line's independent phase and result counters");
        }
        finally { foreach(var card in cards.Values)card.Dispose(); }
    }
}
