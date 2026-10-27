using System.Reflection;
using _01_AdminApp;
using _01_AdminApp.Services;
using ProductionLine.Shared;

internal static class InspectionPhotoChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target,string name) => (T)target.GetType().GetField(name,Flags)!.GetValue(target)!;
    private static void Check(bool ok,string description) { if(!ok) throw new Exception(description); Console.WriteLine("PASS: "+description); }
    private static Task Display(Control panel,InspectionDto? record) => (Task)panel.GetType().GetMethod("ShowInspectionAsync")!.Invoke(panel,new object?[]{record})!;
    private static bool IsColor(Control panel,bool red)
    {
        var b=Field<PictureBox>(panel,"_image").Image as Bitmap;
        return b!=null && (red ? b.GetPixel(5,5).R>200 && b.GetPixel(5,5).B<50 : b.GetPixel(5,5).B>200 && b.GetPixel(5,5).R<50);
    }
    private static async Task Wait(Func<bool> ready)
    { for(int i=0;i<100;i++){if(ready())return;await Task.Delay(50);}throw new TimeoutException("Inspection photo missing"); }

    public static async Task RunAsync(ProductionLineApiClient api,ProductionLineApiClient writer,InspectionDto red,InspectionDto blue,string output)
    {
        var dashboard = new ApiDashboardService(api); var detail=new ApiLineDetailService(api);
        using var results=new InspectionIssueForm(dashboard,detail) {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000)};
        using var history=new InspectionHistoryForm(dashboard,detail) {ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-3000,-3000)};
        foreach(var (form,gridName,file) in new (Form,string,string)[]{(results,"_inspectionGrid","admin-result-photo.png"),(history,"_history","admin-history-photo.png")})
        {
            form.Show(); var grid=Field<DataGridView>(form,gridName); var panel=Field<Control>(form,"_photo");
            await Wait(()=>grid.Rows.Count>=2);
            void Select(Guid id) { foreach(DataGridViewRow row in grid.Rows) if(row.DataBoundItem is InspectionDto item && item.Id==id){grid.CurrentCell=row.Cells[0];return;} throw new Exception("Record not in table"); }
            Select(red.Id); await Wait(()=>IsColor(panel,true));
            Check(IsColor(panel,true),form.Text+" selects matching red saved photo");
            Select(blue.Id); await Wait(()=>IsColor(panel,false));
            Check(IsColor(panel,false),form.Text+" switches to matching blue saved photo");
            Field<ComboBox>(form,"_result").SelectedIndex=1;
            if(form is InspectionIssueForm) Field<TextBox>(form,"_keyword").Text="remote-test";
            else Field<CheckBox>(form,"_useDate").Checked=true;
            form.GetType().GetMethod("ApplyFilters",Flags)!.Invoke(form,null);
            var selectedPhoto=Field<PictureBox>(panel,"_image").Image;
            var added=await writer.SaveCameraInspectionAsync(new(1,InspectionResultType.Normal,"remote-test-autorefresh",.95,true,await api.GetInspectionImageAsync(blue.Id)));
            await Wait(()=>grid.Rows.Cast<DataGridViewRow>().Any(r=>r.DataBoundItem is InspectionDto record && record.Id==added.Id));
            Check(grid.CurrentRow?.DataBoundItem is InspectionDto current && current.Id==blue.Id && ReferenceEquals(selectedPhoto,Field<PictureBox>(panel,"_image").Image),
                form.Text+" automatically receives a new record without changing selected record or reloading its photo");
            Check(Field<ComboBox>(form,"_result").SelectedIndex==1 && (form is InspectionIssueForm ? Field<TextBox>(form,"_keyword").Text=="remote-test" : Field<CheckBox>(form,"_useDate").Checked),
                form.Text+" retains search and date filters on automatic refresh");
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,file));}
            var timer=Field<System.Windows.Forms.Timer>(form,"_autoRefresh");
            form.Close();
            Check(!timer.Enabled,form.Text+" stops automatic refresh when closed");
        }

        // Simulate requests completing out of order; do not let a slow old row win.
        var slow = new ControlledDetail();
        using var shell=new InspectionIssueForm(dashboard,slow);
        var preview=Field<Control>(shell,"_photo");
        var first=Display(preview,red); var second=Display(preview,blue);
        slow.Pending[blue.Id].SetResult(await api.GetInspectionImageAsync(blue.Id)); await second;
        slow.Pending[red.Id].SetResult(await api.GetInspectionImageAsync(red.Id)); await first;
        Check(IsColor(preview,false),"late old response cannot replace current inspection photo");
        var count=slow.Pending.Count;
        await Display(preview,red with { Id=Guid.NewGuid(),HasImage=false });
        Check(Field<PictureBox>(preview,"_image").Image==null && slow.Pending.Count==count && Field<Label>(preview,"_caption").Text.Contains("없습니다"),"record without photo clears old image and shows no-photo notice without HTTP request");
        var failedId=Guid.NewGuid(); var failed=Display(preview,red with {Id=failedId});
        slow.Pending[failedId].SetException(new IOException("TEST missing image")); await failed;
        Check(Field<PictureBox>(preview,"_image").Image==null && Field<Label>(preview,"_caption").Text.Contains("사진 조회 실패"),"failed photo request cannot retain a different product image");
        var closingId=Guid.NewGuid();var closing=Display(preview,red with {Id=closingId});shell.Dispose();
        slow.Pending[closingId].SetResult(await api.GetInspectionImageAsync(red.Id));await closing;
        Check(true,"late image response after closing form is discarded");
    }
    private sealed class ControlledDetail:ILineDetailService
    {
        public Dictionary<Guid,TaskCompletionSource<byte[]>> Pending=new();
        public Task<List<InspectionDto>> GetInspectionsAsync(int lineId)=>Task.FromResult(new List<InspectionDto>());
        public Task<byte[]> GetInspectionImageAsync(Guid id,CancellationToken cancellationToken=default)
        {var promise=new TaskCompletionSource<byte[]>();Pending.Add(id,promise);return promise.Task;}
    }
}
