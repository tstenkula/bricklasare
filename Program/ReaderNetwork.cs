namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Management;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;

    internal sealed class ReaderNetworkDisconnectedException : IOException
    {
        internal ReaderNetworkDisconnectedException(string message) : base(message) { }
        internal ReaderNetworkDisconnectedException(string message, Exception inner) : base(message, inner) { }
    }
    internal sealed class ReaderAdapterEvidence
    {
        internal string Id;
        internal string UsbId;
        internal int Index;
        internal bool Up;
        internal IPAddress Address;
        internal IPAddress Mask;
    }
    internal static class ReaderNetwork
    {
        internal const string ConnectionHelp = "Läsarens USB-nätverksanslutning kunde inte verifieras. Anslut en OMNIKEY 5427 CK, kontrollera EEM-drivrutinen och läsarens IP-adress och försök igen. Anslutningen skickas inte via Wi-Fi, vanligt nätverk eller VPN.";
        private const int MaxBody = 16384, MaxHeaders = 8192;
        private static readonly object IdentityGate = new object();
        private static readonly Dictionary<string, string> UsbIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string identityFingerprint;
        private static long identityChecked;
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);
        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, int length, uint flags);
        internal static bool IsReaderUsbId(string id)
        {
            // Hardware instance IDs, never editable adapter names or descriptions.
            return id != null && id.Length <= 400 && Regex.IsMatch(id,
                @"\AUSB\\VID_076B&PID_(?:5427|5428)(?:&[A-Z0-9_]+)*\\[^\s\\]+\z", RegexOptions.IgnoreCase);
        }
        private static string FindUsbParent(string id)
        {
            uint node;
            if (String.IsNullOrEmpty(id) || id.Length > 400 || CM_Locate_DevNodeW(out node, id, 0) != 0) return null;
            for (int depth = 0; depth < 12; depth++)
            {
                StringBuilder value = new StringBuilder(401);
                if (CM_Get_Device_IDW(node, value, value.Capacity, 0) != 0) return null;
                if (IsReaderUsbId(value.ToString())) return value.ToString();
                uint parent;
                if (CM_Get_Parent(out parent, node, 0) != 0) return null;
                node = parent;
            }
            return null;
        }
        internal static bool IsPrivate(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }
        private static uint Number(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }
        internal static bool IsDirectPeer(IPAddress local, IPAddress mask, IPAddress remote)
        {
            if (!IsPrivate(local) || !IsPrivate(remote) || mask == null || mask.AddressFamily != AddressFamily.InterNetwork || local.Equals(remote)) return false;
            uint m = Number(mask), host = ~m, source = Number(local), destination = Number(remote);
            // Require a contiguous subnet mask with usable host addresses, never a default route.
            if (m == 0 || host < 3 || (host & (host + 1)) != 0) return false;
            return (source & m) == (destination & m) && (destination & host) != 0 &&
                (destination & host) != host && (source & host) != 0 && (source & host) != host;
        }
        internal static ReaderAdapterEvidence Select(IPAddress remote, IEnumerable<ReaderAdapterEvidence> evidence)
        {
            if (!IsPrivate(remote)) throw new IOException(ConnectionHelp);
            List<ReaderAdapterEvidence> usb = evidence.Where(delegate(ReaderAdapterEvidence e) {
                Guid id;
                return e != null && e.Up && e.Index > 0 && e.Index <= 0x00FFFFFF && Guid.TryParse(e.Id, out id) && id != Guid.Empty && IsReaderUsbId(e.UsbId);
            }).ToList();
            // Ambiguity fails even when only one reader's subnet happens to match.
            if (usb.Count == 0) throw new ReaderNetworkDisconnectedException(ConnectionHelp);
            if (usb.Select(delegate(ReaderAdapterEvidence e) { return e.Id.ToUpperInvariant(); }).Distinct().Count() != 1)
                throw new IOException(ConnectionHelp);
            List<ReaderAdapterEvidence> peers = usb.Where(delegate(ReaderAdapterEvidence e) { return IsDirectPeer(e.Address, e.Mask, remote); }).ToList();
            if (peers.Count == 0 || peers.Select(delegate(ReaderAdapterEvidence e) { return e.Index; }).Distinct().Count() != 1)
                throw new IOException(ConnectionHelp);
            return peers.OrderBy(delegate(ReaderAdapterEvidence e) { return Number(e.Address); }).First();
        }
        private static ReaderAdapterEvidence Resolve(IPAddress remote)
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            string fingerprint = String.Join("|", interfaces.Select(delegate(NetworkInterface n) {
                return n.Id + ":" + n.OperationalStatus.ToString();
            }).OrderBy(delegate(string s) { return s; }));
            Dictionary<string, string> identities;
            lock (IdentityGate)
            {
                long now = Stopwatch.GetTimestamp();
                if (identityFingerprint != fingerprint || now - identityChecked > 5 * Stopwatch.Frequency)
                {
                    UsbIdentities.Clear(); identityFingerprint = null;
                    EnumerationOptions options = new EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(3) };
                    using (ManagementObjectSearcher search = new ManagementObjectSearcher("root\\cimv2",
                        "SELECT GUID, InterfaceIndex, PNPDeviceID, NetEnabled, ConfigManagerErrorCode FROM Win32_NetworkAdapter", options))
                    using (ManagementObjectCollection adapters = search.Get())
                    {
                        foreach (ManagementObject adapter in adapters)
                        using (adapter)
                        {
                            if (!(adapter["NetEnabled"] is bool) || !(bool)adapter["NetEnabled"] ||
                                adapter["ConfigManagerErrorCode"] == null || Convert.ToUInt32(adapter["ConfigManagerErrorCode"], CultureInfo.InvariantCulture) != 0) continue;
                            string id = adapter["GUID"] as string, pnp = adapter["PNPDeviceID"] as string;
                            Guid parsed;
                            if (!Guid.TryParse(id, out parsed) || adapter["InterfaceIndex"] == null) continue;
                            string usb = FindUsbParent(pnp);
                            if (usb != null) UsbIdentities[parsed.ToString("D") + ":" + Convert.ToInt32(adapter["InterfaceIndex"], CultureInfo.InvariantCulture)] = usb;
                        }
                    }
                    identityFingerprint = fingerprint; identityChecked = now;
                }
                identities = new Dictionary<string, string>(UsbIdentities, StringComparer.OrdinalIgnoreCase);
            }
            List<ReaderAdapterEvidence> evidence = new List<ReaderAdapterEvidence>();
            foreach (NetworkInterface adapter in interfaces)
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || !adapter.Supports(NetworkInterfaceComponent.IPv4)) continue;
                Guid id;
                if (!Guid.TryParse(adapter.Id, out id)) continue;
                IPInterfaceProperties properties = adapter.GetIPProperties();
                int index = properties.GetIPv4Properties().Index;
                string usb;
                if (!identities.TryGetValue(id.ToString("D") + ":" + index, out usb)) continue;
                // Include the adapter even without a usable IPv4 address so multiple readers stay ambiguous.
                evidence.Add(new ReaderAdapterEvidence { Id = id.ToString("D"), Index = index, Up = true, UsbId = usb });
                foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        evidence.Add(new ReaderAdapterEvidence { Id = id.ToString("D"), Index = index, Up = true,
                            UsbId = usb, Address = address.Address, Mask = address.IPv4Mask });
            }
            return Select(remote, evidence);
        }
        internal static string Send(IPAddress remote, string command)
        {
            if (command == null || command.Length == 0 || command.Length > 1024 || !Regex.IsMatch(command, @"\A[0-9A-Fa-f]+\z"))
                throw new InvalidDataException("Ogiltigt läsarkommando.");
            ReaderAdapterEvidence adapter;
            try { adapter = Resolve(remote); }
            catch (ReaderNetworkDisconnectedException) { throw; }
            catch (Exception ex) { throw new IOException(ConnectionHelp, ex); }
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    socket.SendTimeout = 3000; socket.ReceiveTimeout = 3000; socket.NoDelay = true;
                    // IP_UNICAST_IF takes the current interface index in NETWORK byte order.
                    // Pin the outgoing interface as well as the local address; never use a VPN/LAN fallback.
                    socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(adapter.Index));
                    if ((int)socket.GetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31) != adapter.Index)
                        throw new IOException(ConnectionHelp);
                    socket.Bind(new IPEndPoint(adapter.Address, 0));
                    IAsyncResult connecting = socket.BeginConnect(new IPEndPoint(remote, 80), null, null);
                    using (System.Threading.WaitHandle wait = connecting.AsyncWaitHandle)
                    {
                        if (!wait.WaitOne(3000)) throw new ReaderNetworkDisconnectedException("Läsaren svarade inte via USB. Kontrollera anslutningen och försök igen.");
                        socket.EndConnect(connecting);
                    }
                    using (NetworkStream network = new NetworkStream(socket, false))
                    using (BufferedStream stream = new BufferedStream(network, 4096))
                    {
                        byte[] request = Encoding.ASCII.GetBytes("POST /cjax HTTP/1.1\r\nHost: " + remote.ToString() +
                            "\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: " + command.Length.ToString(CultureInfo.InvariantCulture) +
                            "\r\nConnection: close\r\n\r\n" + command);
                        stream.Write(request, 0, request.Length); stream.Flush();
                        return ReadResponse(stream, Stopwatch.StartNew());
                    }
                }
            }
            catch (SocketException ex) { throw new ReaderNetworkDisconnectedException(ConnectionHelp, ex); }
            catch (IOException ex)
            {
                if (ex.InnerException is SocketException) throw new ReaderNetworkDisconnectedException(ConnectionHelp, ex);
                throw;
            }
        }
        private static int ReadByte(Stream input, Func<bool> expired)
        {
            if (expired()) throw new ReaderNetworkDisconnectedException("Läsaren svarade för långsamt.");
            return input.ReadByte();
        }
        private static string Line(Stream input, Func<bool> expired, ref int remaining)
        {
            StringBuilder line = new StringBuilder();
            while (true)
            {
                if (--remaining < 0) throw new InvalidDataException("För stort HTTP-huvud från läsaren.");
                int b = ReadByte(input, expired);
                if (b == '\r')
                {
                    if (--remaining < 0 || ReadByte(input, expired) != '\n') throw new InvalidDataException("Avklippt HTTP-svar från läsaren.");
                    return line.ToString();
                }
                if (b < 32 || b > 126) throw new InvalidDataException("Ogiltigt HTTP-svar från läsaren.");
                line.Append((char)b);
            }
        }
        private static void CopyBytes(Stream input, Stream output, int count, Func<bool> expired)
        {
            for (int i = 0; i < count; i++)
            {
                int b = ReadByte(input, expired);
                if (b < 0) throw new InvalidDataException("Avklippt läsarsvar.");
                output.WriteByte((byte)b);
            }
        }
        internal static string ReadResponse(Stream input, Stopwatch clock)
        {
            return ReadResponse(input, delegate { return clock.ElapsedMilliseconds > 6000; });
        }
        internal static string ReadResponse(Stream input, Func<bool> expired)
        {
            int remaining = MaxHeaders;
            string status = Line(input, expired, ref remaining);
            if (!Regex.IsMatch(status, @"\AHTTP/1\.[01] 200(?: .*)?\z")) throw new InvalidDataException("Läsaren svarade inte OK.");
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string line;
            while ((line = Line(input, expired, ref remaining)).Length != 0)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || !Regex.IsMatch(line.Substring(0, colon), @"\A[A-Za-z0-9!#$%&'*+.^_`|~-]+\z")) throw new InvalidDataException("Ogiltigt HTTP-huvud.");
                string name = line.Substring(0, colon), value = line.Substring(colon + 1).Trim();
                if (headers.ContainsKey(name)) throw new InvalidDataException("Dubbelt HTTP-huvud.");
                headers.Add(name, value);
            }
            string transfer, size, encoding;
            bool chunked = headers.TryGetValue("Transfer-Encoding", out transfer), length = headers.TryGetValue("Content-Length", out size);
            if ((chunked && (length || !String.Equals(transfer, "chunked", StringComparison.OrdinalIgnoreCase))) ||
                (headers.TryGetValue("Content-Encoding", out encoding) && !String.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Oväntad HTTP-kodning från läsaren.");
            using (MemoryStream body = new MemoryStream())
            {
                if (chunked)
                {
                    while (true)
                    {
                        // Bound metadata across all chunks and trailers, not just each individual line.
                        string chunk = Line(input, expired, ref remaining);
                        int semicolon = chunk.IndexOf(';'); if (semicolon >= 0) chunk = chunk.Substring(0, semicolon);
                        uint bytes;
                        if (chunk.Length == 0 || chunk.Length > 8 || !UInt32.TryParse(chunk, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes) || bytes > MaxBody - body.Length)
                            throw new InvalidDataException("För stort eller ogiltigt läsarsvar.");
                        if (bytes == 0)
                        {
                            while ((line = Line(input, expired, ref remaining)).Length != 0)
                                if (line.IndexOf(':') <= 0) throw new InvalidDataException("Ogiltigt HTTP-slut från läsaren.");
                            break;
                        }
                        CopyBytes(input, body, (int)bytes, expired);
                        if (ReadByte(input, expired) != '\r' || ReadByte(input, expired) != '\n') throw new InvalidDataException("Avklippt HTTP-del från läsaren.");
                    }
                }
                else if (length)
                {
                    int bytes;
                    if (!Int32.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out bytes) || bytes < 0 || bytes > MaxBody)
                        throw new InvalidDataException("För stort läsarsvar.");
                    CopyBytes(input, body, bytes, expired);
                }
                else
                {
                    int b;
                    while ((b = ReadByte(input, expired)) >= 0)
                    {
                        if (body.Length >= MaxBody) throw new InvalidDataException("För stort läsarsvar.");
                        body.WriteByte((byte)b);
                    }
                }
                try { return new UTF8Encoding(false, true).GetString(body.ToArray()); }
                catch (DecoderFallbackException ex) { throw new InvalidDataException("Ogiltig textkodning i läsarsvaret.", ex); }
            }
        }
    }
}
