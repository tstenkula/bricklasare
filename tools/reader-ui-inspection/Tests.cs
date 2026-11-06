namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;

    public static class InspectionTests
    {
        private static int count;
        private static readonly IPAddress Address = IPAddress.Parse("192.168.63.99");
        private static void Check(bool ok, string reason) { count++; if (!ok) throw new InvalidOperationException("Inspection test: " + reason); }
        private static void Reject(Action action, string reason)
        {
            bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } catch (IOException) { rejected = true; }
            Check(rejected, reason);
        }
        private static byte[] Bytes(string s) { return Encoding.ASCII.GetBytes(s); }
        private static byte[] Response(string s)
        {
            using (MemoryStream stream = new MemoryStream(Bytes(s))) return ReaderNetwork.PublicFileResponse(stream, delegate { return false; });
        }
        private static string Capture(Dictionary<string, string> files, List<string> fetched, Dictionary<string, byte[]> saved)
        {
            return ReaderSoundInspection.Collect(Address, delegate(string path) {
                fetched.Add(path); string body;
                if (!files.TryGetValue(path, out body)) throw new IOException();
                return Bytes(body);
            }, delegate(string name, byte[] body) { saved.Add(name, body); }, delegate { return 0; });
        }
        public static int Run()
        {
            count = 0;
            Check(ReaderSoundInspection.FailureCode(new InvalidDataException("synthetic-private-message")) == "UNCLASSIFIED", "Arbitrary exception text is never converted into report output");
            Check(ReaderSoundInspection.FailureCode(new InvalidDataException("Oväntad HTTP-kodning från läsaren.")) == "HTTP_ENCODING", "Known HTTP failure classified without raw data");
            {
                string next;
                List<string> trace = new List<string>();
                using (MemoryStream stream = new MemoryStream(Bytes("HTTP/1.0 302 Found\r\nLocation: /ui/index.htm\r\n\r\n")))
                    Check(ReaderNetwork.InspectPublicResponse(stream, delegate { return false; }, Address, "/", trace.Add, out next) == null && next == "/ui/index.htm", "Same-reader static redirect accepted without executing code");
                Check(trace.Contains("HTTP_STATUS_302") && trace.Contains("LOCAL_STATIC_REDIRECT"), "HTTP status and redirect traced without raw headers");
                foreach (string location in new[] { "http://other.invalid/a.htm", "/cjax", "/save.htm?cmd=1", "//other.invalid/a.js", "/a%2Ehtm", "http://192.168.63.98/index.htm" })
                    Reject(delegate { using (MemoryStream stream = new MemoryStream(Bytes("HTTP/1.0 302 Found\r\nLocation: " + location + "\r\n\r\n"))) ReaderNetwork.InspectPublicResponse(stream, delegate { return false; }, Address, "/", delegate { }, out next); }, "Redirect cannot escape fixed USB reader or navigate command/query endpoints");
                using (MemoryStream stream = new MemoryStream(Bytes("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nabc\r\n0\r\n\r\n")))
                    Check(ReaderNetwork.InspectPublicResponse(stream, delegate { return false; }, Address, "/", trace.Add, out next).SequenceEqual(Bytes("abc")) && next == null, "Header replay preserves strict bounded chunk parser");
                int calls = 0;
                Reject(delegate { ReaderNetwork.FollowStaticRedirects("/", delegate(string path, out string target) { calls++; target = "/index.htm"; return null; }, delegate { }); }, "Redirect loops are bounded");
                Check(calls == 4, "At most initial request plus three redirects");
                string final = null;
                byte[] result = ReaderNetwork.FollowStaticRedirects("/", delegate(string path, out string target) { target = path == "/" ? "/ui/index.htm" : null; return target == null ? Bytes("ok") : null; }, delegate(string path) { final = path; });
                Check(final == "/ui/index.htm" && result.SequenceEqual(Bytes("ok")), "Final resource path is retained for relative references");
                List<string> fetched = new List<string>(); Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>();
                ReaderSoundInspection.Collect(Address, delegate(string path) { fetched.Add(path); return Bytes(path == "/" ? "<script src='reader.js'></script>" : "var code=1;"); }, delegate(string name, byte[] b) { saved.Add(name, b); }, delegate { return 0; }, delegate(string path) { return path == "/" ? "/ui/index.htm" : path; });
                Check(fetched.SequenceEqual(new[] { "/", "/ui/reader.js" }), "Redirected page resolves relative script against final directory");
                saved.Clear();
                Reject(delegate { ReaderSoundInspection.Collect(Address, delegate { return Bytes("<body>No scripts</body>"); }, delegate(string name, byte[] b) { saved.Add(name, b); }, delegate { return 0; }); }, "No scripts remains a failure");
                Check(saved.ContainsKey("rapport.txt"), "No-script failure still saves report");
                string folder = Path.Combine(Path.GetTempPath(), "bricklasare-inspection-test-" + Guid.NewGuid().ToString("N"));
                try {
                    Reject(delegate { ReaderSoundInspection.Run("synthetic-private-value", folder); }, "Invalid address fails before network");
                    string log = File.ReadAllText(Path.Combine(folder, "kontroll.txt"));
                    Check(log.Contains("STOP_STAGE_ADDRESS_VALIDATION") && log.Contains("ERROR_TYPE_System.IO.InvalidDataException") && !log.Contains("synthetic-private-value"), "Failure checkpoint exists without private input or exception message");
                } finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
                fetched.Clear(); saved.Clear();
                ReaderSoundInspection.Collect(Address, delegate(string path) { fetched.Add(path); if (path == "/bad.js") throw new InvalidDataException(); return Bytes(path == "/" ? "<script src='bad.js'></script><script src='good.js'></script>" : "var good=1;"); }, delegate(string name, byte[] b) { saved.Add(name, b); }, delegate { return 0; });
                Check(saved.Count == 2 && fetched.Count == 3, "Malformed optional resource does not discard other available scripts");
            }
            foreach (string path in new[] { "/", "/index.html", "/led.htm", "/js/reader.js", "/jquery.min.js", "/scripts/prototype-1.6.0.js" })
            {
                Check(ReaderNetwork.PublicFilePath(path), "Literal resource accepted");
                string request = Encoding.ASCII.GetString(ReaderNetwork.PublicFileRequest(Address, path));
                Check(request == "GET " + path + " HTTP/1.1\r\nHost: 192.168.63.99\r\nAccept-Encoding: identity\r\nConnection: close\r\n\r\n", "Only fixed GET with no data or authentication");
            }
            foreach (string path in new[] { null, "", "file.js", "//foreign.invalid/file.js", "/cjax", "/cgi/save", "/file.js?cmd=reset", "/file.js#x",
                "/%2e%2e/file.js", "/../file.js", "/foo/./file.js", "/foo\\file.js", "/file.js\r\nX-Evil: value", "/file.json", "/file.exe", "/.js", "/a..js", "/file.js/", "http://192.168.63.99/file.js" })
                Reject(delegate { ReaderNetwork.PublicFileRequest(Address, path); }, "Command/query/foreign/traversal/header path rejected");
            foreach (string remote in new[] { "127.0.0.1", "8.8.8.8", "::1" })
                Reject(delegate { ReaderNetwork.PublicFileRequest(IPAddress.Parse(remote), "/"); }, "Only private IPv4 candidate allowed");
            Check(ReaderSoundInspection.ReferencePath(Address, "/pages/led.htm", "reader.js") == "/pages/reader.js", "Relative resources resolved locally");
            Check(ReaderSoundInspection.ReferencePath(Address, "/", "http://192.168.63.99/a.min.js") == "/a.min.js", "Same-reader absolute URL");
            foreach (string link in new[] { "http://other.invalid/a.js", "http://192.168.63.98/a.js", "https://192.168.63.99/a.js", "//other.invalid/a.js",
                "http://user@192.168.63.99/a.js", "http://192.168.63.99:81/a.js", "../a.js", "a.js?set=1", "a.js#x", "/a%2Ejs", "javascript:reset('a.js')" })
                Check(ReaderSoundInspection.ReferencePath(Address, "/", link) == null, "No external/command/encoded resource references");
            Check(ReaderSoundInspection.SourceFile(" src='plain.js' type='text/javascript'") == "plain.js", "Single quoted source");
            Check(ReaderSoundInspection.SourceFile(" type=text/javascript src=plain.js defer") == "plain.js", "Unquoted relative source");
            Check(ReaderSoundInspection.SourceFile(" src='plain.js' /") == "plain.js", "Quoted self-closing resource source");
            Check(ReaderSoundInspection.SourceFile(" src=plain.js/") == "plain.js/", "Unquoted slash remains part of source value");
            Check(ReaderSoundInspection.SourceFile(" title=\"text src='save.js'\" data-src='not.js'") == null, "Attribute values and data-src are not src");
            Check(ReaderSoundInspection.SourceFile(" src='one.js' src='two.js'") == null, "Duplicate src is ambiguous");
            Check(ReaderSoundInspection.SourceFile(" title='broken src=save.js") == null, "Malformed attribute cannot invent a source");
            string body = "// synthetic source\nvar sound='led.htm';";
            Check(Response("HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\n\r\n" + body).SequenceEqual(Bytes(body)), "Source bytes preserved");
            string big = new string('x', 20000);
            Check(Response("HTTP/1.0 200 OK\r\n\r\n" + big).Length == big.Length, "Tool-only larger response");
            Reject(delegate { using (MemoryStream input = new MemoryStream(Bytes("HTTP/1.0 200 OK\r\n\r\n" + big))) ReaderNetwork.ReadResponse(input, delegate { return false; }); }, "App response bound remains 16KiB");
            foreach (string bad in new[] { "HTTP/1.1 302 Found\r\nLocation: http://other.invalid\r\n\r\n", "HTTP/1.1 404 Missing\r\n\r\n",
                "HTTP/1.1 200 OK\r\nContent-Length: 262145\r\n\r\n", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nx",
                "HTTP/1.1 200 OK\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n\r\nx", "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n",
                "HTTP/1.0 200 OK\r\n\r\n\0", "HTTP/1.0 200 OK\r\n\r\n" + new string('x', 262145) })
                Reject(delegate { Response(bad); }, "Redirect/malformed/empty/binary/oversized response rejected");
            Reject(delegate { using (MemoryStream input = new MemoryStream(Bytes("HTTP/1.0 200 OK\r\n\r\nx"))) ReaderNetwork.PublicFileResponse(input, delegate { return true; }); }, "HTTP deadline honored");
            {
                Dictionary<string, string> files = new Dictionary<string, string> {
                    { "/", "<body>synthetic-private-id-123456789012<script src=common.min.js></script><script>var page='led.htm';</script><frame src='pages/panel.html'><a href='save.html'>Action</a><form action='save.html'><input value='synthetic-sensitive'></form><script title=\"src='bad.js'\" data-src='also-bad.js'></script>" },
                    { "/common.min.js", "var operation='save.html';var command='/cjax';var foreign='http://evil.invalid/x.js';" },
                    { "/pages/panel.html", "<script src='reader.js'></script><frame src='/'>" },
                    { "/pages/reader.js", "var sequence='Keyboard Wedge';" }
                };
                List<string> fetched = new List<string>(); Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>();
                string report = Capture(files, fetched, saved);
                Check(fetched.SequenceEqual(new[] { "/", "/common.min.js", "/pages/panel.html", "/pages/reader.js" }), "Only script/frame resources are fetched, never operations/forms/JS literals");
                Check(!fetched.Contains("/save.html") && !fetched.Contains("/led.htm") && report.Contains("/led.htm"), "Candidates recorded without navigating actions");
                Check(saved.Count == 4 && saved.Keys.All(delegate(string f) { return f == "rapport.txt" || f.EndsWith(".txt"); }), "Only inert script text and report saved");
                Check(!saved.Values.Any(delegate(byte[] b) { return Encoding.ASCII.GetString(b).Contains("synthetic-private-id") || Encoding.ASCII.GetString(b).Contains("synthetic-sensitive"); }), "HTML values are not stored");
                Check(saved.Values.Any(delegate(byte[] b) { return Encoding.ASCII.GetString(b).Contains("var page='led.htm'"); }), "Inline vendor code retained for review");
            }
            {
                string html = "<!-- <iframe src='comment.html'></iframe><script>comment-secret</script> -->" +
                    "<div title=\"<script src='attribute.js'>attribute-secret</script>\"></div>" +
                    "<script:example src='custom.js'></script:example>" +
                    "<style><script src='style.js'>style-secret</script></style>" +
                    "<textarea><iframe src='textarea.html'></iframe><script>textarea-secret</script></textarea>" +
                    "<title><script src='title.js'>title-secret</script></title>" +
                    "<script>var template = \"<script src='literal.js'>\";</script>" +
                    "<script><!--<script>nested</script><iframe src='double-escaped.html'>double-escaped-secret</iframe>--></script>" +
                    "<script src='actual.min.js' /><iframe src='script-body.html'>script-body-secret</iframe></script>" +
                    "<iframe src='panel.html' /><script src='fallback.js'>iframe-fallback-secret</script></iframe>" +
                    "<frame src='panel.html' />";
                Dictionary<string, string> files = new Dictionary<string, string> {
                    { "/", html }, { "/actual.min.js", "// actual external source" },
                    { "/panel.html", "<script>var panel = true;</script>" }
                };
                List<string> fetched = new List<string>(); Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>();
                Capture(files, fetched, saved);
                Check(fetched.SequenceEqual(new[] { "/", "/actual.min.js", "/panel.html" }), "Only real resource tags outside comments, attributes and raw text are fetched");
                string[] hidden = { "comment-secret", "attribute-secret", "style-secret", "textarea-secret", "title-secret", "iframe-fallback-secret" };
                Check(!saved.Where(delegate(KeyValuePair<string, byte[]> item) { return item.Key != "rapport.txt"; })
                    .Any(delegate(KeyValuePair<string, byte[]> item) { string source = Encoding.ASCII.GetString(item.Value); return hidden.Any(delegate(string value) { return source.Contains(value); }); }),
                    "Only actual script bodies are extracted");
                Check(saved.Values.Any(delegate(byte[] bytes) { return Encoding.ASCII.GetString(bytes).Contains("script-body-secret"); }),
                    "Self-closing script syntax still preserves raw script body without navigating it");
                Check(saved.Values.Any(delegate(byte[] bytes) { return Encoding.ASCII.GetString(bytes).Contains("double-escaped-secret"); }),
                    "Legacy comment-wrapped double-escaped script content remains raw text");
            }
            {
                foreach (string context in new[] { "template", "svg", "math" })
                {
                    Dictionary<string, string> files = new Dictionary<string, string> {
                        { "/", "<script src='before.js'></script><" + context + "><script src='inert.js'></script></" + context + "><script src='after.js'></script>" },
                        { "/before.js", "// real source before unsupported context" }
                    };
                    List<string> fetched = new List<string>(); Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>();
                    Capture(files, fetched, saved);
                    Check(fetched.SequenceEqual(new[] { "/", "/before.js" }), "Unsupported inert or foreign context stops further discovery");
                }
            }
            {
                List<string> fetched = new List<string>(); Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>();
                byte[] real = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Bytes("// source bytes remain exact")).ToArray();
                byte[] fallback = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Bytes(" \r\n<html><body>fallback-private-id<input value='fallback-sensitive'></body></html>")).ToArray();
                string report = ReaderSoundInspection.Collect(Address, delegate(string path) {
                    fetched.Add(path);
                    if (path == "/") return Bytes("<script src='missing.js'></script><script src='good.js'></script>");
                    return path == "/missing.js" ? fallback : real;
                }, delegate(string name, byte[] bytes) { saved.Add(name, bytes); }, delegate { return 0; });
                Check(fetched.SequenceEqual(new[] { "/", "/missing.js", "/good.js" }) && report.Contains("sparades inte"), "HTML soft-404 is skipped and other scripts remain available");
                Check(saved.Count == 2 && saved["script-001.txt"].SequenceEqual(real), "Accepted external script bytes and BOM remain unchanged");
                Check(!saved.Values.Any(delegate(byte[] bytes) { string source = Encoding.ASCII.GetString(bytes); return source.Contains("fallback-private-id") || source.Contains("fallback-sensitive"); }),
                    "Complete HTML fallback and private DOM values are not saved");
            }
            {
                string root = "<script src='missing.js'></script><script src='good.js'></script>";
                Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>();
                string report = ReaderSoundInspection.Collect(Address, delegate(string path) {
                    if (path == "/") return Bytes(root);
                    if (path == "/missing.js") throw new SocketException();
                    return Bytes("// actual synthetic script");
                }, delegate(string name, byte[] bytes) { saved.Add(name, bytes); }, delegate { return 0; });
                Check(report.Contains("kunde inte hämtas") && saved.Count == 2, "Missing optional socket resource does not discard available code");
            }
            {
                List<string> fetched = new List<string>(); Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>();
                string root = String.Join("", Enumerable.Range(0, 100).Select(delegate(int n) { return "<script src='f" + n + ".js'></script>"; }));
                Dictionary<string, string> files = Enumerable.Range(0, 100).ToDictionary(delegate(int n) { return "/f" + n + ".js"; }, delegate(int n) { return "// synthetic"; }); files["/"] = root;
                Capture(files, fetched, saved); Check(fetched.Count == 32, "Request count bounded");
                Reject(delegate { ReaderSoundInspection.Collect(Address, delegate { return Bytes("<script>x</script>"); }, delegate { }, delegate { return 90001; }); }, "Collection deadline before fetch");
                long time = 0;
                Reject(delegate { ReaderSoundInspection.Collect(Address, delegate { time = 90001; return Bytes("<script>x</script>"); }, delegate { }, delegate { return time; }); }, "Collection deadline after final fetch");
                Reject(delegate { ReaderSoundInspection.Collect(Address, delegate { return Bytes("<script>x</script>"); }, delegate { throw new IOException(); }, delegate { return 0; }); }, "Saving failure stops collection");
                Reject(delegate { Capture(new Dictionary<string, string> { { "/", "No scripts" } }, new List<string>(), new Dictionary<string, byte[]>()); }, "Missing code is not success");
                string many = String.Concat(Enumerable.Repeat("<script>x</script>", 65));
                Reject(delegate { Capture(new Dictionary<string, string> { { "/", many } }, new List<string>(), new Dictionary<string, byte[]>()); }, "Inline script count bounded");
                int calls = 0;
                Reject(delegate { ReaderSoundInspection.Collect(Address, delegate(string path) {
                    calls++; return Bytes(path == "/" ? root : "//" + new string('x', 250000));
                }, delegate { }, delegate { return 0; }); }, "Total data bound");
                Check(calls < 32, "Size bound stops before exhausting request allowance");
            }
            Console.WriteLine(count + " inspection tests passed. No network, hardware, reader command or external source execution.");
            return count;
        }
    }
}
