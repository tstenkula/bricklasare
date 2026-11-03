namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Text;

    // Offline policy/protocol tests plus a Windows socket-option check. No connection is opened.
    internal static class ReaderNetworkTests
    {
        private static int count;
        private static void Check(bool ok, string message)
        {
            count++; if (!ok) throw new InvalidOperationException("USB transport test: " + message);
        }
        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            catch (IOException) { rejected = true; }
            Check(rejected, message);
        }
        private static ReaderAdapterEvidence Adapter(string address, string mask)
        {
            return new ReaderAdapterEvidence { Id = "12345678-1234-1234-1234-123456789abc", Index = 17, Up = true,
                UsbId = @"USB\VID_076B&PID_5427&MI_01\synthetic", Address = IPAddress.Parse(address), Mask = IPAddress.Parse(mask) };
        }
        private static string Response(string text)
        {
            using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return ReaderNetwork.ReadResponse(input, delegate { return false; });
        }
        public static int Run()
        {
            count = 0;
            SocketInterfaceOption();
            foreach (string id in new[] { @"USB\VID_076B&PID_5427\synthetic", @"usb\vid_076b&pid_5428&mi_02\synthetic" })
                Check(ReaderNetwork.IsReaderUsbId(id), "Exact supported USB hardware identity");
            foreach (string id in new[] { "OMNIKEY 5427", @"ROOT\VID_076B&PID_5427\synthetic", @"USB\VID_076B&PID_54270\synthetic", @"USB\VID_076C&PID_5427\synthetic", @"USB\VID_076B&PID_5127\synthetic", @"USB\VID_076B&PID_5427" })
                Check(!ReaderNetwork.IsReaderUsbId(id), "Names and incorrect device IDs are not trusted");
            IPAddress remote = IPAddress.Parse("192.168.63.99");
            ReaderAdapterEvidence good = Adapter("192.168.63.1", "255.255.255.0");
            Check(ReaderNetwork.Select(remote, new[] { good }) == good, "Direct USB adapter selected");
            ReaderAdapterEvidence lan = Adapter("192.168.63.2", "255.255.255.0"); lan.Id = "22345678-1234-1234-1234-123456789abc"; lan.Index = 12; lan.UsbId = @"PCI\VEN_0000\synthetic";
            Check(ReaderNetwork.Select(remote, new[] { good, lan }) == good, "Overlapping LAN/VPN subnet does not select LAN");
            Reject(delegate { ReaderNetwork.Select(remote, new[] { lan }); }, "No LAN fallback without reader");
            ReaderAdapterEvidence second = Adapter("192.168.64.1", "255.255.255.0"); second.Id = "32345678-1234-1234-1234-123456789abc"; second.Index = 18;
            Reject(delegate { ReaderNetwork.Select(remote, new[] { good, second }); }, "Multiple USB readers rejected even on different subnets");
            good.Up = false; Reject(delegate { ReaderNetwork.Select(remote, new[] { good }); }, "Disconnected interface rejected"); good.Up = true;
            good.Index = 0; Reject(delegate { ReaderNetwork.Select(remote, new[] { good }); }, "Unspecified interface rejected");
            good.Index = 0x01000000; Reject(delegate { ReaderNetwork.Select(remote, new[] { good }); }, "Interface index cannot become an IPv4 option address"); good.Index = 17;
            good.Id = "not-an-interface-guid"; Reject(delegate { ReaderNetwork.Select(remote, new[] { good }); }, "Invalid adapter identity rejected"); good = Adapter("192.168.63.1", "255.255.255.0");
            Reject(delegate { ReaderNetwork.Select(IPAddress.Parse("192.168.64.99"), new[] { good }); }, "Gateway route rejected");
            Check(ReaderNetwork.Select(IPAddress.Parse("10.2.3.99"), new[] { Adapter("10.2.3.1", "255.255.255.0") }).Index == 17, "Custom private subnet works on proven USB adapter");
            foreach (string address in new[] { "127.0.0.1", "8.8.8.8", "192.168.63.0", "192.168.63.255", "192.168.63.1", "::1" })
                Reject(delegate { ReaderNetwork.Select(IPAddress.Parse(address), new[] { good }); }, "Loopback/public/network/broadcast/self/IPv6 target rejected");
            foreach (string mask in new[] { "0.0.0.0", "255.0.255.0", "255.255.255.255", "255.255.255.254" })
                Check(!ReaderNetwork.IsDirectPeer(good.Address, IPAddress.Parse(mask), remote), "Invalid or unusable subnet mask rejected");
            Check(Response("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\n9000") == "9000", "Content length response");
            Check(Response("HTTP/1.0 200 OK\r\n\r\n9000") == "9000", "Connection close response");
            Check(Response("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n2\r\n90\r\n2;part=test\r\n00\r\n0\r\nX-Info: test\r\n\r\n") == "9000", "Chunked response with bounded metadata");
            foreach (string text in new[] {
                "", "HTTP/1.1 200 OK\r", "HTTP/1.1 200 OK\n\n9000", "HTTP/1.1 302 Found\r\nLocation: http://example.invalid\r\n\r\n",
                "HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\n90", "HTTP/1.1 200 OK\r\nContent-Length: 16385\r\n\r\n",
                "HTTP/1.1 200 OK\r\nContent-Length: -1\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 9999999999\r\n\r\n",
                "HTTP/1.1 200 OK\r\nContent-Length: 4\r\nContent-Length: 4\r\n\r\n9000",
                "HTTP/1.1 200 OK\r\nContent-Length: 4\r\nTransfer-Encoding: chunked\r\n\r\n",
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: gzip\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Encoding: gzip\r\n\r\n",
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n4001\r\n",
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n2\r\n9",
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n2\r\n90XX",
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n",
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\ninvalid-trailer\r\n\r\n",
                "HTTP/1.1 200 OK\r\nInvalid header\r\n\r\n", "HTTP/1.1 200 OK\r\n Bad: folded\r\n\r\n"
            }) Reject(delegate { Response(text); }, "Malformed HTTP rejected");
            Reject(delegate { Response("HTTP/1.1 200 OK\r\nLarge: " + new string('x', 8192) + "\r\n\r\n"); }, "Header size bounded");
            Reject(delegate { Response("HTTP/1.0 200 OK\r\n\r\n" + new string('x', 16385)); }, "Unframed body size bounded");
            Reject(delegate {
                using (MemoryStream input = new MemoryStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\n9000")))
                    ReaderNetwork.ReadResponse(input, delegate { return true; });
            }, "Total response deadline enforced before reading");
            Reject(delegate { ReaderNetwork.ReadResponse(new TimeoutStream(), delegate { return false; }); }, "Transport timeout fails closed");
            Reject(delegate {
                byte[] invalid = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1\r\n\r\n?"); invalid[invalid.Length - 1] = 255;
                using (MemoryStream input = new MemoryStream(invalid)) ReaderNetwork.ReadResponse(input, delegate { return false; });
            }, "Invalid UTF-8 is rejected");
            Random random = new Random(5427);
            bool bounded = true;
            for (int i = 0; i < 1000; i++)
            {
                byte[] bytes = new byte[random.Next(0, 512)]; random.NextBytes(bytes);
                try { using (MemoryStream input = new MemoryStream(bytes)) ReaderNetwork.ReadResponse(input, delegate { return false; }); }
                catch (InvalidDataException) { } // Malformed memory input must fail as protocol data.
                catch { bounded = false; break; } // Preserve failures for every unexpected exception type.
            }
            Check(bounded, "One thousand malformed byte sequences fail safely");
            return count;
        }
        private static void SocketInterfaceOption()
        {
            // Exercise Windows Winsock ABI/byte order without binding, connecting, or sending traffic.
            int index = NetworkInterface.LoopbackInterfaceIndex;
            Check(index > 0 && index <= 0x00FFFFFF, "Windows exposes a usable loopback interface index");
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(index));
                Check((int)socket.GetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31) == index,
                    "Windows IP_UNICAST_IF setter uses network order and getter returns host order");
                Check(!socket.Connected && socket.LocalEndPoint == null,
                    "Socket option verification opens no network connection or local endpoint");
            }
        }
        private sealed class TimeoutStream : MemoryStream
        {
            public override int ReadByte() { throw new IOException("Synthetic timeout"); }
        }
    }
}
