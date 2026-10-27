using System.Reflection;
using System.Text.Json;
using ProductionLine.UserApp;

namespace ProductionLine.Verification;

internal static class ArduinoToolChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync(bool realCompile)
    {
        if (realCompile)
        {
            var tools = new ArduinoFirmwareTools();
            var log = new InlineProgress();
            var actualBuild = await tools.CompileAsync(log, CancellationToken.None);
            Check(File.Exists(actualBuild.HexPath), "actual Mega 2560 sketch compiles using bundled CLI and official AVR core");
            Console.WriteLine("HEX: " + actualBuild.HexPath);
            var detected = await tools.DiscoverAsync(CancellationToken.None);
            Console.WriteLine("Detected serial ports: " + detected.Count);
            Console.WriteLine("No upload or motor command executed.");
            return;
        }
        var root = Path.Combine(Path.GetTempPath(), "ProductionLine-Arduino-Test-" + Guid.NewGuid().ToString("N"));
        var core = Path.Combine(root, "data", "packages", "arduino", "hardware", "avr", ArduinoFirmwareTools.CoreVersion);
        Directory.CreateDirectory(core);
        File.WriteAllText(Path.Combine(core, "platform.txt"), "test marker");
        var runner = new FakeRunner();
        var service = new ArduinoFirmwareTools(root: root, runner: runner);
        var build = await service.CompileAsync(null, CancellationToken.None);
        Check(runner.Commands.Count == 1 && runner.Commands[0][0] == "compile", "prepared core reused without downloading");
        Check(runner.Commands[0].Contains(ArduinoFirmwareTools.MegaFqbn), "compile targets ATmega2560 explicitly");
        Check(File.Exists(Path.Combine(build.SketchDirectory, "ConveyorYoloControl.ino")), "current app sketch copied into isolated build job");
        Check(File.Exists(Path.Combine(build.SketchDirectory, "Adafruit_NeoPixel.h")) &&
              File.Exists(Path.Combine(build.SketchDirectory, "Adafruit_NeoPixel.cpp")) &&
              File.Exists(Path.Combine(build.SketchDirectory, "COPYING")),
            "D7 ring LED driver and its license are included in the isolated upload build");
        Check(runner.Commands.All(values => !values.Contains("upload")), "compile-only never flashes hardware");
        foreach (var dependency in new[] { "DHT.h", "DHT.cpp", "Servo.h", "Servo.cpp", "avr/ServoTimers.h", "DHT_LICENSE.txt", "SERVO_LICENSE.txt" })
            Check(File.Exists(Path.Combine(build.SketchDirectory, dependency)), "hardware upload includes " + dependency);
        using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "arduino-cli.yaml"))))
            Check(config.RootElement.GetProperty("directories").GetProperty("data").GetString() == Path.Combine(root, "data"), "CLI configuration is isolated from Arduino IDE data");
        await service.UploadAsync(build, "COM42", null, CancellationToken.None);
        Check(runner.Commands.Last().Contains("--verify") && runner.Commands.Last().Contains("COM42"), "upload uses chosen port and verifies written flash");
        Check(runner.Commands.Last().Contains(build.BuildDirectory), "upload uses exactly the freshly compiled build directory");
        var before = runner.Commands.Count;
        try { await service.UploadAsync(build, "COM42 & bad", null, CancellationToken.None); throw new InvalidOperationException("Expected invalid port"); }
        catch (ArgumentException) { Check(runner.Commands.Count == before, "invalid port rejected before starting process"); }
        File.AppendAllText(build.HexPath, "tampered");
        try { await service.UploadAsync(build, "COM42", null, CancellationToken.None); throw new InvalidOperationException("Expected changed HEX rejection"); }
        catch (IOException) { Check(runner.Commands.Count == before, "changed build cannot be uploaded"); }
        runner.FailCommand = "compile";
        try { await service.CompileAsync(null, CancellationToken.None); throw new InvalidOperationException("Expected compile failure"); }
        catch (IOException) { Check(runner.Commands.Count(values => values[0] == "upload") == 1, "compile failure does not trigger upload"); }
        runner.FailCommand = null;
        using (var fileLock = new FileStream(Path.Combine(root, "operation.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            before = runner.Commands.Count;
            try { await service.CompileAsync(null, CancellationToken.None); throw new InvalidOperationException("Expected concurrent operation rejection"); }
            catch (IOException) { Check(runner.Commands.Count == before, "another app cannot concurrently alter the toolchain"); }
        }
        var ports = ArduinoFirmwareTools.ParsePorts("""
            {"detected_ports":[
              {"port":{"address":"COM8"},"matching_boards":[{"name":"Arduino Mega","fqbn":"arduino:avr:mega"}]},
              {"port":{"address":"COM1"}},
              {"port":{"address":"COM9"},"matching_boards":[{"name":"Uno","fqbn":"arduino:avr:uno"}]},
              {"port":{"address":"192.0.2.10"}}
            ]}
            """);
        Check(ports.Count == 3 && ports.Single(value => value.Address == "COM8").Fqbn == ArduinoFirmwareTools.MegaFqbn,
            "only Mega is automatically identified as supported board");
        Check(ports.Single(value => value.Address == "COM1").Fqbn == null, "ordinary COM1 is not assumed to be an Arduino");
        var process = new ArduinoProcessRunner();
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var result = await process.RunAsync(shell, ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.WriteLine('out'); [Console]::Error.WriteLine('err'); exit 9"], root,
            TimeSpan.FromSeconds(15), null, CancellationToken.None);
        Check(result.ExitCode == 9 && result.Output.Contains("out") && result.Error.Contains("err"), "hidden process runner preserves exit code and both output streams");
        try
        {
            await process.RunAsync(shell, ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"], root, TimeSpan.FromMilliseconds(200), null, CancellationToken.None);
            throw new InvalidOperationException("Expected process timeout");
        }
        catch (TimeoutException) { Check(true, "hung child process is terminated on timeout"); }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await service.CompileAsync(null, cancelled.Token); throw new InvalidOperationException("Expected cancellation"); }
        catch (OperationCanceledException) { Check(true, "cancelled operation does not continue to upload"); }
        Console.WriteLine("ARDUINO TOOL CHECKS PASSED; all upload commands used a fake runner.");
    }

    public static void ShowDialogCheck()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        var verificationRoot = Path.Combine(Path.GetTempPath(), "ProductionLine-Arduino-Dialog-" + Guid.NewGuid().ToString("N"));
        using var dialog = new ArduinoSetupForm(tools: new ArduinoFirmwareTools(root: verificationRoot));
        dialog.Shown += async (_, _) =>
        {
            for (var i = 0; i < 150 && (bool)typeof(ArduinoSetupForm).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!; i++)
                await Task.Delay(100);
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                foreach (var width in new[] { 820, 700 })
                {
                    dialog.Size = new Size(width, width == 820 ? 610 : 500);
                    dialog.PerformLayout();
                    foreach (var field in new[] { "_prepare", "_compile", "_upload", "_cancel", "_close" })
                    {
                        var button = (Button)typeof(ArduinoSetupForm).GetField(field, flags)!.GetValue(dialog)!;
                        Check(button.Parent!.ClientRectangle.Contains(button.Bounds), $"{field} visible at dialog width {width}");
                    }
                    var upload = (Button)typeof(ArduinoSetupForm).GetField("_upload", flags)!.GetValue(dialog)!;
                    var ports = (ComboBox)typeof(ArduinoSetupForm).GetField("_ports", flags)!.GetValue(dialog)!;
                    Check(ports.SelectedIndex < 0 && !upload.Enabled, "upload disabled until a board port is selected");
                    using var image = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                    image.Save(Path.Combine(AppContext.BaseDirectory, $"arduino-setup-{width}.png"));
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { dialog.Close(); }
        };
        Application.Run(dialog);
    }

    private sealed class InlineProgress : IProgress<string> { public void Report(string value) => Console.WriteLine(value); }
    private sealed class FakeRunner : IArduinoCommandRunner
    {
        public List<string[]> Commands { get; } = [];
        public string? FailCommand { get; set; }
        public Task<ArduinoCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory,
            TimeSpan timeout, IProgress<string>? progress, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var values = arguments.Skip(3).ToArray(); Commands.Add(values);
            if (values[0] == FailCommand) return Task.FromResult(new ArduinoCommandResult(1, "", "synthetic failure"));
            if (values[0] == "compile")
            {
                var output = values[Array.IndexOf(values, "--output-dir") + 1];
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "ConveyorYoloControl.ino.hex"), ":00000001FF\n");
            }
            return Task.FromResult(new ArduinoCommandResult(0, "success", ""));
        }
    }
}
