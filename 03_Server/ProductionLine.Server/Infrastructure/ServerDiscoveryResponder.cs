using System.Net;
using System.Net.Sockets;
using ProductionLine.Shared;

namespace ProductionLine.Server.Infrastructure;

// [서버 찾기 응답] 앱이 "서버 있어요?"(UDP 5051)를 보내면 "여기 있어요(포트·PC 이름·IPv4)"로 답함 (김하균 추가, 2026-10-21)
// 앱의 "서버 접속" 창이 이 답장으로 서버 IP를 자동으로 보여 줌. 포트를 못 열면 경고만 남기고 서버는 계속 동작
public sealed class ServerDiscoveryResponder(IConfiguration configuration, ILogger<ServerDiscoveryResponder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var httpPort = HttpPort(configuration);
        UdpClient udp;
        try { udp = new UdpClient(new IPEndPoint(IPAddress.Any, ServerDiscovery.DiscoveryPort)); }
        catch (SocketException ex)
        {
            logger.LogWarning("서버 찾기 응답을 켜지 못했습니다(UDP {Port}): {Error}. IP 직접 입력으로는 접속할 수 있습니다.", ServerDiscovery.DiscoveryPort, ex.Message);
            return;
        }

        using (udp)
        {
            LocalNetwork.IgnoreConnectionReset(udp);
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult request;
                try { request = await udp.ReceiveAsync(stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }
                if (!ServerDiscovery.IsRequest(request.Buffer)) continue;
                var reply = ServerDiscovery.BuildReply(httpPort);
                try { await udp.SendAsync(reply, reply.Length, request.RemoteEndPoint); }
                catch (SocketException) { }
            }
        }
    }

    // appsettings.json의 ServerUrl(예: http://0.0.0.0:5050)에서 포트 번호만 꺼냄
    public static int HttpPort(IConfiguration configuration) =>
        Uri.TryCreate(configuration["ServerUrl"] ?? "", UriKind.Absolute, out var uri) ? uri.Port : ServerDiscovery.DefaultHttpPort;
}
