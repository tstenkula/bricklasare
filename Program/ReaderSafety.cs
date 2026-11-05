// Persistent, recoverable USB-output protection. Does not turn off the RF field.
// HID 5127-903 B.7: Custom Report Enable MIB 04.01.05, integer 00/FF; apply/reboot required.
// Apply Changes writes flash; it is not sent on an already protected start.
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Web.Script.Serialization;

    public sealed class ReaderOutputBackup
    {
        public int Schema { get; set; }
        public string ReaderId { get; set; }
        public string Firmware { get; set; }
        public bool? CustomReport { get; set; }
        public string CreatedUtc { get; set; }
    }
    public sealed class ReaderOutputTransition
    {
        public int Schema { get; set; }
        public string ReaderId { get; set; }
        public string Firmware { get; set; }
        public bool? TargetCustomReport { get; set; }
        public string Phase { get; set; }
        public string CreatedUtc { get; set; }
    }

    public static class ReaderSafety
    {
        private const string CustomReportOid = "04010500";
        private const int Schema = 1;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string[]> LegacyIdentities = new Dictionary<string, string[]>();

        private static bool DecodeMode(Tlv state)
        {
            if (state == null || state.Tag != 2 || state.Value == null || state.Value.Length != 1 ||
                (state.Value[0] != 0 && state.Value[0] != 255))
            {
                InvalidDataException error = new InvalidDataException("Läsaren rapporterar ett okänt USB-läge. Förberedelsen kan inte godkännas.");
                error.Data["Tobtech.UsbModeShape"] = new UsbModeShape(state);
                throw error;
            }
            return state.Value[0] == 255;
        }

        public static bool IsCustomReport(ReaderClient reader)
        {
            RequireReader(reader);
            CheckIdentity(reader, reader.ReaderId, reader.Firmware);
            return DecodeMode(reader.GetMib(CustomReportOid));
        }

        public static bool HasOriginalMode(ReaderClient reader, string dataPath)
        {
            if (reader == null || !IsHex(reader.ReaderId, 32) || !IsHex(reader.Firmware, 8)) return false;
            try
            {
                string path = Path.Combine(dataPath, "usb-original-" + reader.ReaderId + ".json");
                if (!File.Exists(path)) return false;
                ReadBackup(path, reader.ReaderId, reader.Firmware);
                return true;
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        private static void RequireReader(ReaderClient reader)
        {
            if (reader == null || !IsHex(reader.ReaderId, 32) || !IsHex(reader.Firmware, 8))
                throw new InvalidOperationException("Anslut och identifiera läsaren innan USB-läget kontrolleras.");
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int i = 0; i < value.Length; i++)
                if (!((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'A' && value[i] <= 'F'))) return false;
            return true;
        }

        private static void CheckIdentity(ReaderClient reader, string id, string firmware)
        {
            CheckIdentityValues(reader.GetMib("010A00"), reader.GetMib("010100"), id, firmware);
        }

        private static void CheckAddressIdentity(string address, string id, string firmware)
        {
            CheckIdentityValues(
                Codec.MibResponse("010A00", Send(address, Codec.GetMibCommand("010A00"))),
                Codec.MibResponse("010100", Send(address, Codec.GetMibCommand("010100"))), id, firmware);
        }

        private static void CheckIdentityValues(Tlv id, Tlv firmware, string expectedId, string expectedFirmware)
        {
            if (id == null || id.Tag != 4 || id.Value == null || id.Value.Length != 16 ||
                firmware == null || firmware.Tag != 2 || firmware.Value == null || firmware.Value.Length != 4)
                throw new InvalidDataException("Läsarens identitet eller firmware kunde inte verifieras. Åtgärden avbröts.");
            if (Codec.ToHex(id.Value) != expectedId)
                throw new InvalidOperationException("En annan läsare är ansluten. USB-ändringen avbröts för att skydda dess inställningar.");
            if (Codec.ToHex(firmware.Value) != expectedFirmware)
                throw new InvalidOperationException("Läsarens firmware har ändrats. USB-ändringen avbröts; kontakta IT.");
        }

        private static byte[] Send(string address, string cmd)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(address, out ip) || !ReaderNetwork.IsPrivate(ip))
                throw new InvalidOperationException("Ogiltig lokal läsaradress.");
            return Codec.HttpResponse(ReaderNetwork.Send(ip, cmd));
        }

        public static ReaderClient EnsureProtected(ReaderClient reader, string address, string dataPath, LocalLog log, Action<string> progress)
        {
            return EnsureMode(reader, address, dataPath, log, progress, true, false);
        }

        public static ReaderClient RestoreOriginalMode(ReaderClient reader, string address, string dataPath, LocalLog log, Action<string> progress)
        {
            return EnsureMode(reader, address, dataPath, log, progress, false, true);
        }

        private static ReaderClient EnsureMode(ReaderClient reader, string address, string dataPath, LocalLog log,
            Action<string> progress, bool desired, bool restoreOriginal)
        {
            lock (Gate)
            {
                RequireReader(reader);
                string directory = Path.GetFullPath(dataPath);
                if (!String.Equals(directory.TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(reader.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("USB-skyddets data måste sparas tillsammans med läsarens konfiguration.");
                if (reader.HasPendingRecovery)
                    throw new InvalidOperationException("Återställ den avbrutna skanningen innan USB-läget ändras.");
                Directory.CreateDirectory(directory);
                string id = reader.ReaderId, firmware = reader.Firmware;
                string backupPath = Path.Combine(directory, "usb-original-" + id + ".json");
                string pendingPath = Path.Combine(directory, "usb-pending-" + id + ".json");
                // The OS releases this lock even if the application crashes. The empty lock file may remain.
                using (FileStream operationLock = new FileStream(Path.Combine(directory, "usb-mode-" + id + ".lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    CheckIdentity(reader, id, firmware);
                    CheckAddressIdentity(address, id, firmware);
                    bool current = DecodeMode(reader.GetMib(CustomReportOid));
                    ReaderOutputTransition pending = ReadPending(pendingPath, id, firmware);
                    ReaderOutputBackup original;
                    if (File.Exists(backupPath)) original = ReadBackup(backupPath, id, firmware);
                    else
                    {
                        // Never infer the original mode from a possibly modified device after a crash.
                        if (pending != null || restoreOriginal)
                            throw new InvalidDataException("Originalinställningen för USB-läget saknas. Kontakta IT; ingen inställning ändras.");
                        original = new ReaderOutputBackup { Schema = Schema, ReaderId = id, Firmware = firmware,
                            CustomReport = current, CreatedUtc = DateTime.UtcNow.ToString("o") };
                        AtomicWrite(backupPath, original, false);
                    }
                    if (restoreOriginal) desired = original.CustomReport.Value;
                    if (pending == null && current == desired)
                    {
                        if (log != null) log.Add("USB-utdata verifierade: " + (desired ? "Custom Report." : "ursprungligt tangentbordsläge."));
                        return reader;
                    }
                    if (pending == null || pending.TargetCustomReport.Value != desired)
                    {
                        // A new requested target supersedes an interrupted operation, while preserving the original backup.
                        pending = new ReaderOutputTransition { Schema = Schema, ReaderId = id, Firmware = firmware,
                            TargetCustomReport = desired, Phase = "prepared", CreatedUtc = DateTime.UtcNow.ToString("o") };
                        AtomicWrite(pendingPath, pending, true);
                    }
                    Report(progress, desired ? "Förbereder läsaren så att den inte skriver i andra program…" : "Återställer läsarens ursprungliga USB-läge…");
                    if (pending.Phase == "prepared" || current != desired)
                    {
                        // Persist the need for Apply before SET. Readback alone cannot prove that USB has re-enumerated.
                        if (pending.Phase != "prepared")
                        {
                            pending.Phase = "prepared"; AtomicWrite(pendingPath, pending, true);
                        }
                        CheckAddressIdentity(address, id, firmware);
                        if (current != desired)
                        {
                            Tlv reply = Codec.MibResponse(CustomReportOid, Send(address,
                                Codec.SetMibCommand(CustomReportOid, 2, new byte[] { desired ? (byte)255 : (byte)0 })));
                            if (DecodeMode(reply) != desired) throw new IOException("Läsaren bekräftade inte ändringen av USB-läget.");
                        }
                        CheckAddressIdentity(address, id, firmware);
                        RequireEmptySuccess(Send(address, "FF6800010101"));
                        if (log != null) log.Add("USB-lägesändringen har sparats i läsaren; omstart krävs.");
                        Thread.Sleep(350);
                        pending.Phase = "applied"; AtomicWrite(pendingPath, pending, true);
                    }
                    CheckAddressIdentity(address, id, firmware);
                    bool rebootAcknowledged = false;
                    Report(progress, "Läsaren startar om. Låt USB-kabeln sitta kvar…");
                    try { RequireEmptySuccess(Send(address, "FF6800010108")); rebootAcknowledged = true; }
                    catch (WebException ex)
                    {
                        if (!IsDisconnect(ex)) throw;
                        if (log != null) log.Add("USB-omstarten avbröt svaret; frånkoppling och återanslutning måste verifieras.");
                    }
                    catch (IOException)
                    {
                        // A reset may close the HTTP stream. Do not accept mode readback without observing a disconnect.
                        if (log != null) log.Add("Svaret på USB-omstarten avbröts; återanslutningen verifieras.");
                    }
                    ReaderClient connected = Reconnect(address, directory, log, desired, id, firmware, rebootAcknowledged);
                    File.Delete(pendingPath);
                    if (log != null) log.Add("USB-läget verifierat efter omstart för samma läsare och firmware.");
                    Report(progress, desired ? "Läsaren är förberedd. Tryck på Läs bricka när du vill skanna." : "Läsarens ursprungliga USB-läge är återställt.");
                    return connected;
                }
            }
        }

        private static void Report(Action<string> progress, string message)
        {
            if (progress != null) progress(message);
        }

        private static void RequireEmptySuccess(byte[] response)
        {
            if (Codec.Success(response).Length != 0) throw new FormatException("Oväntade data i läsarens bekräftelse.");
        }

        private static bool IsDisconnect(WebException ex)
        {
            return ex.Status == WebExceptionStatus.ConnectFailure || ex.Status == WebExceptionStatus.ConnectionClosed ||
                ex.Status == WebExceptionStatus.ReceiveFailure || ex.Status == WebExceptionStatus.SendFailure ||
                ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.KeepAliveFailure;
        }

        private static ReaderClient Reconnect(string address, string dataPath, LocalLog log, bool expected,
            string id, string firmware, bool rebootAcknowledged)
        {
            Exception last = null; bool observedDisconnect = false;
            Stopwatch elapsed = Stopwatch.StartNew();
            for (int attempt = 0; attempt < 30 && elapsed.ElapsedMilliseconds < 45000; attempt++)
            {
                try
                {
                    // Probe the expected identity first. A different connected reader is a hard failure, never a retry.
                    CheckAddressIdentity(address, id, firmware);
                    if (elapsed.ElapsedMilliseconds >= 3000 && (rebootAcknowledged || observedDisconnect))
                    {
                        ReaderClient candidate = new ReaderClient(address, dataPath, log ?? new LocalLog());
                        candidate.Connect();
                        if (candidate.ReaderId != id || candidate.Firmware != firmware)
                            throw new InvalidOperationException("En annan läsare eller firmware anslöts under USB-omstarten.");
                        if (IsCustomReport(candidate) != expected)
                            throw new InvalidOperationException("USB-läget stämmer inte efter omstart. Kontakta IT.");
                        if (candidate.HasPendingRecovery)
                            throw new InvalidOperationException("En skanningsåterställning väntar. USB-läget kan inte godkännas ännu.");
                        return candidate;
                    }
                }
                catch (WebException ex)
                {
                    if (!IsDisconnect(ex)) throw;
                    observedDisconnect = true; last = ex;
                }
                catch (IOException ex)
                {
                    last = ex;
                    // Only a missing USB interface or transport disconnect counts. Invalid protocol data does not.
                    WebException network = ex.InnerException as WebException;
                    if (ex is ReaderNetworkDisconnectedException || (network != null && IsDisconnect(network))) observedDisconnect = true;
                }
                Thread.Sleep(500);
            }
            throw new IOException("Läsarens USB-omstart kunde inte verifieras. Anslut samma läsare igen och välj Försök igen. Förberedelsen är sparad och kan fortsätta.", last);
        }

        // Diagnostic preflight only: no lock file, backup creation, SET, Apply,
        // reboot or recovery. Use the same strict saved-state validators as EnsureMode.
        internal static string InspectSavedState(ReaderClient reader)
        {
            RequireReader(reader);
            string backupPath = Path.Combine(reader.DirectoryPath, "usb-original-" + reader.ReaderId + ".json");
            string pendingPath = Path.Combine(reader.DirectoryPath, "usb-pending-" + reader.ReaderId + ".json");
            ReaderOutputTransition pending = ReadPending(pendingPath, reader.ReaderId, reader.Firmware);
            bool backup = File.Exists(backupPath);
            if (backup) ReadBackup(backupPath, reader.ReaderId, reader.Firmware);
            else if (pending != null)
                throw new InvalidDataException("Originalinställningen för USB-läget saknas. Kontakta IT; ingen inställning ändras.");
            return "UsbOriginal: " + (backup ? "Godkänd" : "Saknas; ingen sparad förberedelse") +
                "\r\nUsbPending: " + (pending == null ? "Ingen" : pending.Phase == "prepared" ? "Förberedd" : "Tillämpad; omstart väntar");
        }

        private static ReaderOutputBackup ReadBackup(string path, string id, string firmware)
        {
            ReaderOutputBackup backup = ReadJson<ReaderOutputBackup>(path);
            if (backup == null || backup.Schema != Schema || backup.ReaderId != id || backup.Firmware != firmware ||
                !backup.CustomReport.HasValue || String.IsNullOrWhiteSpace(backup.CreatedUtc))
                throw new InvalidDataException("USB-originalinställningen är ogiltig eller gäller en annan läsare eller firmware. Kontakta IT.");
            return backup;
        }

        private static ReaderOutputTransition ReadPending(string path, string id, string firmware)
        {
            if (!File.Exists(path)) return null;
            ReaderOutputTransition pending = ReadJson<ReaderOutputTransition>(path);
            if (pending == null || pending.Schema != Schema || pending.ReaderId != id || pending.Firmware != firmware ||
                !pending.TargetCustomReport.HasValue || (pending.Phase != "prepared" && pending.Phase != "applied") ||
                String.IsNullOrWhiteSpace(pending.CreatedUtc))
                throw new InvalidDataException("Den avbrutna USB-förberedelsen kunde inte verifieras. Behåll konfigurationsfilerna och kontakta IT.");
            return pending;
        }

        private static T ReadJson<T>(string path)
        {
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("USB-konfigurationsfilen är för stor.");
            try { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (ArgumentException ex) { throw new InvalidDataException("USB-konfigurationsfilen kunde inte läsas.", ex); }
            catch (InvalidOperationException ex) { throw new InvalidDataException("USB-konfigurationsfilen kunde inte läsas.", ex); }
        }

        private static void AtomicWrite(string path, object value, bool replace)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(value));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (replace && File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // Kept for source compatibility; the complete protected operation now happens before this method returns.
        // New UI code should use EnsureProtected / RestoreOriginalMode and keep their returned ReaderClient.
        public static void ChangeCustomReport(ReaderClient reader, string address, bool enable)
        {
            RequireReader(reader);
            ReaderClient result = EnsureMode(reader, address, reader.DirectoryPath, new LocalLog(), null, enable, false);
            lock (Gate) LegacyIdentities[Path.GetFullPath(reader.DirectoryPath) + "|" + address] = new string[] { result.ReaderId, result.Firmware };
        }

        public static ReaderClient WaitForReconnect(string address, string dataPath, LocalLog log, bool expected)
        {
            string[] identity;
            lock (Gate)
            {
                if (!LegacyIdentities.TryGetValue(Path.GetFullPath(dataPath) + "|" + address, out identity))
                    throw new InvalidOperationException("USB-återanslutningen saknar verifierad läsaridentitet. Anslut läsaren på nytt.");
            }
            ReaderClient candidate = new ReaderClient(address, dataPath, log ?? new LocalLog());
            candidate.Connect(); CheckIdentity(candidate, identity[0], identity[1]);
            if (IsCustomReport(candidate) != expected) throw new InvalidOperationException("Läsarens USB-läge stämmer inte.");
            return candidate;
        }
    }
}
