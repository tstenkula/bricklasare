// OMNIKEY RCO v2.0 - hardware-test build. C# 5 / .NET Framework 4.5+.
// Protocol: HID 5127-903 B.7 sections 3.2.3, 9.2.2, 9.4.1, A.2, A.8-A.13, A.16, B.2.2-B.2.3.
// Scan sessions change only allow-listed polling and ASK data selection.
// ReaderSound.cs separately journals public indicator-sequence sound changes.
// Apply Changes writes reader flash; every session is journalled and restored.
// Does NOT write cards, change keys, disable EEM, reset to factory defaults or install drivers.
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Web.Script.Serialization;

    public static class Codec
    {
        public static byte[] Hex(string value)
        {
            string s = Regex.Replace(value ?? "", @"\s+", "");
            if ((s.Length % 2) != 0 || !Regex.IsMatch(s, @"\A[0-9a-fA-F]*\z"))
                throw new FormatException("Ogiltiga hexadecimala data.");
            byte[] b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }
        public static string ToHex(byte[] data) { return BitConverter.ToString(data).Replace("-", ""); }
        public static byte[] Slice(byte[] b, int start, int count)
        {
            if (start < 0 || count < 0 || start > b.Length - count) throw new FormatException("Avklippt svar från läsaren.");
            byte[] v = new byte[count]; Array.Copy(b, start, v, 0, count); return v;
        }
        public static byte[] HttpResponse(string body)
        {
            string s = (body ?? "").Trim().TrimStart((char)0xFEFF);
            // Only an unambiguous hexadecimal response is accepted. Never strip arbitrary HTML.
            if (s.StartsWith("\"", StringComparison.Ordinal))
                s = new JavaScriptSerializer().Deserialize<string>(s);
            s = Regex.Replace(s, @"\A(?:RX|Response)\s*:\s*", "", RegexOptions.IgnoreCase);
            byte[] result = Hex(s);
            if (result.Length < 2 || result.Length > 4096) throw new FormatException("Läsarens HTTP-svar är inte ett giltigt APDU-svar. Spara diagnostiken.");
            return result;
        }
        public static byte[] Success(byte[] response)
        {
            if (response == null || response.Length < 2) throw new FormatException("Tomt APDU-svar.");
            int n = response.Length;
            int sw = (response[n - 2] << 8) | response[n - 1];
            if (sw != 0x9000) throw new ReaderStatusException(sw);
            return Slice(response, 0, n - 2);
        }
        public static Tlv ReadTlv(byte[] input, ref int position)
        {
            if (position > input.Length - 2) throw new FormatException("Avklippt TLV-huvud.");
            byte tag = input[position++];
            if ((tag & 31) == 31) throw new FormatException("Oväntad TLV-tagg.");
            int len = input[position++];
            if ((len & 128) != 0)
            {
                int count = len & 127;
                if (count < 1 || count > 2 || position > input.Length - count) throw new FormatException("Ogiltig TLV-längd.");
                len = 0;
                for (int i = 0; i < count; i++) len = (len << 8) | input[position++];
            }
            byte[] value = Slice(input, position, len); position += len;
            return new Tlv { Tag = tag, Value = value };
        }
        public static string GetMibCommand(string oid)
        {
            return MibCommand(oid, false, 5, new byte[0]);
        }
        public static string SetMibCommand(string oid, byte tag, byte[] value)
        {
            return MibCommand(oid, true, tag, value);
        }
        private static string MibCommand(string oid, bool set, byte tag, byte[] value)
        {
            byte[] o = Hex(oid);
            if (o.Length == 0 || o.Length > 32 || value.Length > 127) throw new ArgumentException("MIB-data utanför tillåtna gränser.");
            List<byte> pdu = new List<byte>();
            pdu.Add(set ? (byte)2 : (byte)0); pdu.Add(0); pdu.Add(0);
            pdu.Add(0x0D); pdu.Add((byte)o.Length); pdu.AddRange(o);
            pdu.Add(tag); pdu.Add((byte)value.Length); pdu.AddRange(value);
            List<byte> result = new List<byte>(Hex("FF680000"));
            result.Add((byte)pdu.Count); result.AddRange(pdu); result.Add(0);
            return ToHex(result.ToArray());
        }
        public static Tlv MibResponse(string oid, byte[] response)
        {
            byte[] p = Success(response);
            if (p.Length < 3 || p[0] != 3) throw new FormatException("Oväntat MIB-svar.");
            if (p[1] != 0) throw new ReaderMibException(oid, p[1], p[2]);
            if (p.Length < 7) throw new FormatException("Avklippt MIB-värde.");
            int i = 3; Tlv name = ReadTlv(p, ref i); Tlv value = ReadTlv(p, ref i);
            if (name.Tag != 0x0D || ToHex(name.Value) != oid.ToUpperInvariant() || i != p.Length)
                throw new FormatException("MIB-svaret motsvarar inte den begärda inställningen.");
            return value;
        }
        public static byte[] Atr(byte[] value)
        {
            if (value.Length == 0 || value.All(delegate(byte b) { return b == 0; })) return new byte[0];
            if (value[0] == 0x3B || value[0] == 0x3F) return value;
            // Some firmware presents ATR as an ASCII hex string in the OCTET_STRING.
            string text = Encoding.ASCII.GetString(value).Trim('\0', ' ', '\r', '\n', '\t');
            if (text.Length == 0) return new byte[0];
            if (!text.StartsWith("3B", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("3F", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Okänt ATR-format. Avbryter i stället för att tolka fel teknik.");
            return Hex(text);
        }
        public static byte[] EmPacs(byte[] response)
        {
            byte[] payload = Success(response);
            int p = 0; Tlv envelope = ReadTlv(payload, ref p);
            // HID documents 9D wrapping for LF; HF has a bare BIT STRING. Require LF.
            if (envelope.Tag != 0x9D || p != payload.Length) throw new FormatException("Svaret är inte HID:s dokumenterade LF/Prox-svar (9D). eR visas inte.");
            int q = 0; Tlv bits = ReadTlv(envelope.Value, ref q);
            if (bits.Tag != 3 || q != envelope.Value.Length || bits.Value.Length != 6 || bits.Value[0] != 0)
                throw new FormatException("EM-svaret är inte 40 bitar utan utfyllnad. Det kan vara en annan bricktyp eller ett annat firmwareformat. Spara diagnostiken.");
            return Slice(bits.Value, 1, 5);
        }
        public static byte[] Order(byte[] raw, string order)
        {
            byte[] copy = (byte[])raw.Clone();
            if (order == "Reverse") Array.Reverse(copy);
            else if (order != "Forward") throw new InvalidOperationException("Byteordningen är inte kontrollerad. Kör referensprovet först.");
            return copy;
        }
        public static string Rco9(byte[] normalized)
        {
            if (normalized.Length < 4 || normalized.Length > 16) throw new ArgumentException("UID måste vara 4–16 byte.");
            int n = normalized.Length;
            int hi = (normalized[n - 4] << 8) | normalized[n - 3];
            int lo = (normalized[n - 2] << 8) | normalized[n - 1];
            return (hi.ToString("D5", CultureInfo.InvariantCulture) + lo.ToString("D5", CultureInfo.InvariantCulture)).Substring(1);
        }
        public static string ReferenceOrder(byte[] raw, string expectedHex)
        {
            bool forward = ToHex(raw) == expectedHex;
            byte[] reversed = (byte[])raw.Clone(); Array.Reverse(reversed);
            bool reverse = ToHex(reversed) == expectedHex;
            if (forward == reverse) throw new InvalidOperationException("Referensbrickan matchar inte entydigt. Använd den lokalt konfigurerade referensbrickan. Ingen byteordning sparades.");
            return forward ? "Forward" : "Reverse";
        }
    }
    public sealed class Tlv { public byte Tag; public byte[] Value; }
    public sealed class ReaderMibException : InvalidOperationException
    {
        public byte Status { get; private set; }
        public byte Index { get; private set; }
        public string Command { get; private set; }
        public ReaderMibException(string oid, byte status, byte index)
            : base("Läsaren avvisade ett inställningskommando. Spara en felrapport via Hjälp.")
        {
            Command = ReaderDiagnostics.MibName(oid); Status = status; Index = index;
        }
    }
    public sealed class ReaderStatusException : Exception
    {
        public int StatusWord { get; private set; }
        public ReaderStatusException(int sw) : base("Läsaren svarade " + sw.ToString("X4") + ". Kontrollera brickans läge och spara diagnostiken om felet kvarstår.") { StatusWord = sw; }
    }
    public sealed class PollSetting
    {
        public string Name; public string Oid;
        public PollSetting(string name, string oid) { Name = name; Oid = oid; }
    }
    public sealed class SavedSetting { public string Oid { get; set; } public int Tag { get; set; } public string HexValue { get; set; } }
    public sealed class Journal
    {
        public int Schema { get; set; }
        public string ReaderId { get; set; }
        public string Created { get; set; }
        public List<SavedSetting> Settings { get; set; }
    }
    // Known reference identifiers are local IT configuration, never source fixtures.
    public sealed class ReferenceCard
    {
        public const string FileName = "referensbricka.json";
        public string MifareHex { get; set; }
        public string EmHex { get; set; }

        public static ReferenceCard Read(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > 16384)
                throw new InvalidDataException("Lokal referensbricka saknas eller är ogiltig. Be IT konfigurera referensbricka.json i programmets lokala datamapp.");
            ReferenceCard card;
            try { card = new JavaScriptSerializer().Deserialize<ReferenceCard>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (ArgumentException) { throw new InvalidDataException("Ogiltig lokal referensbricka. Kontakta IT."); }
            catch (InvalidOperationException) { throw new InvalidDataException("Ogiltig lokal referensbricka. Kontakta IT."); }
            return Validate(card);
        }
        public static ReferenceCard Validate(ReferenceCard card)
        {
            if (card == null || !Regex.IsMatch(card.MifareHex ?? "", @"\A(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{14}|[0-9A-Fa-f]{20})\z") ||
                !Regex.IsMatch(card.EmHex ?? "", @"\A[0-9A-Fa-f]{10}\z"))
                throw new InvalidDataException("Referensbrickan kräver MIFARE-UID på 4, 7 eller 10 byte och EM på 5 byte i verifierad byteordning.");
            byte[] m = Codec.Hex(card.MifareHex), e = Codec.Hex(card.EmHex);
            if (m.SequenceEqual(m.Reverse()) || e.SequenceEqual(e.Reverse()))
                throw new InvalidDataException("Välj en referensbricka vars byteordning kan bestämmas entydigt för båda teknikerna.");
            card.MifareHex = Codec.ToHex(m); card.EmHex = Codec.ToHex(e);
            return card;
        }
    }
    public sealed class Calibration
    {
        public int Schema { get; set; }
        public string ReaderId { get; set; }
        public string Firmware { get; set; }
        public string MifareOrder { get; set; }
        public string EmOrder { get; set; }
    }
    public sealed class ScanResult
    {
        public string Mr; public string Er; public string RawM; public string RawE;
        public string MOrder; public string EOrder;
    }
    public sealed class LocalLog
    {
        private readonly object gate = new object();
        private readonly Queue<string> lines = new Queue<string>();
        public void Add(string message)
        {
            lock (gate)
            {
                lines.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
                if (lines.Count > 2500) lines.Dequeue();
            }
        }
        public void Clear() { lock (gate) lines.Clear(); }
        public void Transport(bool response, int length)
        {
            Add((response ? "RX" : "TX") + " antal tecken: " + length);
        }
        public string Text()
        {
            lock (gate) return "OMNIKEY RCO v2.0 testversion\r\n" + DateTime.Now.ToString("s") + "\r\n" + String.Join("\r\n", lines.ToArray());
        }
    }
    public sealed partial class ReaderClient
    {
        public const string AskDataOid = "040C0200";
        public static readonly PollSetting[] Polls = new PollSetting[] {
            new PollSetting("ISO14443A", "02020301030100"),
            new PollSetting("ISO14443B", "02020301040100"),
            new PollSetting("ISO15693", "02020301050100"),
            new PollSetting("FeliCa", "02020301060100"),
            new PollSetting("iCLASS14443B", "02020301070100"),
            new PollSetting("iCLASS15693", "02020301070400"),
            new PollSetting("Prox", "020203010A0100") };
        private readonly Uri endpoint;
        private readonly LocalLog log;
        private readonly string directory;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public string ReaderId { get; private set; }
        public string Firmware { get; private set; }
        public string Board { get; private set; }
        public Calibration Cal { get; private set; }
        public bool Connected { get; private set; }
        public string CalibrationIssue { get; private set; }
        public bool CanRead { get { return Connected && String.IsNullOrEmpty(CalibrationIssue); } }
        // Raw PC/SC UID order -> RCO MSB block convention. LF PACS is already MSB first.
        // This is an explicit standard format, not a claimed per-reader calibration.
        internal string MifareOrder { get { return Cal == null ? "Reverse" : Cal.MifareOrder; } }
        internal string EmOrder { get { return Cal == null ? "Forward" : Cal.EmOrder; } }
        internal Func<string, byte[]> TestTransport;
        public string DirectoryPath { get { return directory; } }
        private string JournalPath { get { return Path.Combine(directory, "aterstall-" + ReaderId + ".json"); } }
        private string CalibrationPath { get { return Path.Combine(directory, "byteordning-" + ReaderId + ".json"); } }
        public bool HasPendingRecovery { get { return ReaderId != null && File.Exists(JournalPath); } }
        public ReaderClient(string address, string localDirectory, LocalLog logger)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(address.Trim(), out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Ange läsarens lokala IPv4-adress, normalt 192.168.63.99.");
            byte[] b = ip.GetAddressBytes();
            bool local = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            if (!local) throw new ArgumentException("Endast privat/lokal IPv4-adress tillåts. Inga kortnummer skickas till internet.");
            endpoint = new Uri("http://" + ip.ToString() + "/cjax");
            directory = localDirectory; System.IO.Directory.CreateDirectory(directory); log = logger;
        }
        private byte[] Send(string command)
        {
            log.Transport(false, command.Length);
            if (TestTransport != null) return TestTransport(command);
            string text = ReaderNetwork.Send(IPAddress.Parse(endpoint.Host), command);
            log.Transport(true, text.Length);
            return Codec.HttpResponse(text);
        }

        public Tlv GetMib(string oid) { return Codec.MibResponse(oid, Send(Codec.GetMibCommand(oid))); }
        private static bool Allowed(string oid) { return oid == AskDataOid || Polls.Any(delegate(PollSetting p) { return p.Oid == oid; }); }
        private static void ValidateSaved(SavedSetting s)
        {
            if (s == null || !Allowed(s.Oid)) throw new InvalidOperationException("Säkerhetskopian innehåller en inställning som programmet inte får ändra.");
            byte[] v = Codec.Hex(s.HexValue);
            if (s.Oid == AskDataOid)
            {
                if (s.Tag != 4 || v.Length != 8) throw new InvalidOperationException("Fel längd/typ för ASK-inställningen.");
            }
            else if (s.Tag != 1 || v.Length != 1 || (v[0] != 0 && v[0] != 1 && v[0] != 255))
                throw new InvalidOperationException("Fel längd/typ för polling-inställningen.");
        }
        private void SetMib(SavedSetting s)
        {
            ValidateSaved(s);
            byte[] v = Codec.Hex(s.HexValue);
            Tlv answer = Codec.MibResponse(s.Oid, Send(Codec.SetMibCommand(s.Oid, (byte)s.Tag, v)));
            if (answer.Tag != s.Tag || !answer.Value.SequenceEqual(v)) throw new IOException("Läsaren bekräftade inte ändringen av " + s.Oid + ".");
        }
        private void Apply()
        {
            log.Add("APPLY: konfigurationen sparas till läsarens flashminne.");
            byte[] result = Codec.Success(Send("FF6800010101"));
            if (result.Length != 0) throw new FormatException("Oväntade data efter Apply Changes.");
            Thread.Sleep(350);
        }
        public void Connect()
        {
            Connected = false; Cal = null; CalibrationIssue = null;
            Tlv board = GetMib("010200");
            if (board.Tag != 2 || board.Value.Length != 1 || (board.Value[0] != 6 && board.Value[0] != 10))
                throw new NotSupportedException("Programmet stöder OMNIKEY 5427 CK och 5427 CK Gen2. Den anslutna modellen stöds inte.");
            Board = board.Value[0] == 6 ? "OMNIKEY 5427 CK (06)" : "OMNIKEY 5427 CK Gen2 (0A)";
            Tlv fw = GetMib("010100");
            if (fw.Tag != 2 || fw.Value.Length != 4) throw new FormatException("Oväntad firmwareidentifiering.");
            Firmware = Codec.ToHex(fw.Value);
            Tlv id = GetMib("010A00");
            if (id.Tag != 4 || id.Value.Length != 16) throw new FormatException("Läsarens unika ID kunde inte verifieras.");
            ReaderId = Codec.ToHex(id.Value);
            // Read-only preflight. Do not write unless every affected setting can be backed up.
            ReadSettings(); ReadAtr();
            if (File.Exists(CalibrationPath))
            {
                try
                {
                    FileInfo file = new FileInfo(CalibrationPath);
                    if (file.Length == 0 || file.Length > 16384) throw new InvalidDataException();
                    Calibration c = json.Deserialize<Calibration>(File.ReadAllText(CalibrationPath, Encoding.UTF8));
                    if (!ReceptionFiles.ValidCalibration(c) || c.ReaderId != ReaderId || c.Firmware != Firmware)
                        throw new InvalidDataException();
                    Cal = c;
                }
                catch (Exception ex)
                {
                    // A damaged/stale override must not silently change existing installations' numbers.
                    CalibrationIssue = "Den sparade läsarprofilen är ogiltig eller gäller annan firmware. Be IT importera rätt profil eller göra ett nytt referensprov.";
                    log.Add("Lokal läsarprofil behöver kontrolleras: " + ex.GetType().Name);
                }
            }
            Connected = true;
            log.Add("Ansluten: " + Board + ", FW " + Firmware + ", ID " + ReaderId);
        }
        private List<SavedSetting> ReadSettings()
        {
            List<SavedSetting> result = new List<SavedSetting>();
            List<string> oids = Polls.Select(delegate(PollSetting p) { return p.Oid; }).ToList(); oids.Add(AskDataOid);
            foreach (string oid in oids)
            {
                Tlv t = GetMib(oid); SavedSetting s = new SavedSetting { Oid = oid, Tag = t.Tag, HexValue = Codec.ToHex(t.Value) };
                ValidateSaved(s); result.Add(s);
            }
            return result;
        }
        private void AtomicWrite(string path, string data)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, data, new UTF8Encoding(true));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        private Journal TakeBackup()
        {
            if (HasPendingRecovery) throw new InvalidOperationException("En tidigare körning behöver återställas. Välj Återställ läsaren innan ny skanning.");
            Journal j = new Journal { Schema = 2, ReaderId = ReaderId, Created = DateTime.Now.ToString("s"), Settings = ReadSettings() };
            // Persist recovery before the first mutation. Never overwrite an outstanding journal.
            AtomicWrite(JournalPath, json.Serialize(j));
            string archive = Path.Combine(directory, "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            File.Copy(JournalPath, archive, false);
            return j;
        }
        private void CheckIdentity()
        {
            if (Codec.ToHex(GetMib("010A00").Value) != ReaderId) throw new IOException("En annan läsare är ansluten. Återställning stoppad för att skydda dess inställningar.");
        }
        public void Recover()
        {
            if (!HasPendingRecovery) return;
            CheckIdentity();
            Journal j = json.Deserialize<Journal>(File.ReadAllText(JournalPath, Encoding.UTF8));
            if (j == null || j.Schema != 2 || j.ReaderId != ReaderId || j.Settings == null || j.Settings.Count != Polls.Length + 1 ||
                j.Settings.Select(delegate(SavedSetting s) { return s.Oid; }).Distinct().Count() != j.Settings.Count)
                throw new InvalidOperationException("Säkerhetskopian är ogiltig eller tillhör en annan läsare.");
            foreach (SavedSetting s in j.Settings) ValidateSaved(s);
            List<string> errors = new List<string>();
            foreach (SavedSetting s in j.Settings)
            {
                try { SetMib(s); } catch (Exception e) { errors.Add(s.Oid + ": " + e.Message); }
            }
            try { Apply(); } catch (Exception e) { errors.Add("Apply: " + e.Message); }
            foreach (SavedSetting s in j.Settings)
            {
                try
                {
                    Tlv current = GetMib(s.Oid);
                    if (current.Tag != s.Tag || Codec.ToHex(current.Value) != s.HexValue) errors.Add(s.Oid + " matchar inte säkerhetskopian.");
                }
                catch (Exception e) { errors.Add(s.Oid + ": " + e.Message); }
            }
            if (errors.Count != 0) throw new IOException("Återställningen kunde inte verifieras. Behåll programmets data och anslut samma läsare igen. " + String.Join(" | ", errors.ToArray()));
            File.Delete(JournalPath);
            BackupRetention.Prune(directory);
            log.Add("Återställning verifierad för samtliga ändrade inställningar.");
        }
        private void SetMode(bool mifare)
        {
            CheckIdentity();
            string target = mifare ? "ISO14443A" : "Prox";
            foreach (PollSetting p in Polls)
                SetMib(new SavedSetting { Oid = p.Oid, Tag = 1, HexValue = p.Name == target ? "FF" : "00" });
            if (!mifare) SetMib(new SavedSetting { Oid = AskDataOid, Tag = 4, HexValue = "0101000000000000" });
            Apply();
            foreach (PollSetting p in Polls)
            {
                Tlv actual = GetMib(p.Oid);
                bool expected = p.Name == target;
                if (actual.Tag != 1 || actual.Value.Length != 1 || (actual.Value[0] != 0) != expected)
                    throw new IOException("Polling-växlingen kunde inte verifieras för " + p.Name + ".");
            }
            log.Add("Läge verifierat: " + target);
        }
        private byte[] ReadAtr()
        {
            Tlv t = GetMib("0302010300");
            if (t.Tag != 4) throw new FormatException("Oväntad datatyp för kortstatus.");
            return Codec.Atr(t.Value);
        }
        private void WaitForPresence(bool wanted, CancellationToken token, Action<string> status, string message)
        {
            status(message); DateTime deadline = DateTime.UtcNow.AddSeconds(60); int stable = 0;
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                bool present = ReadAtr().Length > 0;
                stable = present == wanted ? stable + 1 : 0;
                if (stable >= 2) return;
                if (token.WaitHandle.WaitOne(350)) token.ThrowIfCancellationRequested();
            }
            throw new TimeoutException(wanted ? "Ingen ny bricka kunde läsas. Lyft brickan helt och försök igen. Spara diagnostiken om EM inte upptäcks." : "Läsaren rapporterar fortfarande en bricka. Ta bort alla brickor. Om fältet är tomt kan firmware rapportera kvarliggande kortstatus; spara diagnostiken.");
        }
        private byte[] ReadCard(bool mifare, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ReadAtr().Length == 0) throw new IOException("Brickan togs bort för tidigt.");
            byte[] first = mifare ? Codec.Success(Send("FFCA000000")) : Codec.EmPacs(Send("FF70076B07A005A10380010400"));
            if (mifare && first.Length != 4 && first.Length != 7 && first.Length != 10) throw new FormatException("MIFARE-UID har inte 4, 7 eller 10 byte.");
            if (token.WaitHandle.WaitOne(180)) token.ThrowIfCancellationRequested();
            byte[] second = mifare ? Codec.Success(Send("FFCA000000")) : Codec.EmPacs(Send("FF70076B07A005A10380010400"));
            if (ReadAtr().Length == 0 || !first.SequenceEqual(second)) throw new IOException("Instabil avläsning eller brickan byttes. Gör om med en bricka i taget.");
            return first;
        }
        public ScanResult Scan(string mode, bool reference, CancellationToken token, Action<string> status)
        {
            if (mode != "Both" && mode != "M" && mode != "E") throw new ArgumentException("Ogiltigt läge.");
            if (reference && mode != "Both") throw new ArgumentException("Referensprovet ska läsa båda teknikerna.");
            // Validate local reference data before any scan I/O, backup or mutation.
            ReferenceCard expected = reference ? ReferenceCard.Read(Path.Combine(directory, ReferenceCard.FileName)) : null;
            if (!Connected || (!reference && !CanRead)) throw new InvalidOperationException(CalibrationIssue ?? "Anslut läsaren före skanning.");
            WaitForPresence(false, token, status, "Ta bort alla brickor från läsaren. Programmet förbereder avläsningen.");
            TakeBackup();
            ScanResult result = new ScanResult(); Exception failure = null;
            try
            {
                if (mode == "Both" || mode == "M")
                {
                    status("Ställer in MIFARE. Håll brickan borta från läsaren."); SetMode(true);
                    WaitForPresence(true, token, status, reference ? "Steg 1 av 2: Lägg referensbrickan mot läsaren och håll kvar." : "Lägg brickan mot läsaren och håll kvar. Läser MIFARE / mR.");
                    byte[] m = ReadCard(true, token);
                    result.MOrder = reference ? Codec.ReferenceOrder(m, expected.MifareHex) : MifareOrder;
                    result.RawM = Codec.ToHex(m); result.Mr = Codec.Rco9(Codec.Order(m, result.MOrder));
                    WaitForPresence(false, token, status, "MIFARE läst. Lyft brickan helt från läsaren. Använd samma fysiska bricka i nästa steg.");
                }
                if (mode == "Both" || mode == "E")
                {
                    token.ThrowIfCancellationRequested();
                    status("Växlar automatiskt till EM. Håll brickan borta från läsaren."); SetMode(false);
                    WaitForPresence(true, token, status, reference ? "Steg 2 av 2: Lägg SAMMA referensbricka mot läsaren och håll kvar." : "Lägg SAMMA bricka mot läsaren och håll kvar. Läser EM / eR.");
                    byte[] e = ReadCard(false, token);
                    result.EOrder = reference ? Codec.ReferenceOrder(e, expected.EmHex) : EmOrder;
                    result.RawE = Codec.ToHex(e); result.Er = Codec.Rco9(Codec.Order(e, result.EOrder));
                    WaitForPresence(false, token, status, "EM läst. Lyft brickan så återställs läsaren till sina ursprungliga inställningar.");
                }
                token.ThrowIfCancellationRequested();
                if (reference && (result.Mr != Codec.Rco9(Codec.Hex(expected.MifareHex)) || result.Er != Codec.Rco9(Codec.Hex(expected.EmHex))))
                    throw new IOException("Referensprovet misslyckades. Inga kopieringsbara resultat lämnas.");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                status("Återställer läsarens tidigare inställningar. Låt USB-kabeln sitta kvar och håll brickan borta.");
                try { Recover(); }
                catch (Exception restore)
                {
                    failure = new IOException((failure == null ? "" : failure.Message + "\r\n") + "VIKTIGT: " + restore.Message, restore);
                }
            }
            if (failure != null) throw failure;
            if (reference)
            {
                Calibration verified = new Calibration { Schema = 2, ReaderId = ReaderId, Firmware = Firmware, MifareOrder = result.MOrder, EmOrder = result.EOrder };
                AtomicWrite(CalibrationPath, json.Serialize(verified));
                Cal = verified;
                CalibrationIssue = null;
                log.Add("Referensprov godkänt. Endast byteordning per läsare/firmware sparad; inget brickregister.");
            }
            return result;
        }
    }
    internal static class BackupRetention
    {
        internal const int Keep = 20;
        internal static void Prune(string directory)
        {
            try
            {
                if (Directory.GetFiles(directory, "aterstall-*.json").Length != 0) return;
                string[] files = Directory.GetFiles(directory, "backup-*.json")
                    .Where(delegate(string path) { return Regex.IsMatch(Path.GetFileName(path), @"\Abackup-[0-9]{8}-[0-9]{6}-[0-9]{3}\.json\z"); })
                    .OrderByDescending(delegate(string path) { return Path.GetFileName(path); }, StringComparer.Ordinal).ToArray();
                foreach (string file in files.Skip(Keep))
                {
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public static class SelfTests
    {
        private static int count;
        private static void Equal(string actual, string expected)
        {
            count++; if (actual != expected) throw new Exception("Självtest " + count + " misslyckades: " + actual + " != " + expected);
        }
        private static void Reject(Action action)
        {
            count++; bool rejected = false; try { action(); } catch { rejected = true; }
            if (!rejected) throw new Exception("Självtest " + count + " accepterade felaktiga data.");
        }
        public static int Run()
        {
            count = 0;
            // Constructed byte sequences; these are not identifiers from a real card.
            Equal(Codec.Rco9(Codec.Hex("01020304")), "025800772");
            Equal(Codec.Rco9(Codec.Hex("0102030405")), "051501029");
            Equal(Codec.Rco9(Codec.Hex("01020304050607")), "102901543");
            Equal(Codec.Rco9(Codec.Hex("00000000")), "000000000");
            Equal(Codec.Rco9(Codec.Hex("FFFFFFFF")), "553565535");
            Equal(Codec.GetMibCommand("010100"), "FF6800000A0000000D03010100050000");
            Equal(Codec.GetMibCommand("02020301030100"), "FF6800000E0000000D0702020301030100050000");
            Equal(Codec.SetMibCommand("02020301030100", 1, new byte[] { 255 }), "FF6800000F0200000D07020203010301000101FF00");
            Equal(Codec.GetMibCommand("0302010300"), "FF6800000C0000000D050302010300050000");
            Equal(Codec.SetMibCommand("040C0200", 4, Codec.Hex("0101000000000000")), "FF680000130200000D04040C02000408010100000000000000");
            Equal(Codec.ToHex(Codec.MibResponse("010100", Codec.Hex("0300000D030101000204010501769000")).Value), "01050176");
            Equal(Codec.ToHex(Codec.EmPacs(Codec.Hex("9D0803060001020304059000"))), "0102030405");
            Equal(Codec.ReferenceOrder(Codec.Hex("04030201"), "01020304"), "Reverse");
            Equal(Codec.ReferenceOrder(Codec.Hex("0102030405"), "0102030405"), "Forward");
            Equal(Codec.ToHex(Codec.Atr(Encoding.ASCII.GetBytes("3B8F00\0"))), "3B8F00");
            Equal(Codec.ToHex(Codec.HttpResponse(" 90 00\r\n")), "9000");
            Equal(Codec.ToHex(Codec.HttpResponse("\"9000\"")), "9000");
            Reject(delegate { Codec.EmPacs(Codec.Hex("03060001020304059000")); });
            Reject(delegate { Codec.EmPacs(Codec.Hex("9D07030506010203049000")); });
            Reject(delegate { Codec.Success(Codec.Hex("6F00")); });
            Reject(delegate { Codec.MibResponse("010200", Codec.Hex("0300000D030101000204010501769000")); });
            Reject(delegate { Codec.MibResponse("010100", Codec.Hex("0302010D0301010005009000")); });
            Reject(delegate { Codec.ReferenceOrder(Codec.Hex("12345678"), "01020304"); });
            Reject(delegate { Codec.HttpResponse("<html>error 9000</html>"); });
            Reject(delegate { Codec.Hex("123"); });
            Reject(delegate { Codec.Rco9(Codec.Hex("00")); });
            return count;
        }
    }
}
