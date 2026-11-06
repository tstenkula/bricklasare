// Compiled only by the separate read-only inspection tool, never by the app launcher.
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.RegularExpressions;

    internal static partial class ReaderNetwork
    {
        private sealed class HeaderReplay : Stream
        {
            private readonly MemoryStream prefix;
            private readonly Stream source;
            internal HeaderReplay(byte[] header, Stream body) { prefix = new MemoryStream(header, false); source = body; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count) { int n = prefix.Read(buffer, offset, count); return n == 0 ? source.Read(buffer, offset, count) : n; }
            public override int ReadByte() { int b = prefix.ReadByte(); return b < 0 ? source.ReadByte() : b; }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) prefix.Dispose(); base.Dispose(disposing); }
        }
        internal static byte[] InspectPublicResponse(Stream input, Func<bool> expired, IPAddress remote, string path,
            Action<string> trace, out string redirect)
        {
            redirect = null;
            trace("HTTP_HEADER");
            int remaining = 8192;
            string status = Line(input, expired, ref remaining);
            Match match = Regex.Match(status, @"\AHTTP/1\.[01] (?<status>[0-9]{3})(?: .*)?\z");
            if (!match.Success) throw new InvalidDataException("Ogiltigt HTTP-svar.");
            int code = Int32.Parse(match.Groups["status"].Value, System.Globalization.CultureInfo.InvariantCulture);
            trace("HTTP_STATUS_" + code);
            StringBuilder header = new StringBuilder(status + "\r\n");
            string location = null, line;
            while ((line = Line(input, expired, ref remaining)).Length != 0)
            {
                header.Append(line).Append("\r\n");
                int colon = line.IndexOf(':');
                if (colon <= 0) throw new InvalidDataException("Ogiltigt HTTP-huvud.");
                if (String.Equals(line.Substring(0, colon), "Location", StringComparison.OrdinalIgnoreCase))
                {
                    if (location != null) throw new InvalidDataException("Dubbelt omdirigeringsmål.");
                    location = line.Substring(colon + 1).Trim();
                }
            }
            if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
            {
                redirect = ReaderSoundInspection.ReferencePath(remote, path, location);
                if (redirect == null) { trace("REDIRECT_REJECTED"); throw new InvalidDataException("Omdirigeringen är inte en tillåten lokal läsarfil."); }
                trace("LOCAL_STATIC_REDIRECT");
                return null;
            }
            trace("HTTP_BODY");
            header.Append("\r\n");
            using (HeaderReplay replay = new HeaderReplay(Encoding.ASCII.GetBytes(header.ToString()), input))
            {
                byte[] body = PublicFileResponse(replay, expired);
                trace("TEXT_BYTES_" + body.Length);
                return body;
            }
        }
        internal static bool PublicFilePath(string path)
        {
            // Literal static filenames only: no query, encoding, command endpoint or traversal.
            return path == "/" || (path != null && path.Length <= 180 && !path.Contains("..") && Regex.IsMatch(path,
                @"\A/(?:[A-Za-z0-9_][A-Za-z0-9_.-]*/){0,5}[A-Za-z0-9_][A-Za-z0-9_.-]*\.(?:html?|js)\z", RegexOptions.IgnoreCase));
        }
        internal static byte[] PublicFileRequest(IPAddress remote, string path)
        {
            if (!IsPrivate(remote) || !PublicFilePath(path)) throw new InvalidDataException("Otillåten läsarfil.");
            return Encoding.ASCII.GetBytes("GET " + path + " HTTP/1.1\r\nHost: " + remote.ToString() +
                "\r\nAccept-Encoding: identity\r\nConnection: close\r\n\r\n");
        }
        internal static byte[] PublicFileResponse(Stream input, Func<bool> expired)
        {
            byte[] bytes = ReadResponseBytes(input, expired, 262144);
            if (bytes.Length == 0) throw new InvalidDataException("Tom läsarfil.");
            // Files are saved as inert text for inspection, never evaluated as scripts or markup.
            foreach (byte b in bytes)
                if (b == 0 || (b < 32 && b != 9 && b != 10 && b != 13)) throw new InvalidDataException("Läsarfilen är inte text.");
            return bytes;
        }
        internal static byte[] ReadPublicFile(IPAddress remote, string path)
        { return ReadPublicFile(remote, path, delegate { }); }
        internal static byte[] ReadPublicFile(IPAddress remote, string path, Action<string> trace)
        { return ReadPublicFile(remote, path, trace, delegate { }); }
        internal static byte[] ReadPublicFile(IPAddress remote, string path, Action<string> trace, Action<string> resolved)
        { return FollowStaticRedirects(path, delegate(string current, out string next) { return ReadPublicFileOnce(remote, current, trace, out next); }, resolved); }
        internal delegate byte[] StaticFetch(string path, out string next);
        internal static byte[] FollowStaticRedirects(string path, StaticFetch fetch, Action<string> resolved)
        {
            for (int redirects = 0; redirects <= 3; redirects++)
            {
                string next;
                byte[] bytes = fetch(path, out next);
                if (next == null) { resolved(path); return bytes; }
                if (redirects == 3) throw new InvalidDataException("För många omdirigeringar.");
                path = next;
            }
            throw new InvalidDataException("Omdirigering kunde inte slutföras.");
        }
        private static byte[] ReadPublicFileOnce(IPAddress remote, string path, Action<string> trace, out string redirect)
        {
            trace("REQUEST_VALIDATION");
            byte[] request = PublicFileRequest(remote, path);
            trace("USB_VERIFY");
            ReaderAdapterEvidence adapter = Resolve(remote);
            trace("USB_VERIFIED");
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.SendTimeout = 3000; socket.ReceiveTimeout = 3000; socket.NoDelay = true;
                socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(adapter.Index));
                if ((int)socket.GetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31) != adapter.Index) throw new IOException(ConnectionHelp);
                socket.Bind(new IPEndPoint(adapter.Address, 0));
                trace("USB_CONNECT");
                IAsyncResult connecting = socket.BeginConnect(new IPEndPoint(remote, 80), null, null);
                using (System.Threading.WaitHandle wait = connecting.AsyncWaitHandle)
                {
                    if (!wait.WaitOne(3000)) throw new IOException("Läsaren svarade inte via USB.");
                    socket.EndConnect(connecting);
                }
                using (NetworkStream network = new NetworkStream(socket, false))
                using (BufferedStream stream = new BufferedStream(network, 4096))
                {
                    trace("USB_CONNECTED");
                    stream.Write(request, 0, request.Length); stream.Flush();
                    Stopwatch timer = Stopwatch.StartNew();
                    return InspectPublicResponse(stream, delegate { return timer.ElapsedMilliseconds > 6000; }, remote, path, trace, out redirect);
                }
            }
        }
    }
}
