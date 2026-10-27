using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ProductionLine.Shared;

// [서버 찾기] 같은 네트워크(공유기)에 켜진 서버를 자동으로 찾는 약속 (김하균 추가, 2026-10-21)
// - 앱이 UDP 5051번으로 "서버 있어요?"를 뿌림(브로드캐스트) → 서버가 "여기 있어요(포트, PC 이름, IPv4)"로 답함
// - 실제 데이터 통신은 지금처럼 HTTP(TCP 5050). 이 UDP는 주소를 찾을 때만 씀
public sealed record DiscoveredServer(string Address, int Port, string MachineName, bool IsThisPc)
{
    public string Url => $"http://{Address}:{Port}";
    public override string ToString() => $"{Address}:{Port}   ({MachineName}{(IsThisPc ? " · 이 PC" : "")})";
}

public static class ServerDiscovery
{
    public const int DiscoveryPort = 5051;
    public const int DefaultHttpPort = 5050;
    private const string Request = "PRODUCTIONLINE_DISCOVER_V1";
    private const string ReplyPrefix = "PRODUCTIONLINE_SERVER_V1";

    // 서버: 요청이 맞는지 확인
    public static bool IsRequest(byte[] data) => Encoding.UTF8.GetString(data) == Request;

    // 서버: 답장 만들기 "접두어|HTTP포트|PC이름|IPv4;IPv4"
    public static byte[] BuildReply(int httpPort) => Encoding.UTF8.GetBytes(
        $"{ReplyPrefix}|{httpPort}|{Environment.MachineName}|{string.Join(';', LocalNetwork.GetLanIPv4().Select(x => x.ToString()))}");

    // 앱: 네트워크에 서버를 찾는 요청을 뿌리고 wait 동안 온 답장을 모음
    public static async Task<IReadOnlyList<DiscoveredServer>> FindAsync(TimeSpan wait, CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        LocalNetwork.IgnoreConnectionReset(udp);
        var request = Encoding.UTF8.GetBytes(Request);
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        foreach (var broadcast in LocalNetwork.GetBroadcastAddresses()) targets.Add(broadcast);
        foreach (var target in targets)
        {
            try { await udp.SendAsync(request, request.Length, new IPEndPoint(target, DiscoveryPort)); }
            catch (SocketException) { } // 연결 안 된 네트워크 카드 등은 건너뜀
        }

        var found = new List<DiscoveredServer>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait);
        try
        {
            while (true)
            {
                UdpReceiveResult reply;
                try { reply = await udp.ReceiveAsync(timeout.Token); }
                catch (SocketException) { continue; }
                var server = ParseReply(reply);
                if (server != null) found.Add(server);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        // 한 서버가 네트워크 카드마다(공유기, VPN 등) 여러 번 답해도 한 줄로: 공유기 주소(192.168…)를 우선
        return found
            .GroupBy(x => (x.MachineName, x.Port))
            .Select(g => g.OrderBy(x => LocalNetwork.AddressRank(x.Address)).First())
            .OrderBy(x => LocalNetwork.AddressRank(x.Address)).ThenBy(x => x.MachineName)
            .ToList();
    }

    private static DiscoveredServer? ParseReply(UdpReceiveResult reply)
    {
        var parts = Encoding.UTF8.GetString(reply.Buffer).Split('|');
        if (parts.Length < 4 || parts[0] != ReplyPrefix || !int.TryParse(parts[1], out var port) || port is < 1 or > 65535) return null;
        var lanAddresses = parts[3].Split(';', StringSplitOptions.RemoveEmptyEntries);
        var source = reply.RemoteEndPoint.Address;
        if (source.IsIPv4MappedToIPv6) source = source.MapToIPv4();
        var thisPc = IPAddress.IsLoopback(source) || LocalNetwork.GetLanIPv4().Any(x => x.Equals(source));
        // 이 PC의 서버가 127.0.0.1로 답하면, 다른 PC에 알려 줄 수 있게 이 PC의 IPv4로 표시
        var address = IPAddress.IsLoopback(source) ? (lanAddresses.FirstOrDefault() ?? "127.0.0.1") : source.ToString();
        return new DiscoveredServer(address, port, parts[2], thisPc);
    }

    // 사용자가 입력한 "192.168.0.15", "192.168.0.15:5050", "http://..." 를 http://주소:포트 로 바꿈
    public static bool TryNormalizeAddress(string input, out string url)
    {
        url = "";
        var text = input.Trim();
        if (text.Length == 0) return false;
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || uri.Host.Length == 0) return false;
        var port = uri.IsDefaultPort && !input.Contains(":80") ? DefaultHttpPort : uri.Port;
        url = $"http://{uri.Host}:{port}";
        return true;
    }
}

// 이 PC의 네트워크 주소 도우미
public static class LocalNetwork
{
    // 다른 PC가 접속할 때 쓸 이 PC의 IPv4 주소들 (게이트웨이가 있는 실제 랜·와이파이 카드를 앞에)
    public static IReadOnlyList<IPAddress> GetLanIPv4() => Candidates()
        .OrderBy(x => AddressRank(x.Address.ToString()))
        .ThenByDescending(x => x.HasGateway)
        .ThenByDescending(x => x.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .Select(x => x.Address).Distinct().ToList();

    // 주소 우선순위: 공유기에서 흔한 사설 주소(192.168 → 10 → 172.16~31)를 앞에, VPN(Tailscale 등 100.64~127)은 맨 뒤
    public static int AddressRank(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return 9;
        var b = ip.GetAddressBytes();
        if (b[0] == 192 && b[1] == 168) return 0;
        if (b[0] == 10) return 1;
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return 2;
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return 5;
        if (b[0] == 127) return 8;
        return 3;
    }

    // 가장 알맞은 IPv4 하나 (없으면 null)
    public static IPAddress? GetBestLanIPv4() => GetLanIPv4().FirstOrDefault();

    public static IEnumerable<IPAddress> GetBroadcastAddresses() => Candidates()
        .Where(x => x.Mask != null)
        .Select(x =>
        {
            var ip = x.Address.GetAddressBytes();
            var mask = x.Mask!.GetAddressBytes();
            for (var i = 0; i < 4; i++) ip[i] = (byte)(ip[i] | ~mask[i]);
            return new IPAddress(ip);
        }).Distinct();

    private static IEnumerable<(IPAddress Address, IPAddress? Mask, bool HasGateway, NetworkInterfaceType Type)> Candidates()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var unicast in props.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address)) continue;
                if (unicast.Address.GetAddressBytes() is [169, 254, ..]) continue; // 자동 할당(연결 안 됨) 주소 제외
                yield return (unicast.Address, unicast.IPv4Mask, hasGateway, nic.NetworkInterfaceType);
            }
        }
    }

    // Windows에서 상대가 없을 때 UDP 받기가 오류로 끊기는 것을 막음 (SIO_UDP_CONNRESET 끄기)
    public static void IgnoreConnectionReset(UdpClient udp)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { udp.Client.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null); } catch (SocketException) { } catch (PlatformNotSupportedException) { }
    }
}
