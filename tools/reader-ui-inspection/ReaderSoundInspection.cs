namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;

    public static class ReaderSoundInspection
    {
        private const int MaxRequests = 32, MaxTotal = 2097152;
        private static readonly Regex References = new Regex("[\"'](?<file>[^\"'<>\\r\\n]{1,200}\\.(?:html?|js))[\"']",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        private static readonly Regex Attributes = new Regex("\\G\\s*(?<name>[A-Za-z_:][A-Za-z0-9_.:-]*)(?:\\s*=\\s*(?:\"(?<value>[^\"]{0,4096})\"|'(?<value>[^']{0,4096})'|(?<value>[^\\s\"'<>]{1,4096})))?",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        internal static string SourceFile(string attributes)
        {
            int position = 0; string source = null;
            while (position < attributes.Length)
            {
                string remaining = attributes.Substring(position).Trim();
                if (remaining.Length == 0 || remaining == "/") break;
                Match attribute = Attributes.Match(attributes, position);
                if (!attribute.Success || attribute.Index != position || attribute.Length == 0) return null;
                position += attribute.Length;
                if (String.Equals(attribute.Groups["name"].Value, "src", StringComparison.OrdinalIgnoreCase))
                {
                    if (source != null || !attribute.Groups["value"].Success) return null;
                    source = attribute.Groups["value"].Value;
                }
            }
            return source;
        }
        private static bool HtmlSpace(char value)
        {
            return value == ' ' || value == '\t' || value == '\r' || value == '\n' || value == '\f';
        }
        private static int TagEnd(string text, int position)
        {
            char quote = '\0';
            for (int i = position; i < text.Length; i++)
            {
                char value = text[i];
                if (quote != '\0') { if (value == quote) quote = '\0'; }
                else if (value == '\'' || value == '"') quote = value;
                else if (value == '>') return i;
            }
            return -1;
        }
        private static bool TextAt(string text, int position, string value)
        {
            return position + value.Length <= text.Length &&
                String.Compare(text, position, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }
        private static bool TagNameAt(string text, int position, string name)
        {
            int delimiter = position + name.Length;
            return TextAt(text, position, name) && delimiter < text.Length &&
                (HtmlSpace(text[delimiter]) || text[delimiter] == '/' || text[delimiter] == '>');
        }
        private static int ScriptEnd(string text, int position, out int after)
        {
            // Legacy pages may wrap JavaScript in <!-- ... -->. In that escaped state,
            // <script> enters a double-escaped body whose first </script> is still text.
            int escaped = 0;
            for (int i = position; i < text.Length; i++)
            {
                if (escaped == 0 && TextAt(text, i, "<!--")) { escaped = 1; i += 3; continue; }
                if (escaped != 0 && TextAt(text, i, "-->")) { escaped = 0; i += 2; continue; }
                if (escaped == 1 && TagNameAt(text, i, "<script")) { escaped = 2; i += 6; continue; }
                if (!TagNameAt(text, i, "</script")) continue;
                if (escaped == 2) { escaped = 1; i += 7; continue; }
                int end = TagEnd(text, i + 8);
                if (end < 0) break;
                after = end + 1;
                return i;
            }
            after = text.Length;
            return -1;
        }
        private static int RawEnd(string text, string name, int position, out int after)
        {
            if (name == "script") return ScriptEnd(text, position, out after);
            string closing = "</" + name;
            while (position < text.Length)
            {
                int start = text.IndexOf(closing, position, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                int delimiter = start + closing.Length;
                if (TagNameAt(text, start, closing))
                {
                    int end = TagEnd(text, delimiter);
                    if (end >= 0) { after = end + 1; return start; }
                    break;
                }
                position = delimiter;
            }
            after = text.Length;
            return -1;
        }
        private static void InspectHtml(string text, Action<string> resource, Action<string> script)
        {
            // This conservative scanner is not a complete browser HTML engine. Unsupported
            // inert or foreign contexts stop discovery instead of interpreting their children.
            int position = 0;
            while (position < text.Length)
            {
                int start = text.IndexOf('<', position);
                if (start < 0 || start + 1 >= text.Length) return;
                if (start + 4 <= text.Length && String.CompareOrdinal(text, start, "<!--", 0, 4) == 0)
                {
                    int commentEnd = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
                    if (commentEnd < 0) return;
                    position = commentEnd + 3;
                    continue;
                }
                // Declarations and processing instructions cannot introduce resource elements.
                if (text[start + 1] == '!' || text[start + 1] == '?')
                {
                    int declarationEnd = TagEnd(text, start + 2);
                    if (declarationEnd < 0) return;
                    position = declarationEnd + 1;
                    continue;
                }
                int nameStart = start + 1;
                bool closing = text[nameStart] == '/';
                if (closing) nameStart++;
                if (nameStart >= text.Length || !((text[nameStart] >= 'A' && text[nameStart] <= 'Z') ||
                    (text[nameStart] >= 'a' && text[nameStart] <= 'z')))
                { position = start + 1; continue; }
                int nameEnd = nameStart;
                while (nameEnd < text.Length && !HtmlSpace(text[nameEnd]) && text[nameEnd] != '/' && text[nameEnd] != '>') nameEnd++;
                int end = TagEnd(text, nameEnd);
                if (end < 0) return;
                position = end + 1;
                if (closing) continue;
                string name = text.Substring(nameStart, nameEnd - nameStart).ToLowerInvariant();
                bool isScript = name == "script";
                if ((isScript || name == "frame" || name == "iframe") && end - nameEnd <= 4096)
                {
                    string source = SourceFile(text.Substring(nameEnd, end - nameEnd));
                    if (source != null) resource(source);
                }
                if (name == "plaintext" || name == "template" || name == "svg" || name == "math") return;
                if (isScript || name == "style" || name == "textarea" || name == "title" || name == "iframe" ||
                    name == "xmp" || name == "noembed" || name == "noframes" || name == "noscript")
                {
                    // Tag-shaped strings in raw text and fallback content are not HTML resources.
                    int after;
                    int rawEnd = RawEnd(text, name, position, out after);
                    if (rawEnd < 0) return;
                    if (isScript) script(text.Substring(position, rawEnd - position));
                    position = after;
                }
            }
        }
        private static bool StartsWithMarkup(byte[] bytes)
        {
            int position = 0;
            while (position < bytes.Length)
            {
                if (position + 2 < bytes.Length && bytes[position] == 0xEF && bytes[position + 1] == 0xBB && bytes[position + 2] == 0xBF)
                { position += 3; continue; }
                byte value = bytes[position];
                if (value == 9 || value == 10 || value == 12 || value == 13 || value == 32) { position++; continue; }
                return value == '<';
            }
            return false;
        }
        internal static string ReferencePath(IPAddress remote, string current, string reference)
        {
            if (String.IsNullOrEmpty(reference) || reference.Length > 200 || reference.Contains("%") ||
                reference.Contains("\\") || reference.Contains("?") || reference.Contains("#") || reference.Contains("..")) return null;
            Uri target;
            if (!Uri.TryCreate(new Uri("http://" + remote + current), reference, out target) ||
                target.Scheme != "http" || !target.IsDefaultPort || target.Host != remote.ToString() || target.UserInfo.Length != 0 ||
                !ReaderNetwork.PublicFilePath(target.AbsolutePath)) return null;
            return target.AbsolutePath;
        }
        private static void SaveText(string directory, string name, byte[] bytes)
        {
            using (FileStream output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
        }
        internal static string Collect(IPAddress remote, Func<string, byte[]> fetch, Action<string, byte[]> save, Func<long> clock)
        {
            Queue<string> queue = new Queue<string>(); queue.Enqueue("/");
            HashSet<string> queued = new HashSet<string>(StringComparer.Ordinal) { "/" };
            HashSet<string> candidates = new HashSet<string>(StringComparer.Ordinal);
            List<string> report = new List<string> {
                "BRICKLÄSAREN – UNDERLAG FÖR GEN1-LJUD, VERSION 1",
                "Endast GET av startsida och script-/frame-/iframe-resurser via verifierad USB-anslutning.",
                "Inga APDU-, formulär-, spara- eller återställningskommandon skickas. Ingen kod från läsaren körs.",
                "HTML bearbetas i minnet; bara skript och rapport sparas. Granska lokala uppgifter före delning.",
                "Detta är inte ett prov av ljudstyrning. Inga inställningar laddas upp automatiskt.", "" };
            int requests = 0, total = 0, saved = 0;
            while (queue.Count > 0 && requests < MaxRequests)
            {
                if (clock() > 90000) throw new IOException("Insamlingen tog för lång tid. Eventuellt delunderlag ligger kvar lokalt.");
                string path = queue.Dequeue(); requests++;
                byte[] bytes;
                try { bytes = fetch(path); }
                catch (IOException) { if (path == "/") throw; report.Add(path + " · kunde inte hämtas"); continue; }
                catch (SocketException) { if (path == "/") throw; report.Add(path + " · kunde inte hämtas"); continue; }
                if (clock() > 90000) throw new IOException("Insamlingen tog för lång tid. Eventuellt delunderlag ligger kvar lokalt.");
                if (bytes == null || bytes.Length < 1 || bytes.Length > 262144 || bytes.Length > MaxTotal - total)
                    throw new InvalidDataException("För stort eller ogiltigt underlag.");
                total += bytes.Length;
                // Latin-1 maps every byte without executing or rewriting the source file.
                string text = Encoding.GetEncoding(28591).GetString(bytes);
                if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    // Some embedded servers return an HTML error page with status 200. Never
                    // retain that complete DOM as script text. This deliberately also skips
                    // legacy JavaScript starting with an HTML comment; saved bytes stay exact.
                    if (StartsWithMarkup(bytes))
                    { report.Add(path + " · oväntad markeringskod; sparades inte"); continue; }
                    if (saved >= 64) throw new InvalidDataException("För många skript i underlaget.");
                    string name = "script-" + (++saved).ToString("D3") + ".txt";
                    save(name, bytes); report.Add(name + " · " + path + " · " + bytes.Length + " byte");
                }
                else
                {
                    InspectHtml(text, delegate(string source)
                    {
                        string next = ReferencePath(remote, path, source);
                        if (next != null && queued.Count < MaxRequests && queued.Add(next)) queue.Enqueue(next);
                    }, delegate(string code)
                    {
                        if (String.IsNullOrWhiteSpace(code)) return;
                        if (saved >= 64) throw new InvalidDataException("För många skript i underlaget.");
                        string name = "script-" + (++saved).ToString("D3") + ".txt";
                        byte[] source = Encoding.GetEncoding(28591).GetBytes(code);
                        save(name, source); report.Add(name + " · inbäddat skript i " + path + " · " + source.Length + " byte");
                    });
                }
                // Quoted references outside actual resource elements remain manual candidates.
                foreach (Match match in References.Matches(text))
                {
                    string candidate = ReferencePath(remote, path, match.Groups["file"].Value);
                    if (candidate != null && candidates.Count < 128) candidates.Add(candidate);
                }
            }
            report.Add(""); report.Add("Filreferenser för manuell granskning (hämtas inte automatiskt):");
            foreach (string candidate in candidates.OrderBy(delegate(string p) { return p; })) report.Add(candidate);
            report.Add(""); report.Add("Hämtade byte: " + total + ". Sparade skript: " + saved + ". Förfrågningar: " + requests + ".");
            if (saved == 0) throw new InvalidDataException("Inga skript kunde hämtas. Webbgränssnittets kod behöver då sparas manuellt.");
            string result = String.Join("\r\n", report) + "\r\n";
            save("rapport.txt", new UTF8Encoding(false).GetBytes(result));
            return result;
        }
        public static string Run(string address, string directory)
        {
            IPAddress remote;
            if (!IPAddress.TryParse(address, out remote) || !ReaderNetwork.IsPrivate(remote)) throw new InvalidDataException("Ange läsarens lokala IPv4-adress.");
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Välj en ny, tom mapp för underlaget.");
            Directory.CreateDirectory(directory);
            Stopwatch timer = Stopwatch.StartNew();
            return Collect(remote, delegate(string path) { return ReaderNetwork.ReadPublicFile(remote, path); },
                delegate(string name, byte[] data) { SaveText(directory, name, data); }, delegate { return timer.ElapsedMilliseconds; });
        }
    }
}
