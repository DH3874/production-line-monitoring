using ProductionLine.Shared;
using ProductionLine.UserApp;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using OpenCvSharp;
using ProductionLine.Vision;

namespace ProductionLine.Verification;

internal static class FunctionalChecks
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static T Field<T>(object f, string n) => (T)f.GetType().GetField(n, Flags)!.GetValue(f)!;
    static Task Call(object f, string n, params object[] args) => (Task)f.GetType().GetMethod(n, Flags)!.Invoke(f, args)!;
    static object? Invoke(object f, string n, params object[] args) => f.GetType().GetMethod(n, Flags)!.Invoke(f, args);
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    [STAThread]
    static void Main()
    {
        if (Environment.GetCommandLineArgs().Contains("--hardware"))
        {
            HardwareProtocolChecks.Run();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--lighting-controls"))
        {
            LightingControlChecks.Run();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--conveyor-protocol"))
        {
            ConveyorProtocolChecks.RunAsync().GetAwaiter().GetResult();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--full-phone"))
        {
            FullPhoneChecks.Run(Environment.GetCommandLineArgs().Skip(2).First());
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--moving-inspection"))
        {
            MovingInspectionChecks.Run();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--arduino-compile"))
        {
            ArduinoToolChecks.RunAsync(true).GetAwaiter().GetResult();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--arduino-tools"))
        {
            ArduinoToolChecks.RunAsync(false).GetAwaiter().GetResult();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--arduino-dialog"))
        {
            ArduinoToolChecks.ShowDialogCheck();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--phone-packages"))
        {
            PhonePackageChecks.Run(Environment.GetCommandLineArgs().Skip(2).First());
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--continuous-yolo"))
        {
            MovingInspectionChecks.Run();
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var server = new TestServer();
        using var api = new ProductionLineApiClient(server.Url);
        api.LoginAsync("test", "test").GetAwaiter().GetResult();
        using var form = new UserMainForm(api);
        var file = Field<string>(form, "_conversationFile");
        form.Shown += async (_, _) =>
        {
            try
            {
                for (var i = 0; i < 100 && Field<bool>(form, "_loading"); i++) await Task.Delay(50);
                await Call(form, "RefreshAsync", false);
                Field<System.Windows.Forms.Timer>(form, "_refresh").Stop();
                Check(Field<TabControl>(form, "_pages").TabCount == 5, "five pages");
                Check(Field<Label>(form, "_counts").Text.Contains("정상률"), "counts fetched");
                Field<TabControl>(form, "_pages").SelectedIndex = 2;
                await Task.Delay(100);
                await Call(form, "SubmitIssueAsync", new CreateIssueRequest(1, IssueType.InspectionSystem, "테스트 문제", "증상"));
                Check(server.LastCreate?.Title == "테스트 문제", "problem registration payload");
                var action = Field<TextBox>(form, "_action");
                Check(!Field<Button>(form, "_requestCompletion").Enabled, "completion disabled before action");
                action.Text = "단계 건너뛰기 시도";
                await Call(form, "SaveActionAsync", IssueStatus.CompletionRequested);
                Check(server.LastUpdate == null && action.Text == "단계 건너뛰기 시도", "invalid transition rejected without transmission");
                action.Text = "문제 증상 확인";
                await Call(form, "SaveActionAsync", IssueStatus.Acknowledged);
                Check(server.LastUpdate?.Status == IssueStatus.Acknowledged, "acknowledgment before action");
                action.Text = "새 조치만";
                await Call(form, "SaveActionAsync", IssueStatus.InProgress);
                Check(server.LastUpdate?.ActionNote == "새 조치만" && action.Text == "", "new note only; clear after success");
                server.Fail = true; action.Text = "실패해도 유지";
                await Call(form, "SaveActionAsync", IssueStatus.InProgress);
                Check(action.Text == "실패해도 유지", "action retained on server error");
                server.Fail = false;
                await Call(form, "SaveActionAsync", IssueStatus.InProgress);
                Check(server.LastUpdate?.ActionNote == "실패해도 유지" && action.Text == "", "action retry");
                Check(Field<TextBox>(form, "_issueDetail").Text.Contains("마지막 조치 메모: 실패해도 유지"), "latest note displayed separately from full history");
                Check(Field<Label>(form, "_lineInfo").Text.Contains("실제 라인"), "line status and type displayed");
                action.Text = "완료 확인";
                await Call(form, "SaveActionAsync", IssueStatus.CompletionRequested);
                Check(server.LastUpdate?.Status == IssueStatus.CompletionRequested && action.ReadOnly, "completion request locks editing while awaiting admin");
                Check(!Field<Button>(form, "_saveAction").Enabled, "no user reversal after completion request");
                using (var preview = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(preview, new Rectangle(System.Drawing.Point.Empty, form.Size));
                    preview.Save(Path.Combine(AppContext.BaseDirectory, "db-issue-page.png"));
                }
                var input = Field<TextBox>(form, "_aiInput"); input.Text = "무엇부터 확인하나요?";
                await Call(form, "SendChatAsync", false);
                Check(Field<RichTextBox>(form, "_aiMessages").Text.Contains("테스트 근거"), "AI answer and source");
                server.Fail = true; input.Text = "보존할 질문";
                await Call(form, "SendChatAsync", false);
                Check(input.Text == "보존할 질문" && Field<Button>(form, "_aiSend").Enabled, "AI failed input retained; retry enabled");
                server.Fail = false;
                await Call(form, "SendChatAsync", false);
                Check(input.Text == "", "AI retry");
                Field<TextBox>(form, "_adminInput").Text = "관리자 확인 요청";
                await Call(form, "SendChatAsync", true);
                Check(server.LastMessage == "관리자 확인 요청", "admin inquiry sent as issue message");
                using (var restored = new UserMainForm(api))
                {
                    var restoredEntries = Field<List<UserMainForm.ChatEntry>>(restored, "_conversations");
                    Check(restoredEntries.Count == 4 && restoredEntries.Any(x => x.Text.Contains("보존할 질문")),
                        "AI conversation restored from disk; admin messages remain server-side");
                }
                // 실제 서버에는 기록하지 않고 로컬 테스트 서버에만 검사 결과를 전송합니다.
                {
                    var inspector = new VisionInspector();
                    var normal = inspector.Inspect(new(1, InspectionResultType.Normal, "정상 시연"));
                    var defect = inspector.Inspect(new(1, InspectionResultType.Defect, "불량 시연"));
                    Check(normal.Result == InspectionResultType.Normal && defect.Result == InspectionResultType.Defect, "explicit simulation results do not require legacy scores");
                }
                await Call(form, "StartInspectionAsync");
                Check(!Field<bool>(form, "_runningInspection"), "inspection blocked without camera/reference");
                Invoke(form, "ConnectCamera");
                for (int i = 0; i < 100 && Field<object?>(form, "_camera") != null; i++)
                {
                    var camera = Field<object>(form, "_camera");
                    if (camera.GetType().GetProperty("Latest")!.GetValue(camera) != null || camera.GetType().GetProperty("Error")!.GetValue(camera) != null) break;
                    await Task.Delay(100);
                }
                var activeCamera = Field<object?>(form, "_camera");
                if (activeCamera?.GetType().GetProperty("Latest")!.GetValue(activeCamera) != null)
                {
                    Invoke(form, "SetReference");
                    await Call(form, "StartInspectionAsync");
                    Check(server.InspectionCount > 0, "live camera analysis submitted to test server");
                    Invoke(form, "StopInspection", "test stop");
                    int count = server.InspectionCount;
                    await Task.Delay(1700);
                    Check(server.InspectionCount == count, "stop prevents further inspection requests");
                    server.Fail = true;
                    await Call(form, "StartInspectionAsync");
                    Check(!Field<bool>(form, "_runningInspection"), "server error stops inspection");
                    server.Fail = false;
                }
                else Console.WriteLine("SKIP: physical camera not available; live capture/inspection cannot be verified on this device");
                Invoke(form, "DisconnectCamera");
                Check(!Field<bool>(form, "_runningInspection"), "disconnect stops inspection");
                // 장치 유무와 무관하게 같은 검사 루프를 합성 프레임으로 검증합니다.
                using (var image = new Mat(120, 160, MatType.CV_8UC3, Scalar.White))
                {
                    typeof(UserMainForm).GetField("_camera", Flags)!.SetValue(form, new TestFeed(image.ToBytes(".jpg")));
                    Invoke(form, "SetReference");
                    int before = server.InspectionCount;
                    await Call(form, "StartInspectionAsync");
                    Check(server.InspectionCount == before + 1, "start analyzes a frame and sends existing inspection contract");
                    Invoke(form, "StopInspection", "test stop");
                    int stopped = server.InspectionCount;
                    await Task.Delay(1700);
                    Check(server.InspectionCount == stopped, "stop halts periodic requests");
                    server.Fail = true;
                    await Call(form, "StartInspectionAsync");
                    Check(!Field<bool>(form, "_runningInspection"), "inspection auto-stops on server failure");
                    server.Fail = false;
                    Invoke(form, "DisconnectCamera");
                }
                Field<TabControl>(form, "_pages").SelectedIndex = 0;
                using (var preview = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(preview, new Rectangle(System.Drawing.Point.Empty, form.Size)); preview.Save(Path.Combine(AppContext.BaseDirectory, "camera-page.png")); }
                server.Dispose(); input.Text = "연결 끊겨도 유지";
                await Call(form, "SendChatAsync", false);
                Check(input.Text == "연결 끊겨도 유지", "input retained on connection failure");
                Console.WriteLine("ALL CHECKS PASSED; production data unchanged");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { if (File.Exists(file)) File.Delete(file); form.Close(); }
        };
        Application.Run(form);
    }
}

internal sealed class TestFeed(byte[] jpeg) : ICameraFeed
{
    long _sequence;
    public string? Error => null;
    public CameraFrame Latest => new(jpeg, Interlocked.Increment(ref _sequence), DateTime.UtcNow);
    public Task Completion => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class TestServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    IssueDto _issue = new(Guid.NewGuid(), 1, IssueType.InspectionSystem, "초기 문제", "증상", IssueStatus.Occurred, "test", "과거 조치", DateTimeOffset.Now, DateTimeOffset.Now);
    public string Url { get; }
    public bool Fail { get; set; }
    public CreateIssueRequest? LastCreate { get; private set; }
    public UpdateIssueStatusRequest? LastUpdate { get; private set; }
    public string? LastMessage { get; private set; }
    public int InspectionCount { get; private set; }
    public CameraInspectionRequest? LastCameraInspection { get; private set; }
    public List<InspectionDto> Inspections { get; } = [];
    public Dictionary<Guid, byte[]> Images { get; } = new();
    public TestServer()
    {
        var picker = new TcpListener(IPAddress.Loopback, 0); picker.Start();
        var port = ((IPEndPoint)picker.LocalEndpoint).Port; picker.Stop();
        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url); _listener.Start(); _ = Task.Run(Serve);
    }
    async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext c;
            try { c = await _listener.GetContextAsync(); } catch { break; }
            try
            {
                object result;
                var path = c.Request.Url!.AbsolutePath;
                if (Fail) { c.Response.StatusCode = 503; result = new ApiMessage("테스트 서버 오류"); }
                else if (path == "/api/auth/login") result = new LoginResponse("test-token", new UserDto("verification-" + Environment.ProcessId, "검증 사용자", UserRole.User, true, new[] { 1 }));
                else if (path == "/api/lines") result = new[] { new LineStatusDto(1, "테스트 라인", true, "가동", "검사 중", 10, 2, 1, DateTimeOffset.Now) };
                else if (path == "/api/inspections") result = Inspections.ToArray();
                else if (path.StartsWith("/api/inspections/") && path.EndsWith("/image"))
                {
                    var id = Guid.Parse(path.Split('/')[3]);
                    c.Response.ContentType = "image/jpeg";
                    await c.Response.OutputStream.WriteAsync(Images[id]);
                    continue;
                }
                else if (path == "/api/inspections/simulate")
                {
                    var request = (await JsonSerializer.DeserializeAsync<InspectionRequest>(c.Request.InputStream, _json))!;
                    result = new VisionInspector().Inspect(request); InspectionCount++;
                }
                else if (path == "/api/inspections/camera")
                {
                    var request = (await JsonSerializer.DeserializeAsync<CameraInspectionRequest>(c.Request.InputStream, _json))!;
                    LastCameraInspection = request;
                    result = new InspectionDto(Guid.NewGuid(), request.LineId,
                        request.ClassificationAvailable ? request.Result : InspectionResultType.Undetermined,
                        request.Reason, DateTimeOffset.Now, request.Confidence, request.ResultImage != null);
                    var inspection = (InspectionDto)result;
                    Inspections.Insert(0, inspection);
                    if (request.ResultImage != null) Images[inspection.Id] = request.ResultImage;
                    InspectionCount++;
                }
                else if (path.EndsWith("/messages") && c.Request.HttpMethod == "GET") result = Array.Empty<IssueMessageDto>();
                else if (path.EndsWith("/messages") && c.Request.HttpMethod == "POST")
                {
                    var request = (await JsonSerializer.DeserializeAsync<SendIssueMessageRequest>(c.Request.InputStream, _json))!;
                    LastMessage = request.Message;
                    result = new IssueMessageDto(1, _issue.Id, "test", "검증 사용자", UserRole.User,
                        request.Message, DateTimeOffset.Now);
                }
                else if (path == "/api/issues" && c.Request.HttpMethod == "GET") result = new[] { _issue };
                else if (path == "/api/issues")
                {
                    LastCreate = (await JsonSerializer.DeserializeAsync<CreateIssueRequest>(c.Request.InputStream, _json))!;
                    result = _issue with { Id = Guid.NewGuid(), Title = LastCreate.Title, Description = LastCreate.Description };
                }
                else if (path.EndsWith("/status"))
                {
                    LastUpdate = (await JsonSerializer.DeserializeAsync<UpdateIssueStatusRequest>(c.Request.InputStream, _json))!;
                    _issue = _issue with { Status = LastUpdate.Status, ActionNote = LastUpdate.ActionNote }; result = _issue;
                }
                else if (path == "/api/ai/consult") result = new AiConsultResponse("전원 확인", "테스트 근거", true);
                else { c.Response.StatusCode = 404; result = new ApiMessage("not found"); }
                c.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(c.Response.OutputStream, result, result.GetType(), _json);
            }
            finally { c.Response.Close(); }
        }
    }
    public void Dispose() { if (_listener.IsListening) _listener.Stop(); }
}

