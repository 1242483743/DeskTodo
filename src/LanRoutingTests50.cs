using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DesktopTodo
{
    internal static partial class Tests
    {
        private static LanRoute Route50(string address, string mask, int index)
        { return LanRouting.CreateRoute(IPAddress.Parse(address), IPAddress.Parse(mask), index); }
        private static void CheckLanRouting50()
        {
            LanRoute wifi = Route50("192.168.5.20", "255.255.255.0", 10);
            Check(wifi != null && wifi.Broadcast.ToString() == "192.168.5.255" && wifi.PrefixLength == 24,
                "TUN 兼容：根据网卡掩码计算子网广播，不依赖默认广播路由");
            Check(wifi.Contains(IPAddress.Parse("192.168.5.21")) && !wifi.Contains(IPAddress.Parse("192.168.6.21")) &&
                !wifi.Contains(IPAddress.Parse("192.168.5.255")), "发现回复须属于该网卡子网，不接受跨网段或广播源");
            Check(Route50("10.12.7.20", "255.255.254.0", 11).Broadcast.ToString() == "10.12.7.255" &&
                Route50("172.16.5.20", "255.255.0.0", 12).Broadcast.ToString() == "172.16.255.255",
                "支持非 /24 网段，不硬编码广播最后一个字节");
            Check(new[] { "198.18.0.1", "198.19.255.1", "169.254.1.2", "127.0.0.1", "0.0.0.0", "224.0.0.1", "255.255.255.255", "::1" }
                .All(s => !LanRouting.IsLanAddress(IPAddress.Parse(s))), "过滤 TUN fake-IP、未连接地址、回环、组播及 IPv6");
            Check(Route50("192.168.5.20", "255.0.255.0", 10) == null && Route50("192.168.5.20", "0.0.0.0", 10) == null &&
                Route50("192.168.5.20", "255.255.255.255", 10) == null && Route50("192.168.5.20", "255.255.255.254", 10) == null &&
                Route50("192.168.5.20", "255.255.255.0", 0) == null, "拒绝非连续掩码、默认路由、点对点网段及无效接口索引");
            LanRoute broad = Route50("192.168.47.1", "255.255.0.0", 20);
            Check(LanRouting.FindRoute(IPAddress.Parse("192.168.5.21"), new[] { broad, wifi }) == wifi &&
                LanRouting.FindRoute(IPAddress.Parse("8.8.8.8"), new[] { wifi }) == null, "连接采用最长匹配网段，不将公网地址绑定到任意 LAN 网卡");
            LanRoute other = Route50("192.168.5.22", "255.255.255.0", 30);
            Check(LanRouting.FindRoute(IPAddress.Parse("192.168.5.21"), new[] { other, wifi }, "192.168.5.20") == wifi,
                "同网段多网卡时优先采用实际发现回复所用的本地网卡");
            Check(LanRouting.FindRoute(IPAddress.Loopback, new[] { wifi }).LocalAddress.Equals(IPAddress.Loopback), "回环通信不经过 TUN");
            foreach (LanRoute route in LanRouting.GetRoutes())
                using (UdpClient client = LanRouting.CreateDiscoveryClient(route))
                    Check(((IPEndPoint)client.Client.LocalEndPoint).Address.Equals(route.LocalAddress) &&
                        (int)client.Client.GetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31) == route.InterfaceIndex,
                        "真实网卡 UDP 本地地址和 Windows 出站接口绑定正确：" + route.LocalAddress);

            // Only loopback fixtures: no QA data or discovery broadcast leaves this machine.
            using (UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (ManualResetEvent stop = new ManualResetEvent(false))
            {
                server.Client.ReceiveTimeout = 100;
                int received = 0; Exception failure = null;
                int port = ((IPEndPoint)server.Client.LocalEndPoint).Port;
                Thread responder = new Thread(delegate()
                {
                    try
                    {
                        while (!stop.WaitOne(0))
                        {
                            IPEndPoint source = new IPEndPoint(IPAddress.Any, 0); byte[] query;
                            try { query = server.Receive(ref source); } catch (SocketException) { continue; }
                            if (Encoding.UTF8.GetString(query) != LanService.DiscoveryMessage) continue;
                            if (Interlocked.Increment(ref received) == 1) continue;
                            foreach (string payload in new[] { "bad-json", LanJson.Write(new LanPeer { InstanceId = "bad-port", Port = 70000 }),
                                LanJson.Write(new LanPeer { InstanceId = "route-test", DisplayName = "隔离测试", Port = 12345 }) })
                            { byte[] bytes = Encoding.UTF8.GetBytes(payload); server.Send(bytes, bytes.Length, source); }
                        }
                    }
                    catch (Exception e) { failure = e; }
                }) { IsBackground = true };
                responder.Start();
                try
                {
                    var peers = LanRouting.Discover(650, new[] { Route50("203.0.113.231", "255.255.255.0", 1000000), LanRoute.Loopback() }, port);
                    Check(peers.Count == 1 && peers[0].InstanceId == "route-test" && peers[0].Address == "127.0.0.1" && peers[0].LocalAddress == "127.0.0.1",
                        "真实 UDP：坏网卡不影响其他网卡，忽略非法响应并保留发现路径");
                    Check(received >= 2 && received <= 3 && failure == null, "真实 UDP：首包丢失后自动重试，同身份回复去重");
                }
                finally { stop.Set(); responder.Join(1500); }
            }
        }
    }
}
