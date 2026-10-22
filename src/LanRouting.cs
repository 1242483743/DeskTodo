using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DesktopTodo
{
    // Per-socket routing only: never changes proxy settings, system routes or firewall rules.
    internal sealed class LanRoute
    {
        internal IPAddress LocalAddress, Mask, Broadcast;
        internal int InterfaceIndex, PrefixLength;
        internal bool Contains(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            if (IPAddress.IsLoopback(LocalAddress)) return IPAddress.IsLoopback(address);
            byte[] value = address.GetAddressBytes(), local = LocalAddress.GetAddressBytes(), mask = Mask.GetAddressBytes();
            for (int i = 0; i < 4; i++) if ((value[i] & mask[i]) != (local[i] & mask[i])) return false;
            return LanRouting.IsLanAddress(address) && !address.Equals(Broadcast);
        }
        internal static LanRoute Loopback()
        { return new LanRoute { LocalAddress = IPAddress.Loopback, Mask = IPAddress.Parse("255.0.0.0"), Broadcast = IPAddress.Loopback, PrefixLength = 8 }; }
    }

    internal static class LanRouting
    {
        // Windows IP_UNICAST_IF: set interface index in network order; get returns host order.
        // https://learn.microsoft.com/windows/win32/winsock/ipproto-ip-socket-options
        internal static void PinInterface(Socket socket, int index)
        { socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(index)); }

        internal static bool IsLanAddress(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = address.GetAddressBytes();
            return b[0] != 0 && b[0] != 127 && b[0] < 224 &&
                !(b[0] == 169 && b[1] == 254) && !(b[0] == 198 && (b[1] == 18 || b[1] == 19));
        }

        internal static LanRoute CreateRoute(IPAddress address, IPAddress mask, int index)
        {
            if (!IsLanAddress(address) || mask == null || mask.AddressFamily != AddressFamily.InterNetwork || index < 1) return null;
            byte[] a = address.GetAddressBytes(), m = mask.GetAddressBytes(), broadcast = new byte[4];
            int prefix = 0; bool hostBit = false;
            for (int i = 0; i < 4; i++)
            {
                for (int bit = 7; bit >= 0; bit--)
                {
                    if ((m[i] & (1 << bit)) == 0) hostBit = true;
                    else { if (hostBit) return null; prefix++; }
                }
                broadcast[i] = (byte)(a[i] | (byte)~m[i]);
            }
            if (prefix == 0 || prefix > 30) return null;
            return new LanRoute { LocalAddress = address, Mask = mask, Broadcast = new IPAddress(broadcast), InterfaceIndex = index, PrefixLength = prefix };
        }

        internal static List<LanRoute> GetRoutes()
        {
            List<LanRoute> routes = new List<LanRoute>();
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel || adapter.NetworkInterfaceType == NetworkInterfaceType.Ppp) continue;
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    IPv4InterfaceProperties ipv4 = properties.GetIPv4Properties();
                    if (ipv4 == null) continue;
                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                            address.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Duplicate ||
                            address.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Tentative) continue;
                        LanRoute route = CreateRoute(address.Address, address.IPv4Mask, ipv4.Index);
                        if (route != null && !routes.Any(r => r.LocalAddress.Equals(route.LocalAddress))) routes.Add(route);
                    }
                }
                catch (NetworkInformationException) { } // An adapter may disappear during a scan.
                catch (SocketException) { }
            }
            return routes;
        }

        internal static LanRoute FindRoute(IPAddress destination, IEnumerable<LanRoute> routes, string preferredLocal = null)
        {
            if (destination != null && IPAddress.IsLoopback(destination)) return LanRoute.Loopback();
            return routes.Where(r => r.Contains(destination)).OrderByDescending(r => r.PrefixLength)
                .ThenByDescending(r => r.LocalAddress.ToString() == preferredLocal).FirstOrDefault();
        }

        internal static UdpClient CreateDiscoveryClient(LanRoute route)
        {
            UdpClient client = new UdpClient(new IPEndPoint(route.LocalAddress, 0));
            try
            {
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = 80;
                PinInterface(client.Client, route.InterfaceIndex);
                return client;
            }
            catch { client.Close(); throw; }
        }

        internal static List<LanPeer> Discover(int waitMilliseconds, IEnumerable<LanRoute> routes, int port)
        {
            Dictionary<string, LanPeer> result = new Dictionary<string, LanPeer>();
            List<KeyValuePair<LanRoute, UdpClient>> clients = new List<KeyValuePair<LanRoute, UdpClient>>();
            try
            {
                foreach (LanRoute route in routes)
                {
                    try { clients.Add(new KeyValuePair<LanRoute, UdpClient>(route, CreateDiscoveryClient(route))); }
                    catch (SocketException) { } // Failure of one interface must not cancel the others.
                }
                if (clients.Count == 0) throw new SocketException((int)SocketError.NetworkUnreachable);
                byte[] query = Encoding.UTF8.GetBytes(LanService.DiscoveryMessage);
                int duration = Math.Max(300, Math.Min(5000, waitMilliseconds));
                Stopwatch timer = Stopwatch.StartNew();
                int rounds = 0;
                while (timer.ElapsedMilliseconds < duration)
                {
                    if (rounds < 3 && timer.ElapsedMilliseconds >= rounds * duration / 3)
                    {
                        foreach (KeyValuePair<LanRoute, UdpClient> channel in clients)
                        {
                            try { channel.Value.Send(query, query.Length, new IPEndPoint(channel.Key.Broadcast, port)); } catch (SocketException) { }
                            if (!IPAddress.IsLoopback(channel.Key.LocalAddress))
                                try { channel.Value.Send(query, query.Length, new IPEndPoint(IPAddress.Broadcast, port)); } catch (SocketException) { }
                        }
                        rounds++;
                    }
                    foreach (KeyValuePair<LanRoute, UdpClient> channel in clients)
                    {
                        for (int n = 0; n < 32 && timer.ElapsedMilliseconds < duration; n++)
                        {
                            try
                            {
                                if (!channel.Value.Client.Poll(0, SelectMode.SelectRead)) break;
                                IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                                byte[] bytes = channel.Value.Receive(ref source);
                                if (bytes.Length > 8192 || !channel.Key.Contains(source.Address)) continue;
                                LanPeer peer = LanJson.Read<LanPeer>(Encoding.UTF8.GetString(bytes));
                                if (peer == null || String.IsNullOrWhiteSpace(peer.InstanceId) || peer.Port < 1 || peer.Port > 65535) continue;
                                peer.Address = source.Address.ToString(); peer.LocalAddress = channel.Key.LocalAddress.ToString();
                                LanPeer existing;
                                if (!result.TryGetValue(peer.InstanceId, out existing) || !IPAddress.IsLoopback(source.Address)) result[peer.InstanceId] = peer;
                            }
                            catch (SocketException) { break; }
                            catch (System.Runtime.Serialization.SerializationException) { }
                            catch (System.Xml.XmlException) { }
                            catch (ArgumentException) { }
                        }
                    }
                    Thread.Sleep(15);
                }
            }
            finally { foreach (KeyValuePair<LanRoute, UdpClient> channel in clients) channel.Value.Close(); }
            return result.Values.OrderBy(p => p.DisplayName).ThenBy(p => p.MachineName).ToList();
        }
    }
}
