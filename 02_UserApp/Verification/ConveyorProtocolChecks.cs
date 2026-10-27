using ProductionLine.UserApp;

namespace ProductionLine.Verification;

internal static class ConveyorProtocolChecks
{
    public static async Task RunAsync()
    {
        var transport = new SimulatedTransport();
        var unknown = new SimulatedTransport { ReplyOverride = "ERROR UNKNOWN_COMMAND RXHEX=504E47" };
        using (var diagnostic = new ConveyorSerialController(unknown))
        {
            try { await diagnostic.PingAsync(); throw new Exception("Unknown command accepted"); }
            catch (IOException ex) { Check(ex.Message.Contains("PING") && ex.Message.Contains("전송=[PING]") && ex.Message.Contains("RXHEX=504E47"),
                "unknown command reports the exact sent request and received bytes without retry"); }
            Check(unknown.Commands.Count==1,"unknown command is never silently resent");
        }
        using (var restarted = new ConveyorSerialController(new SimulatedTransport { ReplyOverride = "PL_CONVEYOR_PUL_DIR_V7 STOPPED" }))
        {
            try { await restarted.PingAsync(); throw new Exception("Reset ignored"); }
            catch (IOException ex) { Check(ex.Message.Contains("재시작") && !restarted.IsRunning,"board restart is reported explicitly without automatically running"); }
        }
        var configurableTransport = new SimulatedTransport { Identity = "PL_CONVEYOR_PUL_DIR_V7 STOPPED" };
        using (var v7 = new ConveyorSerialController(configurableTransport))
        {
            await v7.HandshakeAsync();
            Check(v7.SupportsConfigurableBoost && v7.SupportsRecognitionBoost, "V7 advertises configurable boost");
            await v7.RecognizeAsync(Guid.NewGuid(),DateTime.UtcNow,2000,1000,true,5000);
            Check(configurableTransport.Commands.Last().EndsWith(" 1 5000"), "V7 sends selected boost duration atomically with sorting");
            var count=configurableTransport.Commands.Count;
            try { await v7.RecognizeAsync(Guid.NewGuid(),DateTime.UtcNow,2000,1000,false,60001); throw new Exception("Invalid boost accepted"); }
            catch (ArgumentOutOfRangeException) { Check(configurableTransport.Commands.Count==count,"invalid duration never reaches hardware"); }
        }
        var boostTransport = new SimulatedTransport { Identity = "PL_CONVEYOR_PUL_DIR_V6 STOPPED" };
        using (var v6 = new ConveyorSerialController(boostTransport))
        {
            await v6.HandshakeAsync();
            Check(v6.SupportsRecognitionBoost && v6.SupportsHardware, "V6 advertises recognition acceleration and sensor support");
            await v6.RecognizeAsync(Guid.NewGuid(),DateTime.UtcNow.AddMilliseconds(-500),2000,1000,true);
            Check(boostTransport.Commands.Last().StartsWith("PRODUCT ") && boostTransport.Commands.Last().EndsWith(" 1") &&
                !boostTransport.Commands.Any(c=>c.StartsWith("SORT ")), "defect boost and sorting are sent as one atomic product command");
        }
        var longTransport = new SimulatedTransport { Identity = "PL_CONVEYOR_PUL_DIR_V5 STOPPED" };
        using (var modern = new ConveyorSerialController(longTransport))
        {
            await modern.HandshakeAsync();
            Check(modern.SupportsHardware, "V5 keeps sensor support");
            await modern.QueueSortAsync(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(60), 60000);
            Check(longTransport.Commands.Last().StartsWith("SORT ") && longTransport.Commands.Last().EndsWith(" 60000"), "V5 accepts long servo timing");
        }
        var v4Transport = new SimulatedTransport { Identity = "PL_CONVEYOR_PUL_DIR_V4 STOPPED" };
        using (var older = new ConveyorSerialController(v4Transport))
        {
            await older.HandshakeAsync();
            try { await older.QueueSortAsync(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(60), 60000); throw new Exception("Expected firmware upgrade requirement"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("V5") && !v4Transport.Commands.Any(c => c.StartsWith("SORT ")), "old firmware gets upload guidance before sending unsupported timing"); }
        }
        var lightOnlyTransport = new SimulatedTransport { MotorRunning = true, Identity = "PL_CONVEYOR_PUL_DIR_V3 RUNNING" };
        using (var lightOnly = new ConveyorSerialController(lightOnlyTransport))
        {
            await lightOnly.InitializeAsync(stopMotorOnConnect: false);
            await lightOnly.SetLightAsync(false);
            Check(lightOnly.IsRunning && !lightOnlyTransport.Commands.Contains("STOP") &&
                  !lightOnlyTransport.Commands.Contains("RUN"), "light-only connection does not send motor commands");
        }
        using var controller = new ConveyorSerialController(transport);
        await controller.HandshakeAsync();
        Check(!controller.IsRunning, "conveyor identity handshake without moving hardware");
        var run = controller.RunAsync();
        var stop = controller.StopAsync();
        await Task.WhenAll(run, stop);
        Check(!controller.IsRunning && transport.Commands.TakeLast(2).SequenceEqual(["RUN", "STOP"]),
            "RUN and STOP preserve order and require device acknowledgments");
        await controller.RunAsync();
        Check(controller.IsRunning, "RUNNING acknowledgment updates state");
        await controller.RefreshLightAsync();
        Check(controller.IsLightOn == true && controller.IsRunning, "D7 light status leaves a running conveyor running");
        await controller.SetLightAsync(false);
        Check(controller.IsLightOn == false && controller.IsRunning, "turning light off does not change motor state");
        await controller.SetLightAsync(true);
        Check(controller.IsLightOn == true && controller.IsRunning, "turning light on is acknowledged independently");
        transport.MotorRunning = false;
        await controller.PingAsync();
        Check(!controller.IsRunning, "heartbeat sees device watchdog stop");
        Check(controller.IsLightOn == true, "motor status responses preserve illumination state");
        transport.FailCommand = true;
        try { await controller.SetLightAsync(false); throw new InvalidOperationException("Expected LED failure"); }
        catch (IOException) { Check(controller.IsLightOn == true, "failed LED command cannot claim light was turned off"); }
        transport.FailCommand = true;
        try { await controller.StopAsync(); throw new InvalidOperationException("Expected STOP failure"); }
        catch (IOException) { Console.WriteLine("PASS: device command error is not treated as successful stop"); }

        using var silent = new ConveyorSerialController(new SilentTransport());
        try { await silent.HandshakeAsync(); throw new InvalidOperationException("Expected identity failure"); }
        catch (TimeoutException) { Console.WriteLine("PASS: incompatible firmware cannot pass handshake"); }
        var oldTransport = new SimulatedTransport { Identity = "PL_CONVEYOR_V1 STOPPED" };
        using var oldController = new ConveyorSerialController(oldTransport);
        try { await oldController.HandshakeAsync(); throw new InvalidOperationException("Expected old PWM firmware rejection"); }
        catch (IOException) { Check(!oldTransport.Commands.Contains("RUN"), "old PWM firmware rejected before motor RUN"); }
        var wrongPins = new SimulatedTransport { Identity = "PL_CONVEYOR_PUL_DIR_V2 STOPPED" };
        using var wrongPinController = new ConveyorSerialController(wrongPins);
        try { await wrongPinController.HandshakeAsync(); throw new InvalidOperationException("Expected wrong pin firmware rejection"); }
        catch (IOException ex) { Check(!wrongPins.Commands.Contains("RUN") && ex.Message.Contains("5번") && ex.Message.Contains("6번"),
            "previous 2/3 pin firmware is rejected with correct upload guidance before RUN"); }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class SimulatedTransport : IConveyorTransport
    {
        private readonly Queue<string> _responses = new();
        public List<string> Commands { get; } = [];
        public bool MotorRunning { get; set; }
        public bool LightOn { get; set; } = true;
        public bool FailCommand { get; set; }
        public string? ReplyOverride { get; set; }
        public string Identity { get; set; } = "PL_CONVEYOR_PUL_DIR_V3 STOPPED";
        public void WriteLine(string value)
        {
            Commands.Add(value);
            if (ReplyOverride != null) { _responses.Enqueue(ReplyOverride); return; }
            if (value.StartsWith("PRODUCT ")) { _responses.Enqueue("PRODUCT_OK " + value.Split(' ')[1]); return; }
            if (value.StartsWith("SORT ")) { _responses.Enqueue("SORT_QUEUED " + value.Split(' ')[1]); return; }
            if (FailCommand) { _responses.Enqueue("ERROR SYNTHETIC"); return; }
            if (value == "RUN") MotorRunning = true;
            if (value == "STOP") MotorRunning = false;
            if (value == "LED_ON") LightOn = true;
            if (value == "LED_OFF") LightOn = false;
            if (value is "LED_ON" or "LED_OFF" or "LED_STATUS")
            { _responses.Enqueue(LightOn ? "LED_ON" : "LED_OFF"); return; }
            _responses.Enqueue(value == "HELLO" ? Identity : MotorRunning ? "RUNNING" : "STOPPED");
        }
        public string ReadLine()
        {
            if (_responses.Count > 0) return _responses.Dequeue();
            Thread.Sleep(25);
            throw new TimeoutException();
        }
        public void Dispose() { }
    }

    private sealed class SilentTransport : IConveyorTransport
    {
        public void WriteLine(string value) { }
        public string ReadLine() { Thread.Sleep(25); throw new TimeoutException(); }
        public void Dispose() { }
    }
}
