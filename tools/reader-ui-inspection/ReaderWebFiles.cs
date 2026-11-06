// Compiled only by the separate read-only inspection tool, never by the app launcher.
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.RegularExpressions;

    internal static partial class ReaderNetwork
    {
        internal const int MaxPublicFile = 2097152;
        private static uint Checksum(byte[] bytes, bool crc)
        {
            if (!crc)
            {
                uint a = 1, b = 0;
                foreach (byte value in bytes) { a = (a + value) % 65521; b = (b + a) % 65521; }
                return (b << 16) | a;
            }
            uint result = 0xFFFFFFFF;
            foreach (byte value in bytes)
            {
                result ^= value;
                for (int bit = 0; bit < 8; bit++) result = (result & 1) != 0 ? (result >> 1) ^ 0xEDB88320u : result >> 1;
            }
            return ~result;
        }
        private static uint Footer(byte[] bytes, int offset, bool little)
        {
            uint result = 0;
            for (int i = 0; i < 4; i++) result = (result << 8) | bytes[offset + (little ? 3 - i : i)];
            return result;
        }
        internal static byte[] DecodePublicBody(byte[] bytes, string encoding, Func<bool> expired)
        {
            if (bytes == null || bytes.Length > MaxPublicFile) throw new InvalidDataException("För stort läsarsvar.");
            bool gzip = String.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase);
            bool deflate = String.Equals(encoding, "deflate", StringComparison.OrdinalIgnoreCase);
            if (!gzip && !deflate)
            {
                if (encoding != null && !String.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Oväntad HTTP-kodning från läsaren.");
                return ValidatePublicText(bytes);
            }
            bool zlib = deflate && bytes.Length >= 2 && (bytes[0] & 15) == 8 && bytes[0] >> 4 <= 7 && (bytes[0] * 256 + bytes[1]) % 31 == 0;
            if ((gzip && (bytes.Length < 18 || bytes[0] != 31 || bytes[1] != 139 || bytes[2] != 8 || (bytes[3] & 224) != 0)) ||
                (deflate && (!zlib || bytes.Length < 6 || (bytes[1] & 32) != 0))) throw new InvalidDataException("Ogiltig komprimerad läsarfil.");
            byte[] result;
            try
            {
                // Framework DeflateStream consumes RFC1951. HTTP's RFC1950 wrapper
                // is removed explicitly, and its Adler-32 is verified below.
                using (MemoryStream input = new MemoryStream(bytes, zlib ? 2 : 0, bytes.Length - (zlib ? 6 : 0), false))
                using (Stream decoder = gzip ? (Stream)new GZipStream(input, CompressionMode.Decompress) : new DeflateStream(input, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    byte[] buffer = new byte[4096]; int count;
                    while ((count = decoder.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (expired()) throw new ReaderNetworkDisconnectedException("Läsaren svarade för långsamt.");
                        if (count > MaxPublicFile - output.Length) throw new InvalidDataException("Uppackad läsarfil är för stor.");
                        output.Write(buffer, 0, count);
                    }
                    result = output.ToArray();
                }
            }
            catch (InvalidDataException error)
            {
                if (error.Message == "Uppackad läsarfil är för stor.") throw;
                throw new InvalidDataException("Ogiltig komprimerad läsarfil.", error);
            }
            // Check trailers explicitly as older Framework stream implementations
            // can accept a truncated stream without reporting the missing trailer.
            if ((gzip && (Footer(bytes, bytes.Length - 8, true) != Checksum(result, true) || Footer(bytes, bytes.Length - 4, true) != result.Length)) ||
                (zlib && Footer(bytes, bytes.Length - 4, false) != Checksum(result, false))) throw new InvalidDataException("Ogiltig komprimerad läsarfil.");
            return ValidatePublicText(result);
        }
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
            string location = null, line, contentEncoding = null;
            while ((line = Line(input, expired, ref remaining)).Length != 0)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) throw new InvalidDataException("Ogiltigt HTTP-huvud.");
                string name = line.Substring(0, colon), value = line.Substring(colon + 1).Trim();
                if (String.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    uint size;
                    trace(UInt32.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out size) ?
                        "HTTP_LENGTH_" + size : "HTTP_LENGTH_INVALID");
                }
                if (String.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    if (contentEncoding != null) throw new InvalidDataException("Dubbelt HTTP-huvud.");
                    contentEncoding = value;
                    trace("HTTP_ENCODING_" + (String.Equals(value, "gzip", StringComparison.OrdinalIgnoreCase) ? "GZIP" :
                        String.Equals(value, "deflate", StringComparison.OrdinalIgnoreCase) ? "DEFLATE" :
                        String.Equals(value, "identity", StringComparison.OrdinalIgnoreCase) ? "IDENTITY" : "OTHER"));
                    if (!String.Equals(value, "gzip", StringComparison.OrdinalIgnoreCase) && !String.Equals(value, "deflate", StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(value, "identity", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Oväntad HTTP-kodning från läsaren.");
                    // The original Content-Length/chunk framing still describes the
                    // encoded bytes. Only this standalone tool decodes their content.
                    header.Append("Content-Encoding: identity\r\n");
                }
                else header.Append(line).Append("\r\n");
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
                byte[] encoded = ReadResponseBytes(replay, expired, MaxPublicFile);
                trace("ENCODED_BYTES_" + encoded.Length);
                byte[] body = DecodePublicBody(encoded, contentEncoding, expired);
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
            return ValidatePublicText(ReadResponseBytes(input, expired, MaxPublicFile));
        }
        private static byte[] ValidatePublicText(byte[] bytes)
        {
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
