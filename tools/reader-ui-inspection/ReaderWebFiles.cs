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
        {
            byte[] request = PublicFileRequest(remote, path);
            ReaderAdapterEvidence adapter = Resolve(remote);
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.SendTimeout = 3000; socket.ReceiveTimeout = 3000; socket.NoDelay = true;
                socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(adapter.Index));
                if ((int)socket.GetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31) != adapter.Index) throw new IOException(ConnectionHelp);
                socket.Bind(new IPEndPoint(adapter.Address, 0));
                IAsyncResult connecting = socket.BeginConnect(new IPEndPoint(remote, 80), null, null);
                using (System.Threading.WaitHandle wait = connecting.AsyncWaitHandle)
                {
                    if (!wait.WaitOne(3000)) throw new IOException("Läsaren svarade inte via USB.");
                    socket.EndConnect(connecting);
                }
                using (NetworkStream network = new NetworkStream(socket, false))
                using (BufferedStream stream = new BufferedStream(network, 4096))
                {
                    stream.Write(request, 0, request.Length); stream.Flush();
                    Stopwatch timer = Stopwatch.StartNew();
                    return PublicFileResponse(stream, delegate { return timer.ElapsedMilliseconds > 6000; });
                }
            }
        }
    }
}
