using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// Uses existing worker/serial reports. Never opens COM/camera or issues control commands.
public sealed class DeviceDiagnosticsService(LineHardwareStore hardware, RemoteLineStore remote)
{
    public DeviceDiagnosticsSnapshot Capture(LineStatusDto line)
    {
        var now = DateTimeOffset.UtcNow;
        var devices = new List<DeviceDiagnostic>();
        void Add(string id, string name, string connection, string state, string detail, DateTimeOffset? at, int fresh = 5)
            => devices.Add(new(id, name, connection, state, detail, at, fresh));
        if (!line.IsReal)
        {
            Add("line", "가상 라인", "실장 장비 없음", "unavailable", "이 라인은 실제 USB·핀 상태를 수집하지 않습니다. 매뉴얼 상담은 가능합니다.", null);
            return new(line.Id, false, now, devices, "");
        }
        var worker = remote.Snapshot(line.Id).Worker;
        var online = worker != null && now >= worker.ReceivedAt && now - worker.ReceivedAt < TimeSpan.FromSeconds(6);
        var state = online ? worker!.State : null;
        var hw = hardware.Attach(line).Hardware;
        var hwFresh = hw is { Connected: true } && now >= hw.ReceivedAt && now - hw.ReceivedAt < TimeSpan.FromSeconds(5)
            && state?.ConveyorConnected != false;
        var hwState = hw == null ? "unknown" : "stale";
        var hwAt = hw?.ReceivedAt;
        Add("worker", "작업자 앱", "기존 서버", online ? "received" : "unknown",
            online ? "작업자 상태 보고를 수신했습니다." : "작업자 앱 보고가 없습니다. 앱 실행·로그인·네트워크를 확인하세요.", worker?.ReceivedAt, 6);
        Add("arduino", "아두이노 USB 통신", state?.Port ?? "COM 포트 미확인", hwFresh ? "received" : hwState,
            hwFresh ? "펌웨어 장비 상태 응답 수신. 개별 핀의 장착·단선 여부까지 증명하지 않습니다."
            : state?.ConveyorConnected == true ? "앱은 연결됨으로 보고했지만 최신 펌웨어 상태 응답이 없습니다. USB·펌웨어·통신 오류를 확인하세요."
            : "최신 아두이노 상태 없음. 작업자 앱에서 COM 포트를 선택해 벨트 연결을 누르세요. 임의 구동은 필요 없습니다.", hwAt);
        Add("temperature", "온도 DHT11", "Mega D30", !hwFresh ? hwState : hw!.TemperatureCelsius.HasValue ? "reading" : "read_error",
            !hwFresh ? "최신 측정값 없음. 먼저 USB 통신을 확인하세요."
            : hw!.TemperatureCelsius is double t ? $"{t:0.0} °C 수신. 공정 허용 범위 미등록으로 고온·저온·정상을 확정하지 않습니다."
            : "보드 통신은 있지만 온도 읽기가 실패했습니다. DHT11 설정·D30 배선·전원·읽기 주기를 확인하세요. 센서 고장으로 단정하지 않습니다.", hwAt);
        Add("humidity", "습도 DHT11", "Mega D30", !hwFresh ? hwState : hw!.HumidityPercent.HasValue ? "reading" : "read_error",
            !hwFresh ? "최신 측정값 없음. 먼저 USB 통신을 확인하세요."
            : hw!.HumidityPercent is double h ? $"{h:0.0} %RH 수신. 공정 허용 범위 미등록으로 정상·고습을 확정하지 않습니다."
            : "보드 통신은 있지만 습도 읽기가 실패했습니다. 배선·전원·센서 설정을 안전한 정지 상태에서 담당자가 확인하세요.", hwAt);
        // 펌웨어 V7: MQ-9 AO를 A0에서 0~1023 원시값으로 보냄(GasAnalogValue). 예전 DO(GasDetected) 방식도 그대로 읽음. (김하균 2026-10-23)
        if (hwFresh && hw!.GasAnalogValue is int gasRaw)
            Add("gas", "가스 MQ-9", "Mega A0 (AO)", hw.GasWarmingUp ? "unknown" : "reading",
                hw.GasWarmingUp ? $"MQ-9 예열 중(전원 후 60초) · A0 원시값 {gasRaw}/1023은 참고하지 않습니다."
                : $"MQ-9 A0 원시값 {gasRaw}/1023 수신. ppm 농도가 아니며 경보 기준이 정해지지 않아 정상·위험을 판정하지 않습니다.", hwAt);
        else
        Add("gas", "가스 MQ-9", "Mega A0 (AO)", !hwFresh ? hwState : hw!.GasDetected == true ? "alert" : hw.GasDetected == false ? "reported" : "unknown",
            !hwFresh ? "최신 가스 신호 없음. 미수신을 미감지로 처리하지 않습니다."
            : hw!.GasDetected == true ? "가스 감지 신호 수신. 실제 가스 종류·농도·누출 위치는 확정할 수 없습니다. 현장 비상 절차를 우선하세요."
            : hw.GasDetected == false ? "DO 미감지 신호 보고. 모듈 연결 정상·가스 안전·ppm 농도를 확인한 결과는 아닙니다."
            : "가스 상태 미확인. 현재 펌웨어의 초기 60초 준비 구간 또는 신호 미확인입니다. 실제 모델·준비 기준을 확인하세요.", hwAt);
        Add("emergency", "비상정지 입력", "Mega D12 · NC", !hwFresh ? hwState : hw!.EmergencyStopped ? "alert" : "reported",
            !hwFresh ? "비상정지 상태 미확인." : hw!.EmergencyStopped ? "펌웨어 비상정지 잠금이 보고됐습니다. 회로 우회·AI에 의한 해제는 하지 않습니다."
            : "비상정지 잠금 해제 상태 보고. 현장 안전이나 재가동 승인 의미는 아닙니다.", hwAt);
        Add("conveyor", "컨베이어 구동", "Mega PUL D5 / DIR D6", hwFresh ? "reported" : hwState,
            !hwFresh ? "최신 구동 상태 없음." : hw!.BeltRunning ? "펌웨어 구동 출력 켜짐. 회전·이송 실측 센서가 없어 실제 벨트 움직임은 미확인입니다."
            : "펌웨어 구동 출력 정지 상태. 정지 명령·비상정지 등 원인을 구분해야 하며 모터 고장으로 단정하지 않습니다.", hwAt);
        Add("sorter", "분류 서보", "Mega D11", hwFresh ? "reported" : hwState,
            !hwFresh ? "최신 분류 상태 없음." : $"명령 각도 {hw!.SorterAngle}° · {(hw.Sorting ? "분류 중/예약 있음" : "분류 대기")}. " +
                (state == null ? "자동 분류 설정 미확인. " : $"자동 분류 {(state.AutoSorting ? "켜짐" : "꺼짐")}. ") +
                "위치 피드백이 없어 실제 각도·서보 장착·회전은 미확인입니다.", hwAt);
        Add("light", "LED 검사 조명", "Mega D7 → DIN", state?.LightOn.HasValue == true && state.ConveyorConnected ? "reported" : "unknown",
            state?.LightOn.HasValue == true && state.ConveyorConnected ? $"앱의 마지막 조명 명령 상태: {(state.LightOn.Value ? "LED_ON" : "LED_OFF")}. 실제 점등·전원 연결을 측정한 결과는 아닙니다."
            : "조명 명령 상태 미확인. 통신 후 LED_STATUS를 확인하고 실제 점등을 별도로 확인하세요.", online ? worker!.ReceivedAt : null, 6);
        var frameFresh = state?.CameraConnected == true && state.LiveCapturedAt is { } frameAt &&
            now >= frameAt && now - frameAt < TimeSpan.FromSeconds(3);
        Add("camera", "카메라 영상 수신", state == null ? "USB 카메라 번호 미확인" : $"USB 카메라 #{state.CameraIndex}", frameFresh ? "received" : "unknown",
            frameFresh ? "최근 영상 프레임 수신. 제품 검출·품질 판정 성공과는 별개입니다."
            : "최근 영상 수신 미확인. 실제 USB 분리인지, 장치 점유·권한·영상 오류인지는 앱 기록과 함께 확인하세요.", state?.LiveCapturedAt, 3);
        Add("yolo", "YOLO 검사", "작업자 PC", online ? "reported" : "unknown",
            state == null ? "모델·검사 보고 없음." : $"선택 모델: {state.Model} · 검사 {(state.Inspecting ? "실행 중" : "중지")}. 모델 준비·검출 오류는 아래 앱 기록을 확인하세요. 선택만으로 로드 성공·정확도를 보장하지 않습니다.", online ? worker!.ReceivedAt : null, 6);
        var appStatus = state?.Status ?? "";
        return new(line.Id, true, now, devices, appStatus[..Math.Min(appStatus.Length, 2000)]);
    }
}
