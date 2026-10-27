using System.Reflection;
using ProductionLine.Shared;
using ProductionLine.UserApp;

namespace ProductionLine.Verification;

internal static class LightingControlChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        Application.EnableVisualStyles();
        using var api = new ProductionLineApiClient();
        using var form = new UserMainForm(api);
        T Field<T>(string name) => (T)typeof(UserMainForm).GetField(name, Flags)!.GetValue(form)!;
        void Set(string name, object value) => typeof(UserMainForm).GetField(name, Flags)!.SetValue(form, value);
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Console.WriteLine("PASS: " + message);
        }
        var on = Field<Button>("_lightToggle");
        var off = Field<Button>("_lightOff");
        Check(on.Enabled && off.Enabled, "both light buttons enabled before Arduino connection");
        Check(on.Parent == off.Parent && on.Parent != Field<Button>("_conveyorRun").Parent,
            "lighting has its own row separate from conveyor controls");
        var controller = new TestLightingController();
        Set("_conveyor", controller);
        Set("_runningInspection", true);
        Set("_inspectionBusy", true);
        void Send(bool enabled) => ((Task)typeof(UserMainForm).GetMethod("SetInspectionLightAsync", Flags)!
            .Invoke(form, [enabled])!).GetAwaiter().GetResult();
        Send(true);
        Check(controller.IsLightOn == true && controller.IsRunning, "light on works during inspection and motor operation");
        Send(false);
        Check(controller.IsLightOn == false && controller.IsRunning && controller.MotorCommands == 0,
            "light off during active inspection never sends motor commands");
        Check(on.Enabled && off.Enabled, "both buttons remain usable after commands");
        Set("_inspectionBusy", false);
        Set("_runningInspection", false);
    }

    private sealed class TestLightingController : IConveyorController, IConveyorLightingController
    {
        public bool IsRunning => true;
        public bool? IsLightOn { get; private set; }
        public int MotorCommands { get; private set; }
        public Task RunAsync() { MotorCommands++; return Task.CompletedTask; }
        public Task StopAsync() { MotorCommands++; return Task.CompletedTask; }
        public Task PingAsync() => Task.CompletedTask;
        public Task SetLightAsync(bool enabled) { IsLightOn = enabled; return Task.CompletedTask; }
        public Task RefreshLightAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
