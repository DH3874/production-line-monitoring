using System.Reflection;
using _01_AdminApp;
using _01_AdminApp.Services;
using ProductionLine.Shared;

internal static class VirtualInspectionChecks
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Flags)!.GetValue(o)!;
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    private static async Task Wait(Func<bool> ready){for(int i=0;i<100;i++){if(ready())return;await Task.Delay(50);}throw new TimeoutException("Virtual records missing");}
    public static async Task RunAsync(string output,ProductionLineApiClient? api=null)
    {
        IDashboardService dashboard=api==null?new MockDashboardService():new ApiDashboardService(api);
        ILineDetailService detail=api==null?new MockLineDetailService():new ApiLineDetailService(api);
        var data=new List<InspectionDto>();
        for(int line=2;line<=4;line++)
        {
            var records=await detail.GetInspectionsAsync(line);data.AddRange(records);
            Check(records.Count==5 && records.All(x=>x.LineId==line && x.Reason.Contains("가상 예시")),"virtual line "+line+" contains exactly five labeled sample inspections");
            Check(records.Select(x=>x.Result).Distinct().Count()==3,"virtual line "+line+" includes normal, defect and undetermined examples");
            Check(records.SequenceEqual(await detail.GetInspectionsAsync(line)),"virtual sample IDs/timestamps remain stable on refresh");
            foreach(var record in records)
            {
                Check(record.HasImage,"virtual sample advertises its reference photo");
                using var stream=new MemoryStream(await detail.GetInspectionImageAsync(record.Id));
                using var image=Image.FromStream(stream);
                Check(image.Width>0 && image.Height>0,"sample photo decodes for line "+line+" / "+record.Result);
            }
        }
        Check(data.Select(x=>x.Id).Distinct().Count()==15,"all fifteen sample inspection IDs are distinct");
        using var result=new InspectionIssueForm(dashboard,detail){ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000)};
        using var history=new InspectionHistoryForm(dashboard,detail){ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000)};
        foreach(var (form,comboName,gridName,file) in new (Form,string,string,string)[]{(result,"_line","_inspectionGrid","virtual-inspection-results.png"),(history,"_scope","_history","virtual-inspection-history.png")})
        {
            form.Show();var choice=Field<ComboBox>(form,comboName);var grid=Field<DataGridView>(form,gridName);
            await Wait(()=>choice.Items.Count>=5);
            for(int line=2;line<=4;line++)
            {
                choice.SelectedItem=choice.Items.Cast<object>().Single(x=>x.ToString()!.StartsWith("LINE "+line));
                form.GetType().GetMethod("ApplyFilters",Flags)!.Invoke(form,null);
                Check(grid.Rows.Count==5 && grid.Rows.Cast<DataGridViewRow>().All(r=>r.DataBoundItem is InspectionDto d && d.LineId==line),form.Text+" displays five rows for virtual line "+line);
            }
            await Task.Delay(1200);
            Check(grid.Rows.Count==5,"automatic refresh does not duplicate virtual records");
            static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descendants(c)));
            foreach(var row in grid.Rows.Cast<DataGridViewRow>())
            {
                grid.CurrentCell=row.Cells.Cast<DataGridViewCell>().First(c=>c.Visible);
                await Wait(()=>Descendants(form).OfType<Label>().Any(l=>l.Text.Contains("시연용 참고 사진")) && Descendants(form).OfType<PictureBox>().Any(p=>p.Image!=null));
            }
            Check(true,form.Text+" selection loads sample photos with explicit simulation caption");
            using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(output,file));}
        }
        var scope=Field<ComboBox>(history,"_scope");scope.SelectedIndex=1;
        history.GetType().GetMethod("ApplyFilters",Flags)!.Invoke(history,null);
        Check(Field<DataGridView>(history,"_history").Rows.Count==15,"virtual-only history scope contains fifteen examples");
        scope.SelectedIndex=0;history.GetType().GetMethod("ApplyFilters",Flags)!.Invoke(history,null);
        Check(Field<DataGridView>(history,"_history").Rows.Cast<DataGridViewRow>().All(r=>r.DataBoundItem is InspectionDto d && d.LineId==1),"real history scope excludes all virtual samples");
        result.Close();history.Close();
    }
}
