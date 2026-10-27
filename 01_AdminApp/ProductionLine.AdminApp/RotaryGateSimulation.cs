using System;

namespace _01_AdminApp
{
    internal enum VirtualProductFlow { NormalOnly, DefectOnly, Mixed }
    // 그림과 상태 계산을 분리한 회전판 시연. 실제 라인/서버는 변경하지 않습니다.
    internal sealed class RotaryGateSimulation
    {
        internal enum Step { Feeding, Inspecting, ToGate, Passing, Deploying, Deflecting, Holding, Retracting, Gap }
        public Step Stage { get; private set; } = Step.Feeding;
        private double _elapsed;
        private int? _configuredLineId;
        public VirtualProductFlow Flow { get; private set; } = VirtualProductFlow.Mixed;
        public int CompletedNormalCount { get; private set; }
        public int CompletedDefectCount { get; private set; }
        public static string ProfileName(int lineId) => lineId switch
        { 2 => "정상 전용", 3 => "불량 전용", _ => "정상·불량 혼합" };
        public static string ProfileDescription(int lineId) => lineId switch
        { 2 => "정상만 직진 통과", 3 => "불량만 회전 분류", _ => "정상·불량 교대 검사" };
        public string DemoDescription => Flow switch
        {
            VirtualProductFlow.NormalOnly => "시연: 정상만 반복 · 회전판 고정",
            VirtualProductFlow.DefectOnly => "시연: 불량만 반복 · 회전판 분류",
            _ => "시연: 정상 / 불량 교대 · 회전판 분류"
        };
        public void ConfigureLine(int lineId)
        {
            // Repeated server refreshes must not restart a line or synchronize the cards.
            if (_configuredLineId == lineId) return;
            _configuredLineId = lineId;
            Flow = lineId switch { 2 => VirtualProductFlow.NormalOnly, 3 => VirtualProductFlow.DefectOnly, _ => VirtualProductFlow.Mixed };
            Stage = Step.Feeding;
            _elapsed = 0;
            CompletedNormalCount = CompletedDefectCount = 0;
            IsDefective = Flow == VirtualProductFlow.DefectOnly;
            // Different initial positions make each line immediately distinguishable.
            Advance(lineId switch { 2 => 0, 3 => 5.65, _ => 1.9 });
        }
        public bool IsDefective { get; private set; }
        public bool Inspected => Stage != Step.Feeding && Stage != Step.Inspecting;
        public bool ProductVisible => Stage != Step.Gap;
        public bool BeltMoving => Stage == Step.Feeding || Stage == Step.ToGate || Stage == Step.Passing || Stage == Step.Deflecting;
        private double Progress => Math.Clamp(_elapsed / Duration(Stage), 0, 1);
        public double GateAngleDegrees => Stage switch
        {
            Step.Deploying => 55 * Smooth(Progress),
            Step.Deflecting or Step.Holding => 55,
            Step.Retracting => 55 * (1 - Smooth(Progress)),
            _ => 0
        };
        // First contact occurs beside the slanted plate; the product then slides up along it.
        private double Slide => Math.Clamp((Progress - .25) / .75, 0, 1);
        public double ProductX => Stage switch
        {
            Step.Feeding => 40 + 168 * Progress,
            Step.Inspecting => 208,
            Step.ToGate => 208 + 102 * Progress,
            Step.Passing => 310 + 215 * Progress,
            Step.Deflecting => Progress < .25 ? 310 + 37 * Progress / .25 : 347 + 53 * Slide,
            Step.Holding or Step.Retracting => 400,
            _ => 310
        };
        public double ProductY => Stage switch
        {
            Step.Deflecting => 156 - 80 * Slide,
            Step.Holding or Step.Retracting => 76,
            _ => 156
        };
        public string StageText => Stage switch
        {
            Step.Feeding => "물체 이동 중", Step.Inspecting => "검사 중",
            Step.ToGate => "분류 위치로 이동", Step.Passing => "정상 · 직진 통과",
            Step.Deploying => "불량 · 분류판 회전", Step.Deflecting => "대각선 방향으로 분류",
            Step.Holding => "불량 분류 완료", Step.Retracting => "분류판 원위치 복귀",
            _ => "다음 물체 대기"
        };
        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            while (seconds > 0)
            {
                double amount = Math.Min(seconds, Duration(Stage) - _elapsed);
                _elapsed += amount; seconds -= amount;
                if (_elapsed + 1e-9 < Duration(Stage)) break;
                _elapsed = 0;
                if (Stage == Step.Passing) CompletedNormalCount++;
                if (Stage == Step.Holding) CompletedDefectCount++;
                Stage = Stage switch
                {
                    Step.Feeding => Step.Inspecting,
                    Step.Inspecting => Step.ToGate,
                    Step.ToGate => IsDefective ? Step.Deploying : Step.Passing,
                    Step.Passing => Step.Gap,
                    Step.Deploying => Step.Deflecting,
                    Step.Deflecting => Step.Holding,
                    Step.Holding => Step.Retracting,
                    Step.Retracting => Step.Gap,
                    _ => Step.Feeding
                };
                if (Stage == Step.Feeding) IsDefective = Flow switch
                {
                    VirtualProductFlow.NormalOnly => false,
                    VirtualProductFlow.DefectOnly => true,
                    _ => !IsDefective
                };
            }
        }
        private static double Smooth(double x) => x * x * (3 - 2 * x);
        private static double Duration(Step stage) => stage switch
        {
            Step.Feeding => 2.5, Step.Inspecting => .7, Step.ToGate => 1.2,
            Step.Passing => 2.2, Step.Deploying => .65, Step.Deflecting => 1.8,
            Step.Holding => .35, Step.Retracting => .65, _ => .55
        };
    }
}
