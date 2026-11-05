// Reception v3.6.4 (PowerShell). Reader protocol remains in OmnikeyCore.cs.
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Web.Script.Serialization;

    public enum ConnectionPhase { None, IdentifyReader, ProtectUsbOutput, ImportLocalProfile, ReadSoundState, SavePreferences }

    // Only fixed codes and known source method names enter a shared report. No raw
    // exception messages, stack traces, file paths, HTTP headers or APDU bytes.
    [Serializable]
    internal sealed class UsbModeShape
    {
        internal readonly string Type;
        internal readonly int Length;
        internal readonly string Value;
        internal UsbModeShape(Tlv mode)
        {
            Type = mode == null ? "Missing" : mode.Tag == 1 ? "Boolean" : mode.Tag == 2 ? "Integer" : mode.Tag == 4 ? "OctetString" : "Other";
            Length = mode == null || mode.Value == null ? -1 : mode.Value.Length;
            // Only three fixed, non-identifying mode values are described. Other
            // payloads stay private, even if a faulty device returns unrelated data.
            int value = Length == 1 ? mode.Value[0] : Length == 2 && mode.Value[0] == 0 ? mode.Value[1] : -1;
            Value = value == 0 ? "00" : value == 1 ? "01" : value == 255 ? "FF" : "Other";
        }
    }

    internal static class ReaderDiagnostics
    {
        internal static string Inspect(ReaderClient reader)
        {
            StringBuilder report = new StringBuilder("BRICKLÄSAREN – ANSLUTNINGSKONTROLL 1\r\n");
            report.Append("Tid: ").Append(DateTime.Now.ToString("s")).Append("\r\n");
            report.Append("Kontrollen läser inställningar. Den ändrar inte läsaren.\r\n");
            report.Append("Inga bricknummer, läsar-ID, råsvar eller sökvägar ingår.\r\n\r\n");
            try { reader.Connect(); report.Append("IdentifyReader: OK\r\n"); }
            catch (Exception error) { report.Append("IdentifyReader: ").Append(ReceptionFiles.SafeError(error)).Append("\r\n"); return report.ToString(); }
            try
            {
                bool mode = ReaderSafety.IsCustomReport(reader);
                report.Append("UsbOutputMode: ").Append(mode ? "Custom Report" : "Tangentbordsläge").Append("\r\n");
            }
            catch (Exception error) { report.Append("UsbOutputMode: ").Append(ReceptionFiles.SafeError(error)).Append("\r\n"); }
            try { report.Append(ReaderSafety.InspectSavedState(reader)).Append("\r\n"); }
            catch (Exception error) { report.Append("SavedUsbState: ").Append(ReceptionFiles.SafeError(error)).Append("\r\n"); }
            report.Append("ScanRecovery: ").Append(reader.HasPendingRecovery ? "Väntar" : "Ingen").Append("\r\n");
            report.Append("LocalProfile: ").Append(reader.CalibrationIssue != null ? "Behöver kontrolleras" : reader.Cal != null ? "Godkänd" : "Standardformat").Append("\r\n");
            try { report.Append(reader.InspectReaderSound()).Append("\r\n"); }
            catch (Exception error) { report.Append("ReaderSound: ").Append(ReceptionFiles.SafeError(error)).Append("\r\n"); }
            return report.ToString();
        }
        internal static string Code(Exception error)
        {
            if (error is ReaderMibException) return "MIB_REJECTED";
            switch (error.Message)
            {
                case "Läsaren rapporterar ett okänt USB-läge. Förberedelsen kan inte godkännas.": return "USB_MODE_RESPONSE";
                case "Läsarens identitet eller firmware kunde inte verifieras. Åtgärden avbröts.": return "USB_IDENTITY_RESPONSE";
                case "En annan läsare är ansluten. USB-ändringen avbröts för att skydda dess inställningar.": return "USB_IDENTITY_CHANGED";
                case "Läsarens firmware har ändrats. USB-ändringen avbröts; kontakta IT.": return "USB_FIRMWARE_CHANGED";
                case "USB-originalinställningen är ogiltig eller gäller en annan läsare eller firmware. Kontakta IT.": return "USB_BACKUP_INVALID";
                case "Originalinställningen för USB-läget saknas. Kontakta IT; ingen inställning ändras.": return "USB_BACKUP_MISSING";
                case "Den avbrutna USB-förberedelsen kunde inte verifieras. Behåll konfigurationsfilerna och kontakta IT.": return "USB_PENDING_INVALID";
                case "USB-konfigurationsfilen är för stor.": return "USB_STATE_SIZE";
                case "USB-konfigurationsfilen kunde inte läsas.": return "USB_STATE_JSON";
                case "Läsarens ljudformat stöds inte.": return "SOUND_TABLE_FORMAT";
                case "Läsaren kunde inte lämna sitt ljudläge.": return "SOUND_TABLE_TYPE";
                case "Den sparade ljudinställningen är ogiltig.": return "SOUND_BACKUP_SIZE";
                case "Ljudinställningen gäller inte den här läsaren och firmware.": return "SOUND_BACKUP_IDENTITY";
                case "För stort HTTP-huvud från läsaren.": return "HTTP_HEADER_SIZE";
                case "Avklippt HTTP-svar från läsaren.": return "HTTP_LINE_TRUNCATED";
                case "Ogiltigt HTTP-svar från läsaren.": return "HTTP_LINE_FORMAT";
                case "Läsaren svarade inte OK.": return "HTTP_STATUS";
                case "Ogiltigt HTTP-huvud.": return "HTTP_HEADER_FORMAT";
                case "Dubbelt HTTP-huvud.": return "HTTP_HEADER_DUPLICATE";
                case "Oväntad HTTP-kodning från läsaren.": return "HTTP_ENCODING";
                case "För stort eller ogiltigt läsarsvar.": return "HTTP_CHUNK_SIZE";
                case "Ogiltigt HTTP-slut från läsaren.": return "HTTP_TRAILER";
                case "Avklippt HTTP-del från läsaren.": return "HTTP_CHUNK_TRUNCATED";
                case "För stort läsarsvar.": return "HTTP_BODY_SIZE";
                case "Avklippt läsarsvar.": return "HTTP_BODY_TRUNCATED";
                case "Ogiltig textkodning i läsarsvaret.": return "HTTP_TEXT_ENCODING";
                default: return null;
            }
        }
        internal static string Source(Exception error)
        {
            List<string> result = new List<string>();
            System.Diagnostics.StackFrame[] frames = new System.Diagnostics.StackTrace(error, false).GetFrames();
            if (frames == null) return "";
            foreach (System.Diagnostics.StackFrame frame in frames)
            {
                System.Reflection.MethodBase method = frame.GetMethod();
                if (method == null) continue;
                Type owner = method.DeclaringType;
                if (owner != typeof(ReaderSafety) && owner != typeof(ReaderNetwork) && owner != typeof(ReaderClient) &&
                    owner != typeof(Codec) && owner != typeof(ReceptionFiles)) continue;
                // Ignore generated closure names. This is an identifier from the
                // shipped program, never a path, argument value or exception text.
                if (!Regex.IsMatch(method.Name, @"\A[A-Za-z][A-Za-z0-9_]{0,63}\z")) continue;
                string entry = owner.Name + "." + method.Name;
                if (!result.Contains(entry)) result.Add(entry);
                if (result.Count == 4) break;
            }
            return String.Join(" > ", result.ToArray());
        }
        internal static string MibName(string oid)
        {
            switch (oid)
            {
                case "010200": return "Board";
                case "010100": return "Firmware";
                case "010A00": return "ReaderIdentity";
                case "04010500": return "UsbOutputMode";
                case "0302010300": return "CardPresence";
                case "040C0200": return "AskDataSelection";
                case "02020104010100": return "IndicatorSequences";
                case "02020301030100": case "02020301040100": case "02020301050100":
                case "02020301060100": case "02020301070100": case "02020301070400":
                case "020203010A0100": return "Polling";
                default: return "Other";
            }
        }
    }

    public sealed class ReceptionConfig
    {
        public string Address { get; set; }
    }
    public sealed class ReceptionProfile
    {
        public int Schema { get; set; }
        public string Address { get; set; }
        public Calibration Calibration { get; set; }
    }
    public static class ReceptionFiles
    {
        // Deliberately identical to v2: reuse VERIFIED byte order and pending recovery.
        public static readonly string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tobtech", "Omnikey-RCO-v2");
        public const string ProfileName = "lasarprofil.json";
        public static string PackagePath = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(true);

        public const string LauncherName = "Starta-Bricklasaren.cmd";
        public static string[] RequiredPackageFiles
        {
            get
            {
                return new string[] {
                    LauncherName,
                    "Program/Starta-Bricklasaren.ps1", "Program/Bricklasaren.ico",
                    "Program/Entry.cs", "Program/OmnikeyCore.cs", "Program/ReaderSound.cs", "Program/ReaderNetwork.cs", "Program/ReaderNetworkTests.cs",
                    "Program/FirstStart.cs", "Program/Appearance.cs", "Program/SettingsDialogs.cs", "Program/ReliabilityTests.cs",
                    "Program/ReceptionSupport.cs", "Program/ReaderSafety.cs", "Program/ReceptionApp.cs",
                    "START-HAR.txt", "Snabbstart.txt",
                    "IT/LAS-MIG.txt", "IT/Ljud.md", "IT/Starta-med-felsokning.cmd",
                    "IT/Kontrollera-anslutning.cmd", "IT/Anslutningsdiagnostik.md",
                    "IT/Forsta-start.md", "IT/Utseende.md", "IT/Kor-sjalvtester.cmd", "IT/Verifiering.txt", "IT/Referensbricka.md",
                    "Drivrutiner/LAS-MIG.txt", "LICENSE.txt", "CONTRIBUTING.md",
                    "Program/Workflow.cs", "Program/Updates.cs", "Program/Install-Update.ps1",
                    "Assets/Lucide/volume-2.png", "Assets/Lucide/volume-off.png",
                    "Assets/Lucide/volume-2.svg", "Assets/Lucide/volume-off.svg",
                    "Assets/Lucide/LICENSE.txt", "Assets/Lucide/SOURCE.txt"
                };
            }
        }
        public static void CopyPackageFiles(string source, string destination)
        {
            // Validate everything before creating the destination. Existing folders
            // are not overwritten; this also rejects a source=destination mistake.
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException("Målmappen finns redan. Välj en ny undermapp.");
            string[] files = RequiredPackageFiles;
            foreach (string relative in files)
            {
                string file = Path.Combine(source, relative);
                if (!File.Exists(file))
                    throw new FileNotFoundException("Programfil saknas: " + relative + ". Packa upp hela ZIP-filen.", file);
            }
            Directory.CreateDirectory(destination);
            foreach (string relative in files)
            {
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(source, relative), target, false);
            }
        }

        public static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, text, Utf8);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static bool ValidCalibration(Calibration c)
        {
            return c != null && c.Schema == 2 &&
                Regex.IsMatch(c.ReaderId ?? "", "\\A[0-9A-F]{32}\\z") &&
                Regex.IsMatch(c.Firmware ?? "", "\\A[0-9A-F]{8}\\z") &&
                (c.MifareOrder == "Forward" || c.MifareOrder == "Reverse") &&
                (c.EmOrder == "Forward" || c.EmOrder == "Reverse");
        }
        public static ReceptionProfile ReadProfile(string path)
        {
            FileInfo f = new FileInfo(path);
            if (!f.Exists || f.Length > 16384) throw new InvalidDataException("Läsarprofilen saknas eller är för stor.");
            ReceptionProfile p = new JavaScriptSerializer().Deserialize<ReceptionProfile>(File.ReadAllText(path, Encoding.UTF8));
            if (p == null || p.Schema != 1 || !ValidCalibration(p.Calibration))
                throw new InvalidDataException("Läsarprofilen är inte giltig.");
            // Use the same local-IPv4 validation as the tested reader client.
            new ReaderClient(p.Address ?? "", DataPath, new LocalLog());
            return p;
        }
        public static string LoadAddress(LocalLog log)
        {
            string local = Path.Combine(DataPath, "reception.json");
            try
            {
                if (File.Exists(local))
                {
                    ReceptionConfig c = new JavaScriptSerializer().Deserialize<ReceptionConfig>(File.ReadAllText(local, Encoding.UTF8));
                    if (c != null)
                    {
                        new ReaderClient(c.Address ?? "", DataPath, log);
                        return c.Address;
                    }
                }
            }
            catch (Exception ex) { log.Add("Ignorerar ogiltig lokal UI-inställning: " + ex.GetType().Name); }
            try
            {
                string path = Path.Combine(PackagePath, ProfileName);
                if (File.Exists(path)) return ReadProfile(path).Address;
            }
            catch (Exception ex) { log.Add("Läsarprofilens adress kunde inte läsas: " + ex.GetType().Name); }
            return "192.168.63.99";
        }
        public static FormatPreference LoadFormatPreference()
        {
            // A package setting must never stand in for this Windows user's
            // first choice. Existing valid local v3.2 settings remain compatible.
            return LoadFormatPreference(Path.Combine(DataPath, "formatval.json"));
        }
        public static FormatPreference LoadFormatPreference(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentException("En sökväg till formatvalet krävs.", "path");
            try
            {
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length == 0 || file.Length > 16384) return null;
                FormatPreference value = new JavaScriptSerializer().Deserialize<FormatPreference>(File.ReadAllText(path, Encoding.UTF8));
                if (value == null || !FormatProfiles.ValidMode(value.Mode) || !FormatProfiles.ValidChip(value.RcoChip)) return null;
                return value;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public static void SaveFormatPreference(string mode, string chip)
        {
            SaveFormatPreference(Path.Combine(DataPath, "formatval.json"), mode, chip);
        }
        public static void SaveFormatPreference(string path, string mode, string chip)
        {
            if (!FormatProfiles.ValidMode(mode) || !FormatProfiles.ValidChip(chip)) throw new InvalidDataException("Ogiltigt formatval.");
            // Save both fields together as soon as the choice is confirmed.
            // WriteAtomic preserves the prior file if replacement cannot finish.
            WriteAtomic(path, new JavaScriptSerializer().Serialize(new FormatPreference { Mode = mode, RcoChip = chip }));
        }
        public static void SaveAddress(string address)
        {
            new ReaderClient(address, DataPath, new LocalLog());
            WriteAtomic(Path.Combine(DataPath, "reception.json"), new JavaScriptSerializer().Serialize(new ReceptionConfig { Address = address }));
        }
        public static void ImportProfile(ReaderClient reader, string path)
        {
            ReceptionProfile p = ReadProfile(path);
            if (p.Calibration.ReaderId != reader.ReaderId || p.Calibration.Firmware != reader.Firmware)
                throw new InvalidDataException("Profilen gäller inte den anslutna läsaren och dess firmware. Inga inställningar importerades.");
            if (reader.HasPendingRecovery)
                throw new InvalidOperationException("Återställ läsaren innan en profil importeras.");
            WriteAtomic(Path.Combine(DataPath, "byteordning-" + reader.ReaderId + ".json"), new JavaScriptSerializer().Serialize(p.Calibration));
        }
        public static string ProfileText(ReaderClient reader, string address)
        {
            if (reader == null || !ValidCalibration(reader.Cal) || reader.Cal.ReaderId != reader.ReaderId || reader.Cal.Firmware != reader.Firmware || reader.HasPendingRecovery)
                throw new InvalidOperationException("Läsaren måste ha ett godkänt referensprov och vara återställd innan paketet kan skapas.");
            return new JavaScriptSerializer().Serialize(new ReceptionProfile { Schema = 1, Address = address, Calibration = reader.Cal });
        }
        // Error summaries exclude exception messages and tag identifiers.
        // The bounded in-memory transport log also excludes raw TX/RX data.
        private static readonly object errorGate = new object();
        private static readonly List<string> errorSummaries = new List<string>();
        public static string ConnectionReport(string packagePath)
        {
            PackagePath = Path.GetFullPath(packagePath);
            LocalLog log = new LocalLog();
            return ReaderDiagnostics.Inspect(new ReaderClient(LoadAddress(log), DataPath, log));
        }
        public static string SafeError(Exception error)
        {
            if (error == null) return "Ingen felinformation.";
            StringBuilder text = new StringBuilder();
            int depth = 0;
            for (Exception current = error; current != null && depth < 8; current = current.InnerException, depth++)
            {
                if (depth > 0) text.Append(" -> ");
                text.Append(current.GetType().FullName).Append(" [HRESULT 0x")
                    .Append(current.HResult.ToString("X8")).Append("]");
                string code = ReaderDiagnostics.Code(current), source = ReaderDiagnostics.Source(current);
                if (code != null) text.Append(" [orsak ").Append(code).Append("]");
                if (source.Length != 0) text.Append(" [steg ").Append(source).Append("]");
                UsbModeShape shape = current.Data["Tobtech.UsbModeShape"] as UsbModeShape;
                if (code == "USB_MODE_RESPONSE" && shape != null) text.Append(" [USB-format ").Append(shape.Type)
                    .Append("; längd ").Append(shape.Length).Append("; värde ").Append(shape.Value).Append("]");
                ReaderMibException mib = current as ReaderMibException;
                if (mib != null) text.Append(" [kommando ").Append(mib.Command).Append("; MIB-status ")
                    .Append(mib.Status).Append("; index ").Append(mib.Index).Append("]");
                ReaderStatusException status = current as ReaderStatusException;
                if (status != null) text.Append(" [läsarstatus ").Append(status.StatusWord.ToString("X4")).Append("]");
                System.Net.WebException network = current as System.Net.WebException;
                if (network != null) text.Append(" [nätverksstatus ").Append(network.Status.ToString()).Append("]");
            }
            return text.ToString();
        }
        public static void RecordFailure(Exception error, ConnectionPhase phase = ConnectionPhase.None)
        {
            lock (errorGate)
            {
                string context = phase != ConnectionPhase.None && Enum.IsDefined(typeof(ConnectionPhase), phase)
                    ? " [anslutningsfas " + phase.ToString() + "]" : "";
                errorSummaries.Add(DateTime.Now.ToString("s") + context + " " + SafeError(error));
                if (errorSummaries.Count > 100) errorSummaries.RemoveAt(0);
            }
        }
        public static string Report()
        {
            lock (errorGate)
            {
                return "BRICKLÄSAREN RECEPTION 3.6.4 – POWERSHELL\r\n" +
                    "Läsarmotor: v3.6.4, standardformat med frivillig lokal IT-kalibrering.\r\n" +
                    "Rapport skapad: " + DateTime.Now.ToString("s") + "\r\n" +
                    "Diagnostik: anslutning 1.\r\n" +
                    "Inga skannade nummer, råsvar, sökvägar eller undantagsmeddelanden ingår.\r\n\r\n" +
                    (errorSummaries.Count == 0 ? "Inga fel registrerade under denna programkörning." : String.Join("\r\n", errorSummaries.ToArray()));
            }
        }
    }
    public sealed class ReceptionStep
    {
        public string Eyebrow;
        public string Title;
        public string Hint;
        public ReceptionStep(string eyebrow, string title, string hint) { Eyebrow = eyebrow; Title = title; Hint = hint; }
    }
    // Display-only format selection. No card identifiers or scan history are saved.
    public sealed class FormatPreference
    {
        public string Mode { get; set; }
        public string RcoChip { get; set; }
    }
    public static class FormatProfiles
    {
        public const string Combo = "Kombi";
        public const string Rco = "RCO";
        public const string Axema = "AxemaVAKA";
        public const string Bewator = "Bewator13";
        public static bool ValidMode(string v) { return v == Combo || v == Rco || v == Axema || v == Bewator; }
        public static bool ValidChip(string v) { return v == "M" || v == "E"; }
        public static string Label(string v)
        {
            if (v == Rco) return "RCO · kort och lång ID-kod";
            if (v == Axema) return "Axema VAKA · EM";
            if (v == Bewator) return "Bewator 2010/Omnis · EM";
            return "RCO – MIFARE + EM (mR + eR)";
        }
        private static string NormalizedHex(ScanResult result, string chip)
        {
            if (result == null) throw new ArgumentNullException("result");
            string raw = chip == "M" ? result.RawM : result.RawE;
            string order = chip == "M" ? result.MOrder : result.EOrder;
            if (String.IsNullOrEmpty(raw)) throw new InvalidDataException("Uppgifter för vald RFID-teknik saknas.");
            return Codec.ToHex(Codec.Order(Codec.Hex(raw), order));
        }
        private static byte[] Normalized(ScanResult result, string chip) { return Codec.Hex(NormalizedHex(result, chip)); }
        public static string RcoLong(byte[] uid)
        {
            if (uid == null || uid.Length < 4) throw new InvalidDataException("RCO kräver minst fyra UID-byte.");
            int i = uid.Length - 4;
            int high = (uid[i] << 8) + uid[i + 1];
            int low = (uid[i + 2] << 8) + uid[i + 3];
            return high.ToString("D5", System.Globalization.CultureInfo.InvariantCulture) + low.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
        }
        public static string RcoLong(ScanResult result, string chip) { return RcoLong(Normalized(result, chip)); }
        public static string RcoShort(ScanResult result, string chip) { return RcoLong(result, chip).Substring(1); }
        public static string Vaka14(ScanResult result)
        {
            byte[] em = Normalized(result, "E");
            if (em.Length != 5) throw new InvalidDataException("Axema VAKA kräver EM 40-bit.");
            ulong value = 0;
            foreach (byte b in em) value = (value << 8) | b;
            return value.ToString("D14", System.Globalization.CultureInfo.InvariantCulture);
        }
        public static string Bewator13(ScanResult result)
        {
            byte[] em = Normalized(result, "E");
            if (em.Length != 5) throw new InvalidDataException("Bewator 13 kräver EM 40-bit.");
            uint low = ((uint)em[1] << 24) | ((uint)em[2] << 16) | ((uint)em[3] << 8) | em[4];
            return em[0].ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + low.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        }
        public static bool IsDigits(string text, int count)
        {
            return text != null && text.Length == count && Regex.IsMatch(text, "\\A[0-9]+\\z");
        }
    }
    public static class ReceptionText
    {
        // Translate the existing core's progress messages; do not change its commands or timing.
        public static ReceptionStep Step(string message, bool reference)
        {
            string card = reference ? "referensbrickan" : "brickan";
            if (message.StartsWith("Ta bort alla brickor", StringComparison.Ordinal))
                return new ReceptionStep("FÖRBEREDER", "Lyft bort brickan", "Läsaren behöver vara tom innan vi börjar.");
            if (message.StartsWith("Ställer in MIFARE", StringComparison.Ordinal))
                return new ReceptionStep("FÖRBEREDER", "Håll brickan borta en stund", "Du får strax en uppmaning att lägga på den.");
            if (message.IndexOf("Steg 1 av 2", StringComparison.Ordinal) >= 0 || message.StartsWith("Lägg brickan mot", StringComparison.Ordinal))
                return new ReceptionStep("STEG 1 AV 2 · EGET PASSERSYSTEM", "Lägg på " + card, "Håll kvar tills du får en ny instruktion.");
            if (message.StartsWith("MIFARE läst.", StringComparison.Ordinal))
                return new ReceptionStep("FÖRSTA NUMRET ÄR LÄST", "Lyft bort brickan", "Behåll samma bricka i handen. Vi ska läsa den en gång till.");
            if (message.StartsWith("Växlar automatiskt", StringComparison.Ordinal))
                return new ReceptionStep("FÖRBEREDER NÄSTA STEG", "Håll brickan borta en stund", "Lägg inte på den förrän nästa instruktion visas.");
            if (message.IndexOf("Steg 2 av 2", StringComparison.Ordinal) >= 0 || message.StartsWith("Lägg SAMMA", StringComparison.Ordinal))
                return new ReceptionStep("STEG 2 AV 2 · HYRESVÄRDEN", "Lägg tillbaka samma bricka", reference ? "Använd referensbrickan och håll kvar." : "Håll kvar tills du får en ny instruktion.");
            if (message.StartsWith("EM läst.", StringComparison.Ordinal))
                return new ReceptionStep("BÅDA NUMREN ÄR LÄSTA", "Lyft bort brickan", "Numren visas när läsningen har avslutats.");
            if (message.StartsWith("Återställer läsarens", StringComparison.Ordinal))
                return new ReceptionStep("SLUTFÖR", "Strax klart", "Håll brickan borta och låt läsaren vara inkopplad.");
            return new ReceptionStep("LÄSNING PÅGÅR", "Följ instruktionerna här", "Låt läsaren vara inkopplad.");
        }
        public static string Both(string mr, string er)
        {
            if (!IsNumber(mr) || !IsNumber(er)) throw new InvalidOperationException("Båda numren måste vara färdiglästa.");
            return "Eget passersystem (mR): " + mr + "\r\nHyresvärdens passersystem (eR): " + er;
        }
        public static bool IsNumber(string number) { return Regex.IsMatch(number ?? "", "\\A[0-9]{9}\\z"); }
        public static string QuickStart
        {
            get
            {
                return "BRICKLÄSAREN 3.6.4\r\n\r\n" +
                    "Starta med " + ReceptionFiles.LauncherName + ".\r\n" +
                    "Första gången väljer du vilket passersystem du ska läsa för.\r\n" +
                    "Valet sparas direkt och används automatiskt vid nästa start.\r\n" +
                    "Startguiden hjälper dig med anslutning och eventuell USB-drivrutin.\r\n" +
                    "En ny läsare använder standardformat utan referensbricka.\r\n" +
                    "Ändra valet under Hjälp > Inställningar.\r\n\r\n" +
                    "RCO – MIFARE + EM: mR och eR, samma bricka två gånger.\r\n" +
                    "RCO: kort (9 siffror) och lång (10 siffror), MIFARE eller EM.\r\n" +
                    "Axema VAKA: EM 14 siffror. Bewator 2010/Omnis: EM 13 siffror.\r\n\r\n" +
                    "1. Klicka på Läs bricka.\r\n" +
                    "2. Följ stegen i huvudfönstret och lyft brickan när du blir ombedd.\r\n" +
                    "3. Kopiera önskat ID-nummer. Välj Läs nästa bricka för nytt kort.\r\n\r\n" +
                    "Klistra in numret där du behöver det innan du läser nästa bricka.\r\n" +
                    "Inställningarna för läsaren och valt passersystem sparas, inte skanningarna.\r\n" +
                    "Programmet ställer automatiskt in läsaren så att kortnummer inte skrivs i andra fönster.\r\n" +
                    "Avläsning i programmet startar när du väljer Läs bricka.\r\n" +
                    "För IT: x64-drivrutinen ingår i mappen Drivrutiner; installera bara om den behövs.\r\n" +
                    "Vid frågor eller problem: kontakta IT.\r\n";
            }
        }
    }
    public static class ReceptionTests
    {
        public static int Run()
        {
            int n = 0;
            Action<bool> check = delegate(bool ok) { n++; if (!ok) throw new InvalidOperationException("Receptionens självtest " + n + " misslyckades."); };
            // Synthetic vectors, independent of the locally configured IT reference card.
            ScanResult sample = new ScanResult {
                RawM = "01020304", MOrder = "Forward",
                RawE = "0102030405", EOrder = "Forward"
            };
            check(FormatProfiles.RcoLong(sample, "M") == "0025800772");
            check(FormatProfiles.RcoShort(sample, "M") == "025800772");
            check(FormatProfiles.RcoLong(sample, "E") == "0051501029");
            check(FormatProfiles.RcoShort(sample, "E") == "051501029");
            check(FormatProfiles.Vaka14(sample) == "00004328719365");
            check(FormatProfiles.Bewator13(sample) == "0010033752069");
            check(FormatProfiles.ValidMode("Bewator13") && !FormatProfiles.ValidMode("Generic"));
            // Use an isolated temporary directory; never alter the receptionist's
            // configuration, calibration or recovery files when running tests.
            string configDirectory = Path.Combine(Path.GetTempPath(), "Bricklasaren-format-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDirectory);
            try
            {
                string configPath = Path.Combine(configDirectory, "formatval.json");
                check(ReceptionFiles.LoadFormatPreference(configPath) == null);
                string[] invalid = { "", "{", "null", "{}", "{\"Mode\":\"Generic\",\"RcoChip\":\"M\"}", "{\"Mode\":\"RCO\",\"RcoChip\":\"Unknown\"}", "{\"Mode\":\"RCO\"}" };
                foreach (string text in invalid)
                {
                    File.WriteAllText(configPath, text, ReceptionFiles.Utf8);
                    check(ReceptionFiles.LoadFormatPreference(configPath) == null);
                }
                // The previous local JSON format remains valid without migration.
                File.WriteAllText(configPath, "{\"Mode\":\"RCO\",\"RcoChip\":\"E\"}", ReceptionFiles.Utf8);
                FormatPreference oldChoice = ReceptionFiles.LoadFormatPreference(configPath);
                check(oldChoice != null && oldChoice.Mode == FormatProfiles.Rco && oldChoice.RcoChip == "E");
                foreach (string mode in new string[] { FormatProfiles.Combo, FormatProfiles.Rco, FormatProfiles.Axema, FormatProfiles.Bewator })
                {
                    foreach (string chip in new string[] { "M", "E" })
                    {
                        ReceptionFiles.SaveFormatPreference(configPath, mode, chip);
                        FormatPreference saved = ReceptionFiles.LoadFormatPreference(configPath);
                        check(saved != null && saved.Mode == mode && saved.RcoChip == chip);
                    }
                }
                string before = File.ReadAllText(configPath, Encoding.UTF8);
                bool rejected = false;
                try { ReceptionFiles.SaveFormatPreference(configPath, "Generic", "M"); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected && File.ReadAllText(configPath, Encoding.UTF8) == before);
                rejected = false;
                try { ReceptionFiles.SaveFormatPreference(configPath, FormatProfiles.Rco, "Unknown"); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected && File.ReadAllText(configPath, Encoding.UTF8) == before);
                check(Directory.GetFiles(configDirectory, "*.tmp").Length == 0);
                string referencePath = Path.Combine(configDirectory, ReferenceCard.FileName);
                rejected = false;
                try
                {
                    new ReaderClient("192.168.63.99", configDirectory, new LocalLog()).Scan(
                        "Both", true, System.Threading.CancellationToken.None, delegate(string message) { });
                }
                catch (InvalidDataException) { rejected = true; }
                check(rejected); // Missing reference is rejected before reader I/O.
                check(Directory.GetFiles(configDirectory, "aterstall-*").Length == 0);
                string[] invalidReferences = {
                    "", "{", "null", "{}",
                    "{\"MifareHex\":\"01020304\"}",
                    "{\"MifareHex\":\"010203\",\"EmHex\":\"0102030405\"}",
                    "{\"MifareHex\":\"01020304\",\"EmHex\":\"01020304\"}",
                    "{\"MifareHex\":\"01020201\",\"EmHex\":\"0102030405\"}",
                    "{\"MifareHex\":\"01020304\",\"EmHex\":\"0102030201\"}",
                    "{\"MifareHex\":\"ZZZZZZZZ\",\"EmHex\":\"0102030405\"}",
                    new string('x', 16385)
                };
                foreach (string text in invalidReferences)
                {
                    File.WriteAllText(referencePath, text, ReceptionFiles.Utf8);
                    rejected = false;
                    try { ReferenceCard.Read(referencePath); }
                    catch (InvalidDataException) { rejected = true; }
                    check(rejected);
                }
                foreach (string uid in new string[] { "a1b2c3d4", "01020304050607", "0102030405060708090a" })
                {
                    File.WriteAllText(referencePath, new JavaScriptSerializer().Serialize(
                        new ReferenceCard { MifareHex = uid, EmHex = "a1b2c3d4e5" }), ReceptionFiles.Utf8);
                    ReferenceCard card = ReferenceCard.Read(referencePath);
                    check(card.MifareHex == uid.ToUpperInvariant() && card.EmHex == "A1B2C3D4E5");
                }
                check(!ReceptionFiles.RequiredPackageFiles.Contains(ReferenceCard.FileName));
            }
            finally { Directory.Delete(configDirectory, true); }
            check(Codec.SetMibCommand("04010500", 2, new byte[] { 255 }) == "FF6800000C0200000D04040105000201FF00");
            check(FormatProfiles.IsDigits("0010033752069", 13));
            check(!FormatProfiles.IsDigits("0010033752069\n", 13));
            check(FormatProfiles.Bewator13(new ScanResult { RawE = "0504030201", EOrder = "Reverse" }) == "0010033752069");
            check(FormatProfiles.RcoShort(new ScanResult {RawM = "04030201", MOrder = "Reverse"}, "M") == "025800772");
            check(ReceptionText.IsNumber("025800772"));
            check(!ReceptionText.IsNumber("25800772"));
            check(!ReceptionText.IsNumber("025800772\n"));
            check(!ReceptionText.IsNumber("ABCDEFGHI"));
            check(!ReceptionText.IsNumber(null));
            check(ReceptionText.Both("025800772", "051501029").Contains("mR): 025800772\r\n"));
            check(ReceptionText.Both("025800772", "051501029").EndsWith("eR): 051501029", StringComparison.Ordinal));
            check(ReceptionText.Step("MIFARE läst. Lyft brickan", false).Title == "Lyft bort brickan");
            check(ReceptionText.Step("EM läst. Lyft brickan", false).Eyebrow == "BÅDA NUMREN ÄR LÄSTA");
            check(ReceptionText.Step("Lägg SAMMA bricka mot läsaren och håll kvar.", false).Eyebrow.Contains("HYRESVÄRDEN"));
            check(ReceptionText.Step("Steg 1 av 2: Lägg referensbrickan", true).Title.Contains("referensbrickan"));
            check(ReceptionText.Step("Återställer läsarens tidigare inställningar.", false).Eyebrow == "SLUTFÖR");
            check(!ReceptionFiles.ValidCalibration(null));
            Calibration c = new Calibration { Schema = 2, ReaderId = new string('A', 32), Firmware = "01050176", MifareOrder = "Reverse", EmOrder = "Forward" };
            check(ReceptionFiles.ValidCalibration(c));
            c.EmOrder = "Guess"; check(!ReceptionFiles.ValidCalibration(c));
            c.EmOrder = "Forward"; c.ReaderId = "../bad"; check(!ReceptionFiles.ValidCalibration(c));
            check(!ReceptionText.IsNumber(" 025800772"));
            check(!ReceptionText.IsNumber("025800772 "));
            check(!ReceptionText.IsNumber("0258007720"));
            check(ReceptionText.IsNumber("000000000"));
            check(ReceptionText.Step("Ta bort alla brickor från läsaren.", false).Title == "Lyft bort brickan");
            check(ReceptionText.Step("Ställer in MIFARE.", false).Title == "Håll brickan borta en stund");
            check(ReceptionText.Step("Lägg brickan mot läsaren och håll kvar.", false).Eyebrow.StartsWith("STEG 1 AV 2", StringComparison.Ordinal));
            check(ReceptionText.Step("Växlar automatiskt till EM.", false).Eyebrow == "FÖRBEREDER NÄSTA STEG");
            check(ReceptionText.Step("Steg 2 av 2: Lägg SAMMA referensbricka", true).Hint.Contains("referensbrickan"));
            check(ReceptionText.Step("Annat meddelande", false).Eyebrow == "LÄSNING PÅGÅR");
            c.ReaderId = new string('A', 32); c.Firmware = "01050176\n"; check(!ReceptionFiles.ValidCalibration(c));
            c.Firmware = "01050176"; c.Schema = 1; check(!ReceptionFiles.ValidCalibration(c));
            bool refused = false;
            try { ReceptionText.Both("25800772", "051501029"); } catch (InvalidOperationException) { refused = true; }
            check(refused);
            check(ReceptionText.QuickStart.Contains(ReceptionFiles.LauncherName));
            check(!ReceptionText.QuickStart.Contains("Bricklasaren.exe"));
            string[] packageFiles = ReceptionFiles.RequiredPackageFiles;
            check(packageFiles.Length == packageFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            check(packageFiles.Contains("Program/Starta-Bricklasaren.ps1"));
            check(packageFiles.Contains(ReceptionFiles.LauncherName));
            check(packageFiles.All(delegate(string p) {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                bool sourceOrDocument = ext == ".cmd" || ext == ".ps1" || ext == ".cs" || ext == ".txt" || ext == ".md";
                return sourceOrDocument || p == "Program/Bricklasaren.ico" ||
                    p == "Assets/Lucide/volume-2.png" || p == "Assets/Lucide/volume-off.png" ||
                    p == "Assets/Lucide/volume-2.svg" || p == "Assets/Lucide/volume-off.svg";
            }));
            check(packageFiles.All(delegate(string p) { return !Path.IsPathRooted(p) && !p.Contains(".."); }));
            // A caller cannot change the allow-list used by the next export.
            packageFiles[0] = "changed";
            check(ReceptionFiles.RequiredPackageFiles[0] == ReceptionFiles.LauncherName);
            string privateMessage = "Råvärde: 01020304, 0102030405, 025800772, 051501029, 12345678";
            string safe = ReceptionFiles.SafeError(new IOException(privateMessage, new FormatException(privateMessage)));
            check(safe.Contains("IOException"));
            check(safe.Contains("FormatException"));
            check(!safe.Contains("01020304"));
            check(!safe.Contains("0102030405"));
            check(!safe.Contains("025800772"));
            check(!safe.Contains("051501029"));
            check(!safe.Contains("12345678"));
            check(ReaderDiagnostics.Code(new InvalidDataException(privateMessage)) == null);
            check(ReaderDiagnostics.Code(new InvalidDataException("Dubbelt HTTP-huvud.")) == "HTTP_HEADER_DUPLICATE");
            check(ReaderDiagnostics.Code(new InvalidDataException("Dubbelt HTTP-huvud." + privateMessage)) == null);
            check(ReceptionFiles.SafeError(new InvalidDataException("Dubbelt HTTP-huvud.")).Contains("HTTP_HEADER_DUPLICATE"));
            check(!ReceptionFiles.SafeError(new InvalidDataException("Dubbelt HTTP-huvud." + privateMessage)).Contains("HTTP_HEADER_DUPLICATE"));
            check(ReaderDiagnostics.Code(new InvalidDataException("USB-originalinställningen är ogiltig eller gäller en annan läsare eller firmware. Kontakta IT.")) == "USB_BACKUP_INVALID");
            check(ReaderDiagnostics.Code(new InvalidDataException("Läsaren rapporterar ett okänt USB-läge. Förberedelsen kan inte godkännas.")) == "USB_MODE_RESPONSE");
            bool mibRejected = false;
            try { Codec.MibResponse("04010500", Codec.Hex("0302010D040401050005009000")); }
            catch (ReaderMibException rejected)
            {
                mibRejected = true;
                string summary = ReceptionFiles.SafeError(rejected);
                check(summary.Contains("MIB_REJECTED") && summary.Contains("UsbOutputMode") && summary.Contains("Codec.MibResponse"));
                check(summary.Contains("MIB-status 2") && summary.Contains("index 1"));
                check(!summary.Contains("04010500") && !summary.Contains("0302010D"));
                check(!summary.Contains(Path.GetTempPath()));
            }
            check(mibRejected);
            bool shortError = false;
            try { Codec.MibResponse("02020104010100", Codec.Hex("0302009000")); }
            catch (ReaderMibException missing) { shortError = missing.Status == 2 && missing.Index == 0 && missing.Command == "IndicatorSequences"; }
            check(shortError);
            string unknown = ReceptionFiles.SafeError(new ReaderMibException(privateMessage, 2, 1));
            check(unknown.Contains("Other") && !unknown.Contains("01020304"));
            check(ReceptionFiles.SafeError(new IOException(privateMessage, new ReaderMibException("04010500", 2, 1))).Contains("UsbOutputMode"));
            string chain = ReceptionFiles.SafeError(new IOException(privateMessage, new InvalidDataException("Dubbelt HTTP-huvud.")));
            check(chain.Contains("HTTP_HEADER_DUPLICATE") && !chain.Contains("Råvärde"));
            check(ReceptionFiles.SafeError(null) == "Ingen felinformation.");
            check(ReceptionFiles.SafeError(new ReaderStatusException(0x6985)).Contains("6985"));
            return n;
        }
    }
}
