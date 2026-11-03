namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    // Explicit self-tests only. No reader contact and no changes to user settings.
    public static class ReliabilityTests
    {
        [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
        private static int count;
        private static void Check(bool condition, string description)
        {
            count++; if (!condition) throw new InvalidOperationException("Tillförlitlighetstest: " + description);
        }
        private static void Reject(Action action, string description)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, description);
        }
        public static int Run()
        {
            count = 0;
            string directory = Path.Combine(Path.GetTempPath(), "Bricklasaren-reliability-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                ExerciseUpdates(directory);
                ExerciseFirstStart(Path.Combine(directory, "first-start"));
                LocalLog log = new LocalLog(); log.Add("old-entry");
                for (int i = 0; i < 2600; i++) log.Transport(true, i);
                string trace = log.Text();
                Check(!trace.Contains("old-entry") && trace.Contains("2599"), "Bounded transport history");
                Check(trace.Split(new string[] { "\r\n" }, StringSplitOptions.None).Length == 2502, "Log row limit");
                log.Clear(); Check(!log.Text().Contains("RX"), "Cleared transport history");

                string pending = Path.Combine(directory, "aterstall-test.json"); File.WriteAllText(pending, "pending");
                string original = Path.Combine(directory, "usb-original.json"); File.WriteAllText(original, "original");
                string unrelated = Path.Combine(directory, "backup-other.json"); File.WriteAllText(unrelated, "keep");
                for (int i = 0; i < 25; i++) File.WriteAllText(Path.Combine(directory, "backup-20260101-000000-" + i.ToString("D3") + ".json"), "test");
                BackupRetention.Prune(directory);
                Check(Directory.GetFiles(directory, "backup-*.json").Length == 26, "Pending recovery protects all archives");
                File.Delete(pending); BackupRetention.Prune(directory);
                Check(Directory.GetFiles(directory, "backup-*.json").Length == 21, "Keep twenty completed archives");
                Check(!File.Exists(Path.Combine(directory, "backup-20260101-000000-004.json")) && File.Exists(Path.Combine(directory, "backup-20260101-000000-005.json")), "Keep newest archives");
                Check(File.ReadAllText(unrelated) == "keep" && File.ReadAllText(original) == "original", "Unrelated and original USB files preserved");

                string choices = Path.Combine(directory, "formatval.json");
                ReceptionFiles.SaveFormatPreference(choices, "Kombi", "M");
                Check(ReceptionFiles.LoadFormatPreference(choices).Mode == FormatProfiles.Combo && FormatProfiles.Label("Kombi").Contains("MIFARE + EM"), "Existing dual-read setting stays compatible");

                AppearanceSettings settings = AppearanceFiles.Load(directory);
                Check(AppearanceSettings.DefaultAccentHex == "#003B55", "Navy is the product default");
                Check(settings.Schema == 1 && settings.AccentHex == AppearanceSettings.DefaultAccentHex && settings.LogoFile == "", "Appearance defaults");
                foreach (string hex in new string[] { AppearanceSettings.DefaultAccentHex, "#0076AD", "#176B4D", "#7056A7", "#FFFFFF", "#000000", "#FFFF00", "#808080", "#FF00FF" })
                {
                    ThemePalette theme = new ThemePalette(hex);
                    Check(ThemePalette.Contrast(theme.Accent, theme.ButtonText) >= 4.5 && ThemePalette.Contrast(theme.Hover, theme.ButtonText) >= 4.5, "Readable buttons " + hex);
                    Check(ThemePalette.Contrast(theme.TextAccent, theme.Light) >= 4.5 && ThemePalette.Contrast(theme.TextAccent, Ui.Paper) >= 4.5, "Readable accent labels " + hex);
                }
                settings.Organization = "Testorganisation"; settings.Subtitle = "Besöksservice"; settings.AccentHex = "#176B4D";
                AppearanceFiles.Save(directory, settings, null);
                string configPath = Path.Combine(directory, AppearanceFiles.FileName), before = File.ReadAllText(configPath);
                Check(AppearanceFiles.Load(directory).Organization == settings.Organization, "Appearance persists across load");
                settings.AccentHex = "bad";
                Reject(delegate { AppearanceFiles.Save(directory, settings, null); }, "Invalid color rejected");
                Check(File.ReadAllText(configPath) == before, "Invalid save preserves old settings");
                settings.AccentHex = "#176B4D"; settings.LogoFile = "../outside.png";
                Reject(delegate { AppearanceFiles.Save(directory, settings, null); }, "Logo traversal rejected"); settings.LogoFile = "";
                string input = Path.Combine(directory, "input.png");
                using (Bitmap source = new Bitmap(120, 40))
                {
                    using (Graphics graphics = Graphics.FromImage(source)) { graphics.Clear(Color.Transparent); graphics.FillRectangle(Brushes.DarkGreen, 10, 10, 100, 20); }
                    source.Save(input, ImageFormat.Png);
                }
                using (Bitmap detached = AppearanceFiles.ReadLogo(input))
                {
                    File.Delete(input); // Must succeed on Windows: no lingering source-file lock.
                    AppearanceFiles.Save(directory, settings, detached);
                    Check(!File.Exists(input), "Original logo can be moved or deleted");
                    string first = Path.Combine(directory, "branding", settings.LogoFile);
                    Check(File.Exists(first), "Imported logo owned by the app");
                    AppearanceFiles.Save(directory, settings, detached);
                    Check(!File.Exists(first) && Directory.GetFiles(Path.Combine(directory, "branding")).Length == 1, "Replaced logo removed after commit");
                    before = File.ReadAllText(configPath);
                    bool failed = false;
                    using (FileStream held = new FileStream(configPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        try { AppearanceFiles.Save(directory, settings, detached); } catch (IOException) { failed = true; }
                    }
                    Check(failed && File.ReadAllText(configPath) == before && Directory.GetFiles(Path.Combine(directory, "branding")).Length == 1, "Failed save retains prior configuration and logo");
                }
                using (Bitmap loaded = AppearanceFiles.LoadLogo(directory, AppearanceFiles.Load(directory)))
                    Check(loaded != null && loaded.Width == 120 && loaded.GetPixel(0, 0).A == 0, "Transparency survives import");
                File.WriteAllText(input, new string('x', 40)); Reject(delegate { using (Bitmap invalid = AppearanceFiles.ReadLogo(input)) { } }, "Malformed image rejected");
                using (Bitmap huge = new Bitmap(2049, 1)) huge.Save(input, ImageFormat.Png);
                Reject(delegate { using (Bitmap invalid = AppearanceFiles.ReadLogo(input)) { } }, "Oversize dimensions rejected before decoding");
                byte[] large = new byte[2 * 1024 * 1024 + 1]; File.WriteAllBytes(input, large);
                Reject(delegate { using (Bitmap invalid = AppearanceFiles.ReadLogo(input)) { } }, "Oversize file rejected");

                // Render representative states and repeatedly create/dispose dialogs.
                string defaultDirectory = Path.Combine(directory, "standard");
                Directory.CreateDirectory(defaultDirectory);
                Ui.SetAppearance(AppearanceSettings.Default());
                using (AppearanceDialog standard = new AppearanceDialog(defaultDirectory))
                {
                    Check(((TextBox)Find(standard, "Accentfärg i hexadecimal form", true)).Text == "#003B55", "Fresh editor selects navy");
                    Check(((ComboBox)Find(standard, "Färgtema", true)).SelectedIndex == 0, "Fresh editor selects the default preset");
                    Check(((Button)Find(standard, "Spara utseende", false)).BackColor.ToArgb() == Color.FromArgb(0, 59, 85).ToArgb(), "Standard action buttons use navy");
                }
                Render(new FirstStartDialog(""), "standard-forsta-start");
                Render(new FirstStartDialog("Hämta drivrutinen från tillverkarens webbplats."), "standard-drivrutin-lank");
                Render(new AppearanceDialog(defaultDirectory), "standard-utseende");
                Render(new ReceptionForm(true, defaultDirectory), "standard-huvudfonster");
                Render(new ReferenceDialog(defaultDirectory), "standard-referensprov");
                Render(new FormatSelector("Kombi", "M"), "standard-passersystem");
                Render(new WorkflowDialog(new WorkflowSettings(), directory), "standard-arbetsflode");
                Render(new ReceptionHelp("192.168.63.99", null, "Kombi", "M", false), "standard-hjalp");
                Check(!File.Exists(Path.Combine(defaultDirectory, AppearanceFiles.FileName)), "Default preview does not create user settings");
                Ui.SetAppearance(settings);
                Render(new AppearanceDialog(directory), "eget-tema-gront-utseende");
                Render(new ReceptionForm(true, directory), "eget-tema-gront-huvudfonster");
                Check(AppearanceFiles.Load(directory).AccentHex == "#176B4D", "Existing custom theme is preserved");
                string savedBeforeDialogs = File.ReadAllText(configPath);
                ExerciseDialogs(directory, 5); Collect();
                using (Process process = Process.GetCurrentProcess())
                {
                    int initialGdi = GetGuiResources(process.Handle, 0), initialUser = GetGuiResources(process.Handle, 1), fonts = Ui.FontCount;
                    long initialManaged = GC.GetTotalMemory(true);
                    ExerciseDialogs(directory, 100); Collect();
                    int finalGdi = GetGuiResources(process.Handle, 0), finalUser = GetGuiResources(process.Handle, 1);
                    long finalManaged = GC.GetTotalMemory(true);
                    Console.WriteLine("Dialog stress: 100 cycles; GDI {0}->{1}; USER {2}->{3}; pooled fonts {4}->{5}; managed bytes {6}->{7}", initialGdi, finalGdi, initialUser, finalUser, fonts, Ui.FontCount, initialManaged, finalManaged);
                    Check(Ui.FontCount == fonts, "Font pool stays bounded across repeated dialogs");
                    Check(finalGdi <= initialGdi + 10 && finalUser <= initialUser + 10, "Windows GUI resources return to baseline");
                    Check(finalManaged <= initialManaged + 4 * 1024 * 1024, "Managed retention stays bounded after dialog stress");
                }
                Check(File.ReadAllText(configPath) == savedBeforeDialogs, "Closing without saving leaves appearance unchanged");
                ExerciseSettingsActions(directory);
                AppearanceFiles.Save(directory, AppearanceSettings.Default(), null);
                Check(Directory.GetFiles(Path.Combine(directory, "branding")).Length == 0 && AppearanceFiles.Load(directory).Organization == "", "Reset clears branding and old logo");
                AppearanceSettings previousBlue = AppearanceSettings.Default(); previousBlue.AccentHex = "#0076AD";
                AppearanceFiles.Save(directory, previousBlue, null);
                Check(AppearanceFiles.Load(directory).AccentHex == "#0076AD", "Explicit old blue remains a custom choice");
                File.WriteAllText(configPath, "{"); Check(AppearanceFiles.Load(directory).AccentHex == AppearanceSettings.DefaultAccentHex, "Malformed config falls back to usable default");
                File.WriteAllText(configPath, "null"); Check(AppearanceFiles.Load(directory).Schema == 1, "Null config falls back safely");
                File.WriteAllText(configPath, "{}"); Check(AppearanceFiles.Load(directory).Schema == 1, "Unknown schema falls back safely");
                File.WriteAllText(configPath, "{\"Schema\":1,\"AccentHex\":\"bad\",\"Organization\":\"\",\"Subtitle\":\"\"}");
                Check(AppearanceFiles.Load(directory).AccentHex == AppearanceSettings.DefaultAccentHex, "Invalid saved color falls back safely");
                AppearanceSettings brokenLogo = AppearanceSettings.Default(); brokenLogo.LogoFile = "logo-" + new string('a', 32) + ".png";
                File.WriteAllText(Path.Combine(directory, "branding", brokenLogo.LogoFile), new string('x', 40));
                Check(AppearanceFiles.LoadLogo(directory, brokenLogo) == null, "Corrupt local logo does not prevent startup");
                Check(!ReceptionFiles.RequiredPackageFiles.Contains(AppearanceFiles.FileName) && !ReceptionFiles.RequiredPackageFiles.Contains(ReferenceCard.FileName), "Personal configuration excluded from package");
                return count;
            }
            finally { Ui.SetAppearance(AppearanceSettings.Default()); Ui.DisposeFonts(); Directory.Delete(directory, true); }
        }
        private static void ExerciseFirstStart(string directory)
        {
            Directory.CreateDirectory(directory);
            FakeReader fake = new FakeReader();
            ReaderClient reader = new ReaderClient("192.168.63.99", directory, new LocalLog()); reader.TestTransport = fake.Send;
            Check(!reader.CanRead, "Unconnected reader cannot scan");
            reader.Connect();
            Check(reader.CanRead && reader.Cal == null, "New reader starts with standard format without false calibration");
            Check(Directory.GetFiles(directory).Length == 0 && fake.Writes == 0, "New-reader connect is read-only and needs no local profile");
            ReceptionForm ready = new ReceptionForm(true, directory);
            System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(ReceptionForm).GetField("reader", fields).SetValue(ready, reader);
            typeof(ReceptionForm).GetField("profileChosen", fields).SetValue(ready, true);
            typeof(ReceptionForm).GetField("outputProtected", fields).SetValue(ready, true);
            typeof(ReceptionForm).GetMethod("ApplyModeUi", fields).Invoke(ready, null);
            Check(((Button)Find(ready, "Läs bricka", false)).Enabled, "Main scan button enabled for protected new reader without calibration");
            typeof(ReceptionForm).GetField("outputProtected", fields).SetValue(ready, false);
            typeof(ReceptionForm).GetMethod("UpdateButtons", fields).Invoke(ready, null);
            Check(!((Button)Find(ready, "Läs bricka", false)).Enabled, "First-start format never bypasses output protection");
            typeof(ReceptionForm).GetField("outputProtected", fields).SetValue(ready, true);
            typeof(ReceptionForm).GetMethod("ApplyModeUi", fields).Invoke(ready, null);
            Render(ready, "standard-ny-lasare-klar");
            Check(reader.MifareOrder == "Reverse" && reader.EmOrder == "Forward", "Standard transport-to-number conventions");
            Check(Codec.Rco9(Codec.Order(Codec.Hex("01020304"), reader.MifareOrder)) == "102700513", "Standard four-byte MIFARE block number");
            Check(Codec.Rco9(Codec.Order(Codec.Hex("04010203040506"), reader.MifareOrder)) == "077000260", "Standard seven-byte MIFARE block number");
            Check(Codec.Rco9(Codec.Order(Codec.Hex("0102030405"), reader.EmOrder)) == "051501029", "Standard EM block number");
            ScanResult scanned = reader.Scan("Both", false, System.Threading.CancellationToken.None, fake.Progress);
            Check(scanned.Mr == "102700513" && scanned.Er == "051501029", "Fresh reader completes both scan phases without reference data");
            Check(!reader.HasPendingRecovery && fake.Restored, "Fresh-reader scan restores every changed setting");
            Check(reader.Cal == null && !File.Exists(Path.Combine(directory, ReferenceCard.FileName)), "Normal scanning never manufactures a reference/calibration");
            string profile = Path.Combine(directory, "byteordning-" + reader.ReaderId + ".json");
            Calibration cal = new Calibration { Schema = 2, ReaderId = reader.ReaderId, Firmware = reader.Firmware, MifareOrder = "Forward", EmOrder = "Reverse" };
            string text = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cal);
            File.WriteAllText(profile, text); reader.Connect();
            Check(reader.CanRead && reader.Cal != null && reader.MifareOrder == "Forward" && reader.EmOrder == "Reverse", "Existing verified overrides keep their numbers");
            Check(File.ReadAllText(profile) == text, "Connection leaves existing profile bytes intact");
            File.WriteAllText(profile, "{"); reader.Connect();
            Check(!reader.CanRead && !String.IsNullOrEmpty(reader.CalibrationIssue), "Corrupt override fails closed");
            cal.Firmware = "00000000"; File.WriteAllText(profile, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cal)); reader.Connect();
            Check(!reader.CanRead, "Firmware change does not silently discard a calibration");
            File.WriteAllText(profile, new string('x', 16385)); reader.Connect(); Check(!reader.CanRead, "Oversize override rejected");
            File.Delete(profile); fake.Board = 10; reader.Connect();
            Check(reader.CanRead && reader.Board.Contains("Gen2"), "Gen2 accepted only after same settings preflight");
            fake.BadSettings = true; bool rejected = false;
            try { reader.Connect(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !reader.CanRead, "Unsupported settings leave reader unready");
            fake.BadSettings = false; fake.Board = 9; rejected = false;
            try { reader.Connect(); } catch (NotSupportedException) { rejected = true; }
            Check(rejected && !reader.CanRead, "Other models remain rejected");
            Check(FirstStart.DriverUrl == "https://www.hidglobal.com/drivers/34881", "Driver uses official HTTPS page without bundled installers");
            using (FirstStartDialog dialog = new FirstStartDialog(""))
            {
                dialog.Show(); Application.DoEvents();
                ((Button)Find(dialog, "Försök ansluta", false)).PerformClick();
                Check(dialog.DialogResult == DialogResult.OK && !dialog.OpenDriver, "Retry does not request driver installation");
            }
            using (FirstStartDialog dialog = new FirstStartDialog(""))
            {
                dialog.Show(); Application.DoEvents(); ((Button)Find(dialog, "Hämta USB-drivrutin", false)).PerformClick();
                Check(dialog.DialogResult == DialogResult.OK && dialog.OpenDriver, "Only explicit selection opens the driver page");
            }
        }
        private static void ExerciseUpdates(string directory)
        {
            byte[] fixture = Convert.FromBase64String("QlJJQ0tMQVNBUkUtVVBEQVRFLTEKZXlKVFkyaGxiV0VpT2pFc0lrTm9ZVzV1Wld3aU9pSndjbVYyYVdWM0lpd2lWMmx1Wkc5M2N5STZleUpXWlhKemFXOXVJam9pT1RrdU1TNHdJaXdpVlhKc0lqb2lhSFIwY0hNNkx5OW5hWFJvZFdJdVkyOXRMM1J6ZEdWdWEzVnNZUzlpY21samEyeGhjMkZ5WlM5eVpXeGxZWE5sY3k5a2IzZHViRzloWkM5MFpYTjBMMlpwZUhSMWNtVXVlbWx3SWl3aVUyaGhNalUySWpvaVpqRTJaREExWldNMllqSTVNalE0WkRKak5qRmhaR0l4WlRreU5qTm1OemhsTkdZM1ltRmpaVEZpT1RVMU1ERTBZVEprTVRjNE56SmpabVUwTURZMFpDSXNJbE5wZW1VaU9qY3NJazV2ZEdWeklqb2lVM2x1ZEdobGRHbGpJSFJsYzNRZ1ptbDRkSFZ5WlRzZ2JtVjJaWElnWkc5M2JteHZZV1JsWkM0aWZTd2lUV0ZqSWpwN0lsWmxjbk5wYjI0aU9pSTVPUzR4TGpBaUxDSlZjbXdpT2lKb2RIUndjem92TDJkcGRHaDFZaTVqYjIwdmRITjBaVzVyZFd4aEwySnlhV05yYkdGellYSmxMM0psYkdWaGMyVnpMMlJ2ZDI1c2IyRmtMM1JsYzNRdlptbDRkSFZ5WlM1NmFYQWlMQ0pUYUdFeU5UWWlPaUptTVRaa01EVmxZelppTWpreU5EaGtNbU0yTVdGa1lqRmxPVEkyTTJZM09HVTBaamRpWVdObE1XSTVOVFV3TVRSaE1tUXhOemczTW1ObVpUUXdOalJrSWl3aVUybDZaU0k2Tnl3aVRtOTBaWE1pT2lKVGVXNTBhR1YwYVdNZ2RHVnpkQ0JtYVhoMGRYSmxPeUJ1WlhabGNpQmtiM2R1Ykc5aFpHVmtMaUo5ZlE9PQpBc0pXSEJXTFB3M0VIRWNrT244Z0IydFM3ODNzY1BhRUlMQkYvL24xMTNqRjE1QmRHektMTnN3QzdmN3hSRnA5ZVRSSEtwSWlFTGg3L1A3WXE5U3NPcHV2cVppUGorS2RnbG8rTG52MzUzZHJJa0pNMG9tMklZUlJKOEJ3RTQ3N2NRWHpVQTJFSGplYncvWnRLS3ZEaFdBREN3cGw1UlpKS0kyWVUyOXk5blhVbjFDLzhYR2d5UU9ld1BDbDZaNVVPMHROeW5JdHRjMTdxVTJWckFnUGVDYUxIWmhTdG9VaEs5a3NlaUx3UDFtMk1YMkNheEllMFFkM2dPaDdBdlkrY0NaYmJQWE53N05hV2FrV1B6VXc0M1ZTTUdWRmRTYXVXblRROExQWnd6YmZDWE9VMnpPZnhmYitFQ2F3QmdsQ1dvVm1pV3pkeVBWNzc1b05KM3BMN0krU0tmV0tHcGFGVEljNmdoUUQraE9QNWdZZ2pLUUJaQkh4VUEwWE96UW5WSjlHYml5U2Q1TGRyRW9LNU9yZS9COTdickN6RXJtZXR3T2p1NVNnSnRJY0Y0SUVjcEYrQ0E4RWhNZmJWaXdEekFaQUlybHhXc2dMU3NuRXdKKzR2T3VtUUxZdkRPT0FSR0s4T2JqQkZmcGhZSUYwdDdNNnk1enNLMHhsSXRIZgo=");
            UpdateAsset asset = Updates.Decode(fixture, "preview");
            Check(asset.Version == "99.1.0", "Signed feed accepted");
            Reject(delegate { Updates.Decode(fixture, "stable"); }, "Channel substitution rejected");
            byte[] changed = (byte[])fixture.Clone(); changed[40] ^= 1;
            Reject(delegate { Updates.Decode(changed, "preview"); }, "Tampered feed rejected");
            Updates.VerifyPackage(System.Text.Encoding.UTF8.GetBytes("fixture"), asset);
            Reject(delegate { Updates.VerifyPackage(System.Text.Encoding.UTF8.GetBytes("changed"), asset); }, "Tampered archive rejected");
            foreach (string v in new string[] { "3.5", "3.5.0.1", "3.5.-1", "65536.0.0", "3.5.0\n", "x.0.0" }) Reject(delegate { Updates.ParseVersion(v); }, "Invalid version rejected");
            Check(Updates.ParseVersion("3.10.0") > Updates.ParseVersion("3.9.0"), "Numeric version comparison");
            Check(!new WorkflowSettings().Sounds && new WorkflowSettings().Copy == 0, "Sounds and automatic clipboard opt in");
            Check(WorkflowSettings.CopyValue(2, "123", "—", false) == "", "Missing second number not copied");
            Check(WorkflowSettings.CopyValue(3, "123", "456", false) == "", "Both only for combo mode");
            string root = Path.Combine(directory,"update-tests"); Directory.CreateDirectory(root);
            string releaseArchive = Environment.GetEnvironmentVariable("BRICKLASARE_TEST_ARCHIVE");
            if (!String.IsNullOrEmpty(releaseArchive)) {
                string released = Path.Combine(root, "released");
                Updates.Extract(File.ReadAllBytes(releaseArchive), released);
                foreach (string file in ReceptionFiles.RequiredPackageFiles) Check(File.Exists(Path.Combine(released, file)), "Released archive contains " + file);
            }
            Check(!Updates.Pending(root), "No pending recovery");
            File.WriteAllText(Path.Combine(root,"aterstall-test.json"),"pending"); Check(Updates.Pending(root), "Pending recovery blocks installation");
            using (MemoryStream bytes = new MemoryStream()) {
                using (var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create, true)) { var entry = zip.CreateEntry("../escape"); using (var writer = new StreamWriter(entry.Open())) writer.Write("escape"); }
                Reject(delegate { Updates.Extract(bytes.ToArray(),Path.Combine(root,"extract")); }, "Archive traversal rejected");
                Check(!File.Exists(Path.Combine(root,"escape")), "Traversal never writes outside staging");
            }
        }
        private sealed class FakeReader
        {
            internal byte Board = 6; internal int Writes; internal bool BadSettings;
            private bool present;
            private readonly System.Collections.Generic.Dictionary<string, Tlv> settings = new System.Collections.Generic.Dictionary<string, Tlv>();
            internal FakeReader()
            {
                foreach (PollSetting poll in ReaderClient.Polls) settings.Add(poll.Oid, new Tlv { Tag = 1, Value = new byte[] { 255 } });
                settings.Add(ReaderClient.AskDataOid, new Tlv { Tag = 4, Value = new byte[8] });
            }
            internal bool Restored { get { return settings[ReaderClient.AskDataOid].Value.All(b => b == 0) && ReaderClient.Polls.All(p => settings[p.Oid].Value[0] == 255); } }
            internal void Progress(string message)
            {
                if (message.Contains("Lägg")) present = true;
                if (message.Contains("Lyft") || message.Contains("Ta bort")) present = false;
            }
            internal byte[] Send(string command)
            {
                if (command == "FFCA000000") return Codec.Hex("010203049000");
                if (command == "FF70076B07A005A10380010400") return Codec.Hex("9D0803060001020304059000");
                if (command == "FF6800010101") return Codec.Hex("9000");
                byte[] cmd = Codec.Hex(command); int p = 8; Tlv oid = Codec.ReadTlv(cmd, ref p); Tlv value = Codec.ReadTlv(cmd, ref p);
                string key = Codec.ToHex(oid.Value);
                if (cmd[5] == 2) { Writes++; settings[key] = value; }
                else if (key == "010200") value = new Tlv { Tag = 2, Value = new byte[] { Board } };
                else if (key == "010100") value = new Tlv { Tag = 2, Value = Codec.Hex("01050176") };
                else if (key == "010A00") value = new Tlv { Tag = 4, Value = Enumerable.Repeat((byte)0xAA, 16).ToArray() };
                else if (key == "0302010300") value = new Tlv { Tag = 4, Value = present ? Codec.Hex("3B00") : new byte[0] };
                else value = BadSettings ? new Tlv { Tag = 4, Value = new byte[0] } : settings[key];
                return Codec.Hex("0300000D" + oid.Value.Length.ToString("X2") + key + value.Tag.ToString("X2") + value.Value.Length.ToString("X2") + Codec.ToHex(value.Value) + "9000");
            }
        }
        private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        private static Control Find(Control parent, string text, bool accessible)
        {
            foreach (Control child in parent.Controls)
            {
                if ((accessible ? child.AccessibleName : child.Text) == text) return child;
                Control found = Find(child, text, accessible); if (found != null) return found;
            }
            return null;
        }
        private static void ExerciseSettingsActions(string directory)
        {
            string path = Path.Combine(directory, AppearanceFiles.FileName), before = File.ReadAllText(path);
            using (AppearanceDialog dialog = new AppearanceDialog(directory))
            {
                dialog.Show(); dialog.ClientSize = new Size(560, 440); Application.DoEvents();
                Button save = (Button)Find(dialog, "Spara utseende", false);
                Rectangle location = dialog.RectangleToClient(save.RectangleToScreen(save.ClientRectangle));
                Check(dialog.ClientRectangle.Contains(location), "Save stays visible on a small window");
                TextBox color = (TextBox)Find(dialog, "Accentfärg i hexadecimal form", true);
                color.Text = "#xyz"; Check(!save.Enabled, "Invalid color disables Save in the editor");
                color.Text = "#FFFF00"; Check(save.Enabled, "Valid custom color enables Save");
                ((TextBox)Find(dialog, "Organisationens namn", true)).Text = "Unsaved change";
                ((Button)Find(dialog, "Avbryt", false)).PerformClick();
            }
            Check(File.ReadAllText(path) == before, "Cancel discards edited appearance");
            using (AppearanceDialog dialog = new AppearanceDialog(directory))
            {
                dialog.Show(); Application.DoEvents();
                ((TextBox)Find(dialog, "Accentfärg i hexadecimal form", true)).Text = "#FFFF00";
                ((TextBox)Find(dialog, "Organisationens namn", true)).Text = "Saved appearance";
                ((Button)Find(dialog, "Spara utseende", false)).PerformClick();
            }
            Check(AppearanceFiles.Load(directory).Organization == "Saved appearance" && AppearanceFiles.Load(directory).AccentHex == "#FFFF00", "Editor Save persists chosen text and color");
            before = File.ReadAllText(path);
            using (AppearanceDialog dialog = new AppearanceDialog(directory))
            {
                dialog.Show(); Application.DoEvents();
                ((Button)Find(dialog, "Återställ", false)).PerformClick();
                Check(File.ReadAllText(path) == before, "Reset remains a preview until saved");
                ((Button)Find(dialog, "Spara utseende", false)).PerformClick();
            }
            Check(AppearanceFiles.Load(directory).Organization == "" && AppearanceFiles.Load(directory).AccentHex == AppearanceSettings.DefaultAccentHex && AppearanceFiles.Load(directory).LogoFile == "", "Editor Reset then Save clears customization");
        }
        private static void ExerciseDialogs(string directory, int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                using (AppearanceDialog dialog = new AppearanceDialog(directory))
                {
                    dialog.Show(); Application.DoEvents();
                    dialog.ReplaceLogo(new Bitmap(100, 40)); dialog.ReplaceLogo(new Bitmap(40, 40));
                }
                using (ReferenceDialog dialog = new ReferenceDialog(directory)) { dialog.Show(); Application.DoEvents(); }
                using (FormatSelector dialog = new FormatSelector("Kombi", "M")) { dialog.Show(); Application.DoEvents(); }
                using (ReceptionHelp dialog = new ReceptionHelp("192.168.63.99", null, "Kombi", "M", false)) { dialog.Show(); Application.DoEvents(); }
            }
        }
        private static void Render(Form form, string name)
        {
            using (form)
            {
                string output = Environment.GetEnvironmentVariable("BRICKLASARE_TEST_IMAGES");
                if (String.IsNullOrEmpty(output)) return;
                Directory.CreateDirectory(output);
                form.Show(); Application.DoEvents(); form.Refresh();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                }
            }
        }
    }
}
