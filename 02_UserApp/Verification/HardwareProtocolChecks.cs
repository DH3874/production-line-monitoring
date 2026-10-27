using ProductionLine.UserApp;
using ProductionLine.Shared;

namespace ProductionLine.Verification;

internal static class HardwareProtocolChecks
{
    public static void Run()
    {
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        const string sample = "HW {\"temperatureCelsius\":24.5,\"humidityPercent\":53,\"gasDetected\":false,\"emergencyStopped\":false,\"beltRunning\":true,\"sorterAngle\":45,\"sorting\":true}";
        var parsed = HardwareSerialProtocol.Parse(sample);
        Check(parsed.TemperatureCelsius == 24.5 && parsed.HumidityPercent == 53 && parsed.SorterAngle == 45 && parsed.BeltRunning, "hardware sensor and servo status parsed");
        var failed = HardwareSerialProtocol.Parse(sample.Replace("24.5", "null").Replace("\"gasDetected\":false", "\"gasDetected\":null"));
        Check(failed.TemperatureCelsius == null && failed.GasDetected == null, "missing measurement never becomes normal or zero");
        var analogJson=sample.Replace("\"gasDetected\":false","\"gasDetected\":null,\"gasAnalogValue\":348,\"gasWarmingUp\":true");
        var analog=HardwareSerialProtocol.Parse(analogJson);
        Check(analog.GasAnalogValue==348 && analog.GasWarmingUp && analog.GasDetected==null,"A0 analog gas input and warmup flag survive parsing");
        try { HardwareSerialProtocol.Parse(analogJson.Replace("\"gasAnalogValue\":348","\"gasAnalogValue\":1024"));throw new Exception("Invalid ADC accepted"); }
        catch(IOException) {Check(true,"ADC value above 1023 rejected");}
        try { HardwareSerialProtocol.Parse(sample.Replace("\"emergencyStopped\":false", "\"emergencyStopped\":true")); throw new Exception("unsafe status accepted"); }
        catch (IOException) { Check(true, "contradictory emergency and running state rejected"); }
        try { HardwareSerialProtocol.Parse(sample.Replace("\"humidityPercent\":53", "\"humidityPercent\":153")); throw new Exception("invalid humidity accepted"); }
        catch (IOException) { Check(true, "out of range sensor value rejected"); }
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        Check(HardwareSerialProtocol.CreateSortCommand(id, now.AddSeconds(2), 1000, now.AddMilliseconds(500)) == $"SORT {id:N} 1420 1000", "inference and serial transit time deducted from sort delay");
        Check(HardwareSerialProtocol.CreateSortCommand(id,now.AddSeconds(60),60000,now)==$"SORT {id:N} 59920 60000", "60-second travel and hold remain valid after serial transit allowance");
        try { HardwareSerialProtocol.CreateSortCommand(id, now, 1000, now); throw new Exception("late job accepted"); }
        catch (InvalidOperationException) { Check(true, "expired sort rejected instead of sorting next product"); }
        var stale = new LineHardwareStatusDto(true,24,50,false,false,true,0,false,now.AddSeconds(-6));
        Check(!stale.IsFresh && HardwareStatusText.Format(stale).Contains("통신"), "stale hardware becomes unknown in dashboard");
        Check(HardwareStatusText.Format(null).Contains("미연결"), "unconnected sensors are shown explicitly");
    }
}
