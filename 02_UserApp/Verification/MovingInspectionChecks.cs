using System.Reflection;
using OpenCvSharp;
using ProductionLine.Shared;
using ProductionLine.UserApp;
using ProductionLine.Vision;

namespace ProductionLine.Verification;

internal static class MovingInspectionChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Flags)!.GetValue(instance)!;
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Flags)!.SetValue(instance, value);
    private static object? Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Flags)!.Invoke(instance, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    public static void Run()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var phone = File.ReadAllBytes(Environment.GetCommandLineArgs().Skip(2).First());
        using var expectedInspector = YoloOnnxInspector.CreateCombined(.45f);
        var expectedResult = expectedInspector.Inspect(phone);
        using var blank = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        using var server = new TestServer();
        using var api = new ProductionLineApiClient(server.Url);
        api.LoginAsync("test", "test").GetAwaiter().GetResult();
        using var form = new UserMainForm(api);
        var feed = new MutableFeed(blank.ToBytes(".jpg"));
        var belt = new TestConveyor();
        form.Shown += async (_, _) =>
        {
            try
            {
                for (var i = 0; i < 200 && Field<bool>(form, "_loading"); i++) await Task.Delay(25);
                await (Task)Call(form, "RefreshAsync", false)!;
                Field<System.Windows.Forms.Timer>(form, "_refresh").Stop();
                Field<System.Windows.Forms.Timer>(form, "_frames").Stop();
                Set(form, "_camera", feed);
                Set(form, "_conveyor", belt);
                await ConveyorProtocolChecks.RunAsync();
                var controls = AllControls(form).ToList();
                Check(!controls.Any(c => c.Text is "벨트 정지" || c.Text.Contains("검출 시 정지") || c.Text.Contains("자동 재구동") || c.Text.Contains("검사 완료·해제")),
                    "removed belt stop, detection-stop checkbox and restart/release UI");
                await (Task)Call(form, "RunConveyorAsync")!;
                Check(belt.IsRunning && belt.RunCount == 1, "manual RUN works before starting camera inspection");
                var start = (Task)Call(form, "StartInspectionAsync")!;
                Field<System.Windows.Forms.Timer>(form, "_inspectionTimer").Stop();
                await start;
                Check(server.InspectionCount == 0 && Field<PictureBox>(form, "_recognizedVideo").Image == null,
                    "empty scene produces no product result or stored inspection");
                Check(Field<Label>(form, "_result").Text.StartsWith("인식 대기 중"), "empty scene only shows recognition waiting message");
                feed.Image = phone;
                await (Task)Call(form, "InspectCameraAsync")!;
                Check(belt.IsRunning && belt.StopCount == 0, "real ONNX phone recognition sends no STOP");
                Check(server.InspectionCount == 1 && Field<bool>(form, "_runningInspection"), "moving detection saves first product and continues");
                Check(server.LastCameraInspection is { Confidence: >= 0.45 } stored && stored.ClassificationAvailable == expectedInspector.CanClassifyProducts
                    && stored.Result == expectedResult.Result, "moving inspection preserves combined-model result and existing quality policy");
                Check(Field<PictureBox>(form, "_video").Image != null, "YOLO result image displayed");
                var snapshot = Field<PictureBox>(form, "_recognizedVideo").Image;
                var resultText = Field<Label>(form, "_result").Text;
                Check(snapshot != null && resultText.Contains("#0001") && expectedResult.Detections.Any(x => YoloOnnxInspector.IsPhoneClass(x.ClassName) && resultText.Contains(x.ClassName)) && resultText.Contains("기록 저장 완료"),
                    "first recognition has matching frozen image, product number, class and saved status");
                var liveImage = Field<PictureBox>(form, "_video").Image;
                await (Task)Call(form, "InspectCameraAsync")!;
                Check(server.InspectionCount == 1 && belt.StopCount == 0, "same visible phone is not counted twice or stopped");
                Check(ReferenceEquals(snapshot, Field<PictureBox>(form, "_recognizedVideo").Image) && resultText == Field<Label>(form, "_result").Text,
                    "same product never replaces recognition snapshot or result text");
                Check(!ReferenceEquals(liveImage, Field<PictureBox>(form, "_video").Image), "live video keeps updating independently");
                feed.Image = blank.ToBytes(".jpg");
                await Task.Delay(300);
                await (Task)Call(form, "InspectCameraAsync")!;
                Check(!Field<bool>(form, "_productPresent"), "empty image rearms next product");
                Check(ReferenceEquals(snapshot, Field<PictureBox>(form, "_recognizedVideo").Image) && resultText == Field<Label>(form, "_result").Text,
                    "empty scene preserves last recognized product rather than publishing no-detection result");
                await (Task)Call(form, "RefreshAsync", false)!;
                Check(resultText == Field<Label>(form, "_result").Text, "server refresh cannot overwrite active recognition result");
                feed.Image = phone;
                await (Task)Call(form, "InspectCameraAsync")!;
                Check(server.InspectionCount == 2 && belt.IsRunning && belt.StopCount == 0, "next phone recorded while belt keeps running");
                Check(!ReferenceEquals(snapshot, Field<PictureBox>(form, "_recognizedVideo").Image) && Field<Label>(form, "_result").Text.Contains("#0002"),
                    "next distinct product replaces snapshot and advances displayed product number");
                Call(form, "StopInspection", "test inspection stop");
                await Task.Delay(50);
                Check(!Field<bool>(form, "_runningInspection") && belt.IsRunning && belt.StopCount == 0,
                    "inspection stop controls detection separately from belt");
                var stoppedResult = Field<Label>(form, "_result").Text;
                await (Task)Call(form, "RefreshAsync", false)!;
                Check(stoppedResult == Field<Label>(form, "_result").Text, "last recognized result remains after inspection stop and refresh");
                Check(server.LastCameraInspection?.ResultImage is { Length: > 0 }, "recognized photo is submitted with its inspection record");
                var records = await api.GetInspectionsAsync(1);
                Check(records.Count == 2 && records.All(r => r.HasImage), "each stored camera record advertises its own photo");
                Field<TabControl>(form, "_pages").SelectedIndex = 1;
                await Task.Delay(100);
                var history = Field<DataGridView>(form, "_history");
                history.CurrentCell = history.Rows[1].Cells[0];
                await (Task)Call(form, "ShowInspectionImageAsync")!;
                for (var i = 0; i < 100 && Field<PictureBox>(form, "_historyImage").Image == null; i++) await Task.Delay(20);
                Check(Field<PictureBox>(form, "_historyImage").Image != null && Field<Guid?>(form, "_historyImageId") == records[1].Id,
                    "selecting an older record loads its own image");
                await (Task)Call(form, "RefreshAsync", false)!;
                Check((Guid)history.CurrentRow!.Cells["검사번호"].Value == records[1].Id, "periodic refresh retains selected historical record");
                server.Inspections.Insert(0, new InspectionDto(Guid.NewGuid(), 1, InspectionResultType.Normal, "이전 기록", DateTimeOffset.Now));
                await (Task)Call(form, "RefreshAsync", false)!;
                history.CurrentCell = history.Rows[0].Cells[0];
                await Task.Delay(50);
                Check(Field<PictureBox>(form, "_historyImage").Image == null && Field<Label>(form, "_historyImageCaption").Text.Contains("사진 없음"),
                    "legacy record clears prior image and explicitly shows no saved photo");
                history.CurrentCell = history.Rows[1].Cells[0];
                for (var i = 0; i < 100 && Field<PictureBox>(form, "_historyImage").Image == null; i++) await Task.Delay(20);
                using (var preview = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(preview, new Rectangle(System.Drawing.Point.Empty, form.Size)); preview.Save(Path.Combine(AppContext.BaseDirectory, "history-photo.png")); }
                Field<TabControl>(form, "_pages").SelectedIndex = 0;
                foreach (var size in new[] { new System.Drawing.Size(1400, 900), new System.Drawing.Size(1200, 800) })
                {
                    form.Size = size;
                    form.PerformLayout();
                    var toolbars = Field<Button>(form, "_inspectionStart").Parent!.Parent!.Controls.OfType<FlowLayoutPanel>().ToArray();
                    Check(toolbars.Length == 3 && toolbars.All(panel => panel.Controls.Cast<Control>().Where(c => c.Visible)
                        .All(c => panel.ClientRectangle.Contains(c.Bounds))), $"continuous controls fit at {size.Width}x{size.Height}");
                    using var preview = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(preview, new Rectangle(System.Drawing.Point.Empty, form.Size));
                    preview.Save(Path.Combine(AppContext.BaseDirectory, $"moving-inspection-{size.Width}.png"));
                }
                Console.WriteLine("ALL MOVING-INSPECTION CHECKS PASSED; no physical COM port or motor used.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { Call(form, "DisconnectCamera"); form.Close(); }
        };
        Application.Run(form);
    }

    private static IEnumerable<Control> AllControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (var descendant in AllControls(child)) yield return descendant; }
    }

    private sealed class MutableFeed(byte[] image) : ICameraFeed
    {
        private long _sequence;
        public byte[] Image { get; set; } = image;
        public string? Error => null;
        public CameraFrame Latest => new(Image, ++_sequence, DateTime.UtcNow);
        public Task Completion => Task.CompletedTask;
        public void Dispose() { }
    }
    private sealed class TestConveyor : IConveyorController
    {
        public bool IsRunning { get; private set; }
        public int RunCount { get; private set; }
        public int StopCount { get; private set; }
        public Task RunAsync() { RunCount++; IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync() { StopCount++; IsRunning = false; return Task.CompletedTask; }
        public Task PingAsync() => Task.CompletedTask;
        public void Dispose() { IsRunning = false; }
    }
}

