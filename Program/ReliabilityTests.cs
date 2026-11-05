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
                count += ReaderNetworkTests.Run();
                ExerciseHeaderLayout(Path.Combine(directory, "header-layout"));
                ExerciseConnectionDiagnostics(Path.Combine(directory, "connection-diagnostics"));
                ExerciseReaderSound(Path.Combine(directory, "reader-sound"));
                ExerciseUpdates(directory);
                ExerciseFirstStart(Path.Combine(directory, "first-start"));
                ExerciseReaderWorkspace(Path.Combine(directory, "workspace"));
                ExerciseLateReaderCompletion(Path.Combine(directory, "late-completion"));
                ExerciseConfirmedOperations(Path.Combine(directory, "confirmed-operations"));
                ExerciseDownloadProgress(Path.Combine(directory, "download-progress"));
                ExerciseWorkflow(Path.Combine(directory, "workflow"));
                ExerciseExport(Path.Combine(directory, "export"));
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
                ExerciseDisplayLogo(Path.Combine(directory, "display-logo"));
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
        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(instance);
        }
        private static void SetField(object instance, string name, object value)
        {
            instance.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(instance, value);
        }
        private static object Call(object instance, string name, params object[] arguments)
        {
            return instance.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(instance, arguments);
        }
        private static ReceptionForm ReaderPreview(string directory, string mode)
        {
            Directory.CreateDirectory(directory);
            FakeReader fake = new FakeReader();
            ReaderClient reader = new ReaderClient("192.168.63.99", directory, new LocalLog()); reader.TestTransport = fake.Send; reader.Connect();
            ReceptionForm form = new ReceptionForm(true, directory);
            SetField(form, "reader", reader); SetField(form, "readerSound", (bool?)true); SetField(form, "profileChosen", true); SetField(form, "outputProtected", true); SetField(form, "mode", mode);
            Call(form, "ApplyModeUi"); return form;
        }
        private static void PreviewResults(ReceptionForm form, string first, string second)
        {
            // Fixtures only: these are fabricated numbers, never values from a connected reader.
            Field<TextBox>(form, "mr").Text = first; Field<TextBox>(form, "er").Text = second;
            Call(form, "SetInstruction", "KLART", "Läsningen är klar", "Kopiera numret du behöver. Läs sedan nästa bricka.", false);
            Call(form, "UpdateButtons");
        }
        private static bool Inside(Form form, Control control)
        {
            return control.Visible && form.ClientRectangle.Contains(form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle)));
        }
        private static void ExerciseReaderWorkspace(string directory)
        {
            using (ReceptionForm form = ReaderPreview(directory, FormatProfiles.Combo))
            {
                form.Show(); form.Activate(); Application.DoEvents();
                Check(Field<Label>(form, "leftState").Text == "Väntar på läsning", "Empty result explains that a scan is needed");
                Check(!Field<Button>(form, "clear").Enabled && !Field<Button>(form, "copyM").Enabled, "Empty results cannot be copied or cleared");
                Check(Field<TextBox>(form, "mr").Font.Size >= 32F, "Reader numbers remain large");
                Check(Inside(form, Field<Button>(form, "read")) && Inside(form, Field<Button>(form, "copyE")), "Read and result actions fit the default workspace");
                Call(form, "SetBusy", true); Check(Field<TextBox>(form, "keyboardSink").Focused, "Busy reader input stays in the protected sink");
                Call(form, "SetBusy", false); Check(Field<Button>(form, "read").Focused, "Connection completion returns keyboard focus to Read");
                Check((bool)Call(form, "ProcessCmdKey", new Message(), Keys.Enter) && (bool)Call(form, "ProcessCmdKey", new Message(), Keys.D5), "Reader Enter and digit filtering stays enabled");
                PreviewResults(form, "102700513", "051501029");
                Check(Field<Button>(form, "clear").Enabled && Field<Button>(form, "read").Text == "Läs nästa bricka", "Finished results expose next scan and clear actions");
                Check(Field<Button>(form, "copyM").AccessibleDescription.Contains("Ctrl+1") && Field<TextBox>(form, "mr").AccessibleName.Contains("MIFARE"), "Number and copy controls identify their purpose");
                Field<Button>(form, "clear").PerformClick();
                Check(Field<TextBox>(form, "mr").Text == "—" && Field<TextBox>(form, "er").Text == "—" && !Field<Button>(form, "clear").Enabled, "Clear removes both displayed results");
                Check(Field<Label>(form, "instruction").Text == "Läs en ny bricka" && Field<Button>(form, "read").Focused, "Clear returns to the ready step and keyboard action");
                SetField(form, "reader", null); SetField(form, "outputProtected", false);
                Call(form, "SetBusy", true); Call(form, "SetBusy", false);
                Check(Field<Button>(form, "setup").Focused, "Failed connection focuses the available connection action");
            }
            Render(ReaderPreview(directory, FormatProfiles.Combo), "arbetsyta-klar");
            using (System.Threading.CancellationTokenSource cancellation = new System.Threading.CancellationTokenSource())
            {
                ReceptionForm reading = ReaderPreview(directory, FormatProfiles.Combo); SetField(reading, "cancellation", cancellation);
                Call(reading, "SetBusy", true);
                Call(reading, "SetInstruction", "STEG 1 AV 2", "Lägg på brickan", "Håll kvar tills programmet ber dig lyfta bort den.", false);
                Render(reading, "arbetsyta-laser");
            }
            ReceptionForm complete = ReaderPreview(directory, FormatProfiles.Combo); PreviewResults(complete, "102700513", "051501029"); Render(complete, "arbetsyta-resultat");
            ReceptionForm problem = ReaderPreview(directory, FormatProfiles.Combo); SetField(problem, "outputProtected", false);
            Field<Label>(problem, "connection").Text = "Läsaren behöver återställas";
            Call(problem, "SetInstruction", "HJÄLP BEHÖVS", "Läsaren behöver återställas", "Låt USB-kabeln sitta i. Välj Hjälp och be IT hjälpa dig innan nästa bricka.", true);
            Call(problem, "UpdateButtons"); Render(problem, "arbetsyta-hjalp-behovs");
            ReceptionForm rco = ReaderPreview(directory, FormatProfiles.Rco); PreviewResults(rco, "102700513", "0016909060"); Render(rco, "arbetsyta-rco");
            ReceptionForm vaka = ReaderPreview(directory, FormatProfiles.Axema); PreviewResults(vaka, "00123456789012", "—"); Render(vaka, "arbetsyta-vaka");
            ReceptionForm small = ReaderPreview(directory, FormatProfiles.Combo); small.ClientSize = new Size(800, 600); PreviewResults(small, "102700513", "051501029");
            small.Show(); Application.DoEvents();
            Check(Inside(small, Field<Button>(small, "copyE")) && Inside(small, Field<Button>(small, "clear")), "Small workspace keeps copy and clear actions visible");
            Render(small, "arbetsyta-kompakt");
            ReceptionForm scaled = ReaderPreview(directory, FormatProfiles.Rco); PreviewResults(scaled, "102700513", "0016909060");
            scaled.Scale(new SizeF(1.5F, 1.5F)); scaled.ClientSize = new Size(1200, 900); scaled.Show(); Application.DoEvents();
            Check(Inside(scaled, Field<Button>(scaled, "copyE")) && Inside(scaled, Field<Button>(scaled, "read")), "Scaled workspace keeps primary and result actions visible");
            Render(scaled, "arbetsyta-skalad-150");
        }
        private static void RejectPublication(ReceptionForm form, ReaderClient reader, System.Threading.CancellationToken token, string description)
        {
            bool rejected = false;
            try { Call(form, "PublishScanResult", new ScanResult { Mr = "102700513", Er = "051501029" }, reader, FormatProfiles.Combo, "M", false, token); }
            catch (System.Reflection.TargetInvocationException ex) { if (!(ex.InnerException is OperationCanceledException)) throw; rejected = true; }
            Check(rejected && Field<TextBox>(form, "mr").Text == "—" && Field<TextBox>(form, "er").Text == "—" && Field<string>(form, "lastCopied") == "", description);
        }
        private static void ExerciseLateReaderCompletion(string directory)
        {
            using (ReceptionForm form = ReaderPreview(directory, FormatProfiles.Combo))
            using (System.Threading.CancellationTokenSource cancelled = new System.Threading.CancellationTokenSource())
            {
                ReaderClient reader = Field<ReaderClient>(form, "reader");
                Field<WorkflowSettings>(form, "workflow").Copy = 1;
                cancelled.Cancel();
                RejectPublication(form, reader, cancelled.Token, "Late completion after cancellation cannot publish results for automatic copying");
                Call(form, "InvalidateReaderConnection");
                // Simulate the final protection await returning after USB invalidation.
                SetField(form, "outputProtected", true);
                RejectPublication(form, reader, System.Threading.CancellationToken.None, "Late restoration cannot republish results from an invalidated USB connection");
                Call(form, "UpdateButtons");
                Check(!Field<Button>(form, "read").Enabled && !Field<Button>(form, "copyM").Enabled, "Pending reconnection blocks reading and copying");
                Check(!(bool)Call(form, "TryBeginFileOperation", true) && !Field<bool>(form, "busy"), "Export cannot claim an invalidated reader after the folder dialog");
                SetField(form, "reconnectPending", false);
                SetField(form, "reader", null);
                RejectPublication(form, reader, System.Threading.CancellationToken.None, "A detached reader cannot publish a late result");
                Check(!(bool)Call(form, "TryBeginFileOperation", true), "Export rejects a reader lost while its dialog was open");
                SetField(form, "reader", reader);
                Call(reader, "set_CalibrationIssue", "Synthetic profile failure");
                Check(!reader.CanRead, "Late-result fixture has a reader that is no longer ready");
                RejectPublication(form, reader, System.Threading.CancellationToken.None, "Completed numbers from a reader that is no longer ready are not displayed or copied");
                Call(reader, "set_CalibrationIssue", new object[] { null });
                SetField(form, "profileChosen", false);
                RejectPublication(form, reader, System.Threading.CancellationToken.None, "Normal results require a chosen access-system format at publication");
                SetField(form, "profileChosen", true);
                Call(form, "SetBusy", true);
                Check(!(bool)Call(form, "TryBeginFileOperation", false) && Field<bool>(form, "busy"), "Report saving never takes ownership from an in-flight reconnect");
                Check(!(bool)Call(form, "TryBeginFileOperation", true) && Field<bool>(form, "busy"), "Export never takes ownership from an in-flight reconnect");
                Call(form, "SetBusy", false);
                Check((bool)Call(form, "TryBeginFileOperation", true) && Field<bool>(form, "busy"), "Ready export acquires its own busy lifetime");
                Call(form, "SetBusy", false);
                Call(form, "PublishScanResult", new ScanResult { Mr = "102700513", Er = "051501029" }, reader, FormatProfiles.Combo, "M", false, System.Threading.CancellationToken.None);
                Check((bool)Call(form, "get_HasResults") && Field<Label>(form, "instruction").Text == "Läsningen är klar", "Healthy completion still publishes the validated numbers");
            }
        }
        private static System.Threading.Tasks.Task<bool> ConfirmedUpdate(ReceptionForm form, string directory, Func<Action<UpdateProgress>, System.Threading.Tasks.Task> operation)
        {
            return (System.Threading.Tasks.Task<bool>)Call(form, "RunConfirmedUpdate", Field<WorkflowSettings>(form, "workflow").Channel, directory, operation);
        }
        private static void ExerciseConfirmedOperations(string directory)
        {
            Directory.CreateDirectory(directory);
            using (ReceptionForm form = new ReceptionForm(true, directory))
            {
                form.Show(); Application.DoEvents();
                int calls = 0;
                Func<Action<UpdateProgress>, System.Threading.Tasks.Task> work = delegate(Action<UpdateProgress> progress) { calls++; return System.Threading.Tasks.Task.FromResult(true); };
                Call(form, "SetBusy", true);
                Check(!ConfirmedUpdate(form, directory, work).Result && calls == 0 && Field<bool>(form, "busy"), "Update confirmation does not take busy ownership from reconnect");
                Call(form, "SetBusy", false); SetField(form, "reconnectPending", true);
                Check(!ConfirmedUpdate(form, directory, work).Result && calls == 0 && !Field<bool>(form, "busy"), "Update confirmation rejects a newly pending reconnect");
                SetField(form, "reconnectPending", false);
                string pending = Path.Combine(directory, "usb-pending-confirmation.json"); File.WriteAllText(pending, "synthetic");
                Check(!ConfirmedUpdate(form, directory, work).Result && calls == 0, "Update confirmation rechecks recovery journals after the dialog"); File.Delete(pending);
                Check(ConfirmedUpdate(form, directory, work).Result && calls == 1 && !Field<bool>(form, "busy"), "Offline update needs no reader and releases its own busy state");
                System.Threading.Tasks.TaskCompletionSource<bool> gate = new System.Threading.Tasks.TaskCompletionSource<bool>();
                Action<UpdateProgress> late = null;
                System.Threading.Tasks.Task<bool> running = ConfirmedUpdate(form, directory, delegate(Action<UpdateProgress> progress) { late = progress; return gate.Task; });
                Check(!running.IsCompleted && Field<bool>(form, "busy"), "Asynchronous preparation retains its busy ownership");
                SetField(form, "reconnectPending", true);
                Check(!ConfirmedUpdate(form, directory, work).Result && calls == 1 && Field<bool>(form, "busy"), "A rejected second update cannot release the active operation");
                gate.SetException(new IOException("synthetic download failure")); Application.DoEvents();
                Check(running.IsCompleted, "The failed update continuation completes on the UI thread");
                bool failed = false; try { running.GetAwaiter().GetResult(); } catch (IOException) { failed = true; }
                Check(failed && !Field<bool>(form, "busy") && Field<bool>(form, "reconnectPending"), "Failed asynchronous update releases only its own busy state");
                string before = Field<Label>(form, "updateTitle").Text;
                late(new UpdateProgress(UpdatePhase.Download, 50, 100));
                Check(!Field<UiCard>(form, "updatePanel").Visible && Field<Label>(form, "updateTitle").Text == before, "Late download callback cannot revive a finished progress view");
            }
            using (ReceptionForm form = ReaderPreview(directory, FormatProfiles.Combo))
            {
                ReaderClient expected = Field<ReaderClient>(form, "reader");
                Call(form, "SetBusy", true);
                Check(!(bool)Call(form, "TryBeginReaderOperation", expected, false) && Field<bool>(form, "busy"), "Reader confirmation cannot take busy ownership from reconnect"); Call(form, "SetBusy", false);
                SetField(form, "reconnectPending", true);
                Check(!(bool)Call(form, "TryBeginReaderOperation", expected, false), "Reader confirmation rejects USB changes during the dialog"); SetField(form, "reconnectPending", false);
                FakeReader fake = new FakeReader(); ReaderClient replacement = new ReaderClient("192.168.63.99", directory, new LocalLog()); replacement.TestTransport = fake.Send; replacement.Connect();
                SetField(form, "reader", replacement);
                Check(!(bool)Call(form, "TryBeginReaderOperation", expected, false), "Profile import and USB restore reject a replaced reader object"); SetField(form, "reader", expected);
                Call(expected, "set_CalibrationIssue", "Synthetic calibration issue");
                Check((bool)Call(form, "TryBeginReaderOperation", expected, false), "Profile repair remains available when ordinary reading is not ready"); Call(form, "SetBusy", false);
                string journal = Path.Combine(directory, "aterstall-" + expected.ReaderId + ".json"); File.WriteAllText(journal, "synthetic");
                Check(!(bool)Call(form, "TryBeginReaderOperation", expected, false), "A newly pending recovery blocks profile import and USB mode restoration");
                Check((bool)Call(form, "TryBeginReaderOperation", expected, true), "Recovery can claim the matching reader despite not being ready to scan"); Call(form, "SetBusy", false); File.Delete(journal);
                Check(!(bool)Call(form, "TryBeginReaderOperation", expected, true), "Recovery confirmation rejects a journal removed while its dialog was open");
            }
        }
        private sealed class SmallReads : MemoryStream
        {
            internal SmallReads(byte[] bytes) : base(bytes) { }
            public override int Read(byte[] buffer, int offset, int count) { return base.Read(buffer, offset, Math.Min(count, 10)); }
        }
        private static void ExerciseDownloadProgress(string directory)
        {
            Directory.CreateDirectory(directory);
            System.Collections.Generic.List<UpdateProgress> progress = new System.Collections.Generic.List<UpdateProgress>();
            long elapsed = 0;
            using (SmallReads input = new SmallReads(new byte[1000]))
                Check(Updates.ReadDownload(input, 2000, 1000, progress.Add, delegate { return elapsed += 10; }).Length == 1000, "Progress reads the exact signed package length");
            Check(progress.Count <= 12 && progress[0].Percent == 0 && progress.Last().Percent == 100, "Download reports are throttled with exact start and EOF completion");
            Check(progress.Take(progress.Count - 1).All(p => p.Percent < 100) && progress.All(p => p.Total == 1000), "Only exact EOF reaches 100 percent against the signed total");
            progress.Clear();
            Reject(delegate { using (MemoryStream input = new MemoryStream(new byte[3])) Updates.ReadDownload(input, 20, 4, progress.Add, null); }, "Truncated signed download fails");
            Check(progress.All(p => p.Percent < 100), "Truncated download never reports completion"); progress.Clear();
            Reject(delegate { using (MemoryStream input = new MemoryStream(new byte[5])) Updates.ReadDownload(input, 20, 4, progress.Add, null); }, "Oversize signed download fails before completion");
            Check(progress.All(p => p.Percent < 100), "Oversize download never reports completion");
            Reject(delegate { using (MemoryStream input = new MemoryStream(new byte[4])) Updates.ReadDownload(input, 20, 4, null, delegate { return 180001; }); }, "Download progress preserves the overall time bound");
            Check(new UpdateProgress(UpdatePhase.Download, 500000, 1000000).Detail == "50 % · 0,50 av 1,00 MB", "Progress displays actual percentage and readable decimal megabytes");
            Reject(delegate { new UpdateProgress(UpdatePhase.Download, 5, 4); }, "Progress rejects bytes beyond the signed length");
            using (ReceptionForm form = new ReceptionForm(true, directory))
            {
                form.Show(); Application.DoEvents();
                Call(form, "SetBusy", true); SetField(form, "updateProgressGeneration", 1); Field<UiCard>(form, "updatePanel").Visible = true;
                Call(form, "SetInstruction", "UPPDATERAR", "Hämtar en ny version", "Låt programmet arbeta klart. Dina inställningar sparas.", false);
                Call(form, "ShowUpdateProgress", new UpdateProgress(UpdatePhase.Download, 620000, 1000000), 1);
                Check(Field<UpdateProgressBar>(form, "updateBar").Percent == 62 && !Field<UpdateProgressBar>(form, "updateBar").Indeterminate, "Actual download progress reaches the visible bar");
                RenderSnapshot(form, "uppdatering-hamtar");
                Call(form, "ShowUpdateProgress", new UpdateProgress(UpdatePhase.Verify, 1000000, 1000000, true), 1);
                Check(Field<UpdateProgressBar>(form, "updateBar").Indeterminate && Field<Label>(form, "updateTitle").Text == "Kontrollerar uppdateringen", "Verification has a distinct phase without invented percentage");
                RenderSnapshot(form, "uppdatering-kontrollerar");
                Call(form, "ShowUpdateProgress", new UpdateProgress(UpdatePhase.PrepareInstallation, 1000000, 1000000, true), 1);
                Check(Field<Label>(form, "updateTitle").Text == "Förbereder installation", "Preparation never claims the separate installer has completed");
                RenderSnapshot(form, "uppdatering-forbereder");
                form.ClientSize = new Size(800, 600);
                Call(form, "ShowUpdateProgress", new UpdateProgress(UpdatePhase.Download, 370000, 1000000), 1); Application.DoEvents();
                Check(Inside(form, Field<UpdateProgressBar>(form, "updateBar")) && Inside(form, Field<Label>(form, "updateDetail")) && Inside(form, Field<Button>(form, "copyE")), "Compact download progress and result actions remain in view");
                RenderSnapshot(form, "uppdatering-hamtar-kompakt");
                Call(form, "ShowUpdateProgress", new UpdateProgress(UpdatePhase.Verify, 1000000, 1000000, true), 1); Application.DoEvents();
                Check(Inside(form, Field<Label>(form, "updateDetail")) && Inside(form, Field<Button>(form, "copyE")), "Compact verification state retains visible text and result actions");
                RenderSnapshot(form, "uppdatering-kontrollerar-kompakt");
            }
        }
        private static void ExerciseWorkflow(string directory)
        {
            Directory.CreateDirectory(directory); string path = Path.Combine(directory, "arbetsflode.json");
            using (WorkflowDialog dialog = new WorkflowDialog(new WorkflowSettings(), directory))
            {
                dialog.Show(); dialog.ClientSize = new Size(530, 360); Application.DoEvents();
                Check(dialog.AutoScaleMode == AutoScaleMode.Dpi && dialog.AcceptButton != null && dialog.CancelButton != null, "Workflow supports DPI and standard save/cancel keys");
                Check(Inside(dialog, (Control)dialog.AcceptButton) && Inside(dialog, (Control)dialog.CancelButton), "Workflow footer remains visible in a small window");
                ((ComboBox)Find(dialog, "Kopiera automatiskt", true)).SelectedIndex = 2;
                ((Button)dialog.CancelButton).PerformClick();
            }
            Check(!File.Exists(path), "Cancelling workflow changes creates no settings");
            using (WorkflowDialog dialog = new WorkflowDialog(new WorkflowSettings(), directory))
            {
                dialog.Show(); Application.DoEvents();
                ((ComboBox)Find(dialog, "Kopiera automatiskt", true)).SelectedIndex = 3;
                ((ComboBox)Find(dialog, "Versioner", true)).SelectedIndex = 1;
                ((Button)dialog.AcceptButton).PerformClick();
            }
            WorkflowSettings saved = WorkflowSettings.Load(directory);
            Check(saved.Copy == 3 && saved.Channel == "preview", "Workflow save retains the chosen values");
        }
        private static void ExerciseHeaderLayout(string directory)
        {
            foreach (Rectangle work in new[] { new Rectangle(0, 0, 1280, 680), new Rectangle(-1280, 32, 1280, 688), new Rectangle(1920, -900, 1600, 860) })
            {
                foreach (Rectangle initial in new[] { new Rectangle(work.Left + 400, work.Bottom - 180, 610, 1050),
                    new Rectangle(work.Left - 200, work.Top - 120, 915, 1530) })
                {
                    Rectangle fit = Ui.FitWindowBounds(initial, work);
                    Check(work.Contains(fit) && fit.Width > 0 && fit.Height > 0, "Expanded/scaled settings remain inside taskbar-excluded working area, including negative monitor coordinates");
                }
            }
            Directory.CreateDirectory(directory); AppearanceSettings settings = AppearanceSettings.Default();
            settings.Organization = "Testorganisation"; settings.Subtitle = "Entré nord";
            using (Bitmap square = new Bitmap(128, 128))
            {
                using (Graphics graphics = Graphics.FromImage(square)) { graphics.Clear(Color.Transparent); graphics.FillRectangle(Brushes.Navy, 4, 4, 120, 120); }
                AppearanceFiles.Save(directory, settings, square);
            }
            using (ReceptionForm form = ReaderPreview(directory, FormatProfiles.Combo))
            {
                form.Show(); Application.DoEvents();
                Label title = Field<Label>(form, "brand"), subtitle = Field<Label>(form, "brandSubtitle");
                PictureBox logo = Field<PictureBox>(form, "logo");
                Check(title.Font.SizeInPoints > subtitle.Font.SizeInPoints, "Organization title stays larger than its subtitle");
                Check(logo.Width == logo.Height && logo.Left == title.Left && title.Left == subtitle.Left, "Square logo has no centered gutter and aligns with both branding labels");
                Button help = Field<Button>(form, "help"); SoundButton app = Field<SoundButton>(form, "appSoundButton"), hardware = Field<SoundButton>(form, "readerSoundButton");
                Rectangle h = help.RectangleToScreen(help.ClientRectangle), a = app.RectangleToScreen(app.ClientRectangle), r = hardware.RectangleToScreen(hardware.ClientRectangle);
                Check(h.Top == a.Top && a.Top == r.Top && help.Height == app.Height && app.Height == hardware.Height, "All three header controls share height and top edge");
                Check(h.Left - a.Right >= 10, "Sound controls keep a visible gap before Help");
                RenderSnapshot(form, "branding-compact-aligned");
                Rectangle work = Screen.FromControl(form).WorkingArea;
                using (ReceptionHelp dialog = new ReceptionHelp("192.168.63.99", null, FormatProfiles.Combo, "M", false))
                {
                    dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(work.Right - 650, work.Bottom - 200);
                    dialog.Show(form); Application.DoEvents();
                    ((Button)Find(dialog, "IT-inställningar", false)).PerformClick(); Application.DoEvents();
                    Check(work.Contains(dialog.Bounds), "Expanding IT settings near the bottom moves the complete window into view");
                    dialog.Height = Math.Min(dialog.Height, 600); Application.DoEvents();
                    Check(dialog.VerticalScroll.Visible, "Tall IT settings scroll in a compact window");
                    RenderSnapshot(dialog, "it-settings-contained");
                }
            }
            using (Bitmap wide = new Bitmap(320, 64))
            {
                using (Graphics graphics = Graphics.FromImage(wide)) { graphics.Clear(Color.Transparent); graphics.FillRectangle(Brushes.Navy, 0, 0, 320, 64); }
                AppearanceFiles.Save(directory, settings, wide);
            }
            using (ReceptionForm form = ReaderPreview(directory, FormatProfiles.Combo))
            {
                form.Show(); Application.DoEvents(); PictureBox logo = Field<PictureBox>(form, "logo");
                Check(logo.Width > logo.Height && Math.Abs((double)logo.Width / logo.Height - 5) < 0.1,
                    "Wide rectangular logo keeps its aspect ratio without stretching or cropping");
                Check(logo.Left == Field<Label>(form, "brand").Left, "Wide logo shares the branding left edge");
                RenderSnapshot(form, "branding-wide-logo");
            }
            using (AppearanceDialog dialog = new AppearanceDialog(directory))
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(work.Right - 300, work.Bottom - 120);
                dialog.Show(); Application.DoEvents();
                Check(Screen.FromControl(dialog).WorkingArea.Contains(dialog.Bounds), "Appearance editor clamps both size and position on first open");
                Check(Inside(dialog, (Button)Find(dialog, "Spara utseende", false)), "Appearance save action stays visible after fitting");
                RenderSnapshot(dialog, "branding-settings-contained");
            }
        }
        private static void ExerciseDisplayLogo(string directory)
        {
            Directory.CreateDirectory(directory); AppearanceSettings settings = AppearanceSettings.Default();
            using (Bitmap original = new Bitmap(2048, 1024))
            {
                using (Graphics graphics = Graphics.FromImage(original)) { graphics.Clear(Color.Transparent); graphics.FillRectangle(Brushes.Navy, 256, 256, 1280, 512); }
                AppearanceFiles.Save(directory, settings, original);
            }
            using (Bitmap display = AppearanceFiles.LoadDisplayLogo(directory, settings, new Size(360, 128)))
            {
                Check(display.Width == 256 && display.Height == 128 && display.GetPixel(0, 0).A == 0, "Display logo preserves aspect ratio and transparency");
                Check(display.Width * display.Height * 4 <= 128 * 1024, "Large logo display copy has a bounded pixel footprint");
            }
            using (Bitmap original = AppearanceFiles.LoadLogo(directory, settings))
                Check(original.Width == 2048 && original.Height == 1024, "Display downsampling leaves the editable original untouched");
            using (Bitmap display = AppearanceFiles.LoadDisplayLogo(directory, settings, new Size(540, 192)))
                Check(display.Width == 384 && display.Height == 192, "Display copy adapts to a larger DPI footprint");
            using (ReceptionForm form = new ReceptionForm(true, directory))
            {
                Image display = Field<PictureBox>(form, "logo").Image;
                Check(display != null && display.Width <= 360 && display.Height <= 128, "Main workspace retains the small display bitmap");
            }
        }
        private static void ExerciseExport(string directory)
        {
            Directory.CreateDirectory(directory); string unrelated = Path.Combine(directory, "behall.txt"); File.WriteAllText(unrelated, "keep");
            bool failed = false;
            try
            {
                ReceptionForm.ExportPackageFiles("unused", directory, null, delegate(string source, string destination) {
                    Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "partial.txt"), "partial"); throw new IOException("synthetic failure");
                });
            }
            catch (IOException) { failed = true; }
            Check(failed && Directory.GetDirectories(directory).Length == 0 && File.ReadAllText(unrelated) == "keep", "Failed export removes only its own partial directory");
            string output = ReceptionForm.ExportPackageFiles("unused", directory, null, delegate(string source, string destination) {
                Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "program.txt"), "synthetic program");
            });
            Check(Directory.Exists(output) && File.Exists(Path.Combine(output, "Snabbstart.txt")) && !File.Exists(Path.Combine(output, ReceptionFiles.ProfileName)), "Completed standard package contains no invented reader profile");
            Check(Directory.GetDirectories(directory).Length == 1 && File.ReadAllText(unrelated) == "keep", "Export leaves no staging directory and preserves surrounding files");
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
            Check(Updates.Authenticate(asset).Version == asset.Version, "Signed offer reauthenticated before staging");
            UpdateAsset noEnvelope = new UpdateAsset { Version = asset.Version, Url = asset.Url, Sha256 = asset.Sha256, Size = asset.Size };
            Reject(delegate { Updates.Authenticate(noEnvelope); }, "Unsigned offer rejected before staging");
            UpdateAsset substituted = Updates.Decode(fixture, "preview"); substituted.Version = "99.2.0";
            Reject(delegate { Updates.Authenticate(substituted); }, "Mutable offer cannot replace signed version");
            substituted = Updates.Decode(fixture, "preview"); substituted.Sha256 = new string('0', 64);
            Reject(delegate { Updates.Authenticate(substituted); }, "Mutable offer cannot replace signed package hash");
            Reject(delegate { Updates.Decode(fixture, "stable"); }, "Channel substitution rejected");
            byte[] changed = (byte[])fixture.Clone(); changed[40] ^= 1;
            Reject(delegate { Updates.Decode(changed, "preview"); }, "Tampered feed rejected");
            Updates.VerifyPackage(System.Text.Encoding.UTF8.GetBytes("fixture"), asset);
            Reject(delegate { Updates.VerifyPackage(System.Text.Encoding.UTF8.GetBytes("changed"), asset); }, "Tampered archive rejected");
            foreach (string v in new string[] { "3.5", "3.6.0.1", "3.5.-1", "65536.0.0", "3.6.0\n", "x.0.0" }) Reject(delegate { Updates.ParseVersion(v); }, "Invalid version rejected");
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
        private static void ExerciseReaderSound(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (byte board in new byte[] { 6, 10 })
            {
                string local = Path.Combine(directory, board.ToString()); Directory.CreateDirectory(local);
                FakeReader fake = new FakeReader { Board = board };
                ReaderClient reader = new ReaderClient("192.168.63.99", local, new LocalLog()); reader.TestTransport = fake.Send; reader.Connect();
                byte[] original = (byte[])fake.SoundTable.Clone();
                Check(reader.ReadReaderSound(), "Hardware sound is read rather than inferred from PC volume");
                Check(!reader.SetReaderSound(false) && !reader.ReadReaderSound(), "Reader mute is confirmed by readback");
                byte[] muted = (byte[])fake.SoundTable.Clone();
                Check(muted.SequenceEqual(ReaderClient.QuietSound(original)), "Only buzzer bits in public sequences changed");
                Check(muted.Skip(39).SequenceEqual(original.Skip(39)), "Internal reader sequences untouched");
                int writes = fake.Writes; Check(!reader.SetReaderSound(false) && fake.Writes == writes, "An unchanged mute state does not write flash");
                Check(reader.SetReaderSound(true) && original.SequenceEqual(fake.SoundTable), "Unmute restores exact original pattern and LEDs");
                fake.SoundFailure = 1;
                bool failed = false; try { reader.SetReaderSound(false); } catch (IOException) { failed = true; }
                Check(failed && original.SequenceEqual(fake.SoundTable) && reader.ReadReaderSound(), "Partial and ambiguous writes roll back before reporting failure");
                string soundPath = Path.Combine(local, "lasarljud-" + reader.ReaderId + ".json");
                var soundJson = new System.Web.Script.Serialization.JavaScriptSerializer();
                ReaderSoundBackup pendingSound = new ReaderSoundBackup { Schema = 1, ReaderId = reader.ReaderId, Firmware = reader.Firmware,
                    Original = Codec.ToHex(original.Take(39).ToArray()), Pending = Codec.ToHex(original.Take(39).ToArray()) };
                foreach (int changedIndex in new int[] { 0, 1, 12 })
                {
                    byte[] changed = (byte[])muted.Clone(); changed[changedIndex] ^= 1; fake.SoundTable = (byte[])changed.Clone();
                    string saved = soundJson.Serialize(pendingSound); File.WriteAllText(soundPath, saved); writes = fake.Writes;
                    Reject(delegate { reader.ReadReaderSound(); }, "Pending sound recovery rejects changed LEDs, durations and repeat counts");
                    Check(fake.Writes == writes && fake.SoundTable.SequenceEqual(changed) && File.ReadAllText(soundPath) == saved,
                        "Rejected sound recovery preserves live sequences and the pending journal without writes");
                }
                byte[] badPending = original.Take(39).ToArray(); badPending[0] |= 0x80;
                pendingSound.Pending = Codec.ToHex(badPending); fake.SoundTable = (byte[])muted.Clone();
                string damaged = soundJson.Serialize(pendingSound); File.WriteAllText(soundPath, damaged); writes = fake.Writes;
                Reject(delegate { reader.ReadReaderSound(); }, "Corrupt pending sound flags cannot enter a reader command");
                Check(fake.Writes == writes && fake.SoundTable.SequenceEqual(muted) && File.ReadAllText(soundPath) == damaged,
                    "Corrupt pending sound data remains available for investigation without reader writes");
                pendingSound.Pending = Codec.ToHex(original.Take(39).ToArray()); File.WriteAllText(soundPath, soundJson.Serialize(pendingSound));
                fake.SoundTable = (byte[])muted.Clone(); fake.SoundTable[39] ^= 1;
                byte[] expectedRecovery = (byte[])fake.SoundTable.Clone(); Array.Copy(original, expectedRecovery, 39);
                Check(reader.ReadReaderSound() && fake.SoundTable.SequenceEqual(expectedRecovery),
                    "Matching pending sound recovery restores buzzer state and preserves current internal sequences");
                ReaderSoundBackup recovered = soundJson.Deserialize<ReaderSoundBackup>(File.ReadAllText(soundPath));
                Check(String.IsNullOrEmpty(recovered.Pending), "Pending sound journal clears only after matching readback");
                Check(WorkflowSettings.Load(local).Sounds == false && reader.ReadReaderSound(), "Hardware sound and app preference are independent");
                string journal = Path.Combine(local, "aterstall-" + reader.ReaderId + ".json"); File.WriteAllText(journal, "pending");
                writes = fake.Writes; failed = false; try { reader.SetReaderSound(false); } catch (InvalidOperationException) { failed = true; }
                Check(failed && fake.Writes == writes, "Pending card recovery prevents sound writes"); File.Delete(journal);
                File.Delete(Path.Combine(local, "lasarljud-" + reader.ReaderId + ".json")); fake.SoundTable = (byte[])muted.Clone();
                Check(reader.SetReaderSound(true) && ReaderClient.QuietSound(fake.SoundTable).SequenceEqual(muted), "Initially silent reader uses existing timing and off-step without altering LEDs");
                fake.SoundTable = new byte[129]; Reject(delegate { reader.ReadReaderSound(); }, "Unknown firmware table layout is rejected without guessing");
            }
            using (ReceptionForm form = ReaderPreview(Path.Combine(directory, "ui"), FormatProfiles.Combo))
            {
                SoundButton app = Field<SoundButton>(form, "appSoundButton"), hardware = Field<SoundButton>(form, "readerSoundButton");
                form.Show(); Application.DoEvents(); Call(form, "UpdateButtons");
                SetField(form, "readerSound", (bool?)null); Call(form, "UpdateButtons");
                Check(!hardware.Enabled && !hardware.StateKnown && app.StateKnown, "Unknown reader sound never presents a confirmed on/off symbol");
                SetField(form, "readerSound", (bool?)true); Call(form, "UpdateButtons");
                Check(hardware.Enabled && !hardware.Muted && app.Muted, "Separate hardware-on and app-off icons");
                Check(hardware.Size == app.Size && hardware.Top == app.Top, "Sound icon slots and baselines align");
                app.PerformClick();
                Check(Field<WorkflowSettings>(form, "workflow").Sounds && Field<bool?>(form, "readerSound") == true, "App icon toggles only the app preference");
                Check(WorkflowSettings.Load(Path.Combine(directory, "ui")).Sounds, "App sound choice persists");
                Call(form, "SetBusy", true); Check(!hardware.Enabled && !app.Enabled, "Sound controls cannot interrupt an active reader or update operation");
                Call(form, "SetBusy", false); SetField(form, "readerSound", (bool?)false); Call(form, "UpdateButtons"); Check(hardware.Muted && !app.Muted, "Mute symbols remain independent");
                foreach (bool readerOn in new bool[] { true, false }) foreach (bool appOn in new bool[] { true, false })
                {
                    SetField(form, "readerSound", (bool?)readerOn); Field<WorkflowSettings>(form, "workflow").Sounds = appOn; Call(form, "UpdateButtons");
                    RenderSnapshot(form, "ljud-lasare-" + readerOn + "-app-" + appOn);
                }
            }
        }
        private static void ExerciseConnectionDiagnostics(string directory)
        {
            foreach (string encoded in new[] { "00", "0000" })
                Check(!ReaderSafety.DecodeMode(new Tlv { Tag = 2, Value = Codec.Hex(encoded) }), "Disabled USB mode accepts documented value in one or two octets");
            foreach (string encoded in new[] { "FF", "00FF" })
                Check(ReaderSafety.DecodeMode(new Tlv { Tag = 2, Value = Codec.Hex(encoded) }), "Protected USB mode accepts documented value in one or two octets");
            foreach (string encoded in new[] { "", "01", "0001", "0100", "FF00", "FFFF", "000000", "0000FF", "000000FF" })
                Reject(delegate { ReaderSafety.DecodeMode(new Tlv { Tag = 2, Value = Codec.Hex(encoded) }); }, "Unknown, reversed or oversized USB integer remains rejected");
            foreach (byte tag in new byte[] { 0, 1, 4, 5 })
                Reject(delegate { ReaderSafety.DecodeMode(new Tlv { Tag = tag, Value = new byte[] { 0, 255 } }); }, "Wrong USB mode type remains rejected");
            Reject(delegate { ReaderSafety.DecodeMode(null); }, "Missing USB mode remains rejected");
            Reject(delegate { ReaderSafety.DecodeMode(new Tlv { Tag = 2, Value = null }); }, "Missing USB mode value remains rejected");
            FakeReader fake = new FakeReader();
            ReaderClient reader = new ReaderClient("192.168.63.99", directory, new LocalLog());
            int commands = 0;
            reader.TestTransport = delegate(string command) {
                // A diagnostic run must send GET MIB only. Reject all card reads,
                // SETs, Apply, reboot and automatic sound-recovery commands.
                byte[] bytes = Codec.Hex(command);
                Check(bytes.Length > 8 && bytes[0] == 255 && bytes[1] == 104 && bytes[2] == 0 && bytes[3] == 0 && bytes[5] == 0,
                    "Connection diagnostic sends GET MIB only");
                commands++; return fake.Send(command);
            };
            fake.WideUsbMode = true;
            string report = ReaderDiagnostics.Inspect(reader);
            Check(report.Contains("IdentifyReader: OK") && report.Contains("UsbOutputMode: Custom Report"), "Read-only diagnostic identifies reader and USB mode");
            Check(report.Contains("ReaderSound: På") && commands > 10 && fake.Writes == 0 && Directory.GetFiles(directory).Length == 0, "Diagnostic creates no reader state and performs no writes");
            Check(!report.Contains(reader.ReaderId) && !report.Contains(reader.Firmware) && !report.Contains(directory) && !report.Contains("192.168.63.99"), "Diagnostic excludes device identity, address and paths");
            string backupPath = Path.Combine(directory, "usb-original-" + reader.ReaderId + ".json");
            byte[] damaged = System.Text.Encoding.UTF8.GetBytes("{}"); File.WriteAllBytes(backupPath, damaged);
            report = ReaderDiagnostics.Inspect(reader);
            Check(report.Contains("USB_BACKUP_INVALID") && report.Contains("ReaderSafety.ReadBackup"), "Legacy or invalid USB backup has a distinct diagnostic code");
            Check(File.ReadAllBytes(backupPath).SequenceEqual(damaged) && Directory.GetFiles(directory).Length == 1, "Diagnostic preserves invalid state for investigation");
            File.Delete(backupPath);
            string pendingPath = Path.Combine(directory, "usb-pending-" + reader.ReaderId + ".json");
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            ReaderOutputTransition pending = new ReaderOutputTransition { Schema = 1, ReaderId = reader.ReaderId, Firmware = reader.Firmware,
                TargetCustomReport = true, Phase = "prepared", CreatedUtc = DateTime.UtcNow.ToString("o") };
            File.WriteAllText(pendingPath, json.Serialize(pending)); string pendingBefore = File.ReadAllText(pendingPath);
            report = ReaderDiagnostics.Inspect(reader);
            Check(report.Contains("USB_BACKUP_MISSING") && File.ReadAllText(pendingPath) == pendingBefore, "Interrupted operation without original is reported without creating a guessed backup");
            pending.Firmware = "00000000"; File.WriteAllText(pendingPath, json.Serialize(pending));
            report = ReaderDiagnostics.Inspect(reader);
            Check(report.Contains("USB_PENDING_INVALID") && fake.Writes == 0, "Mismatched pending state is identified without recovery writes");
            File.Delete(pendingPath);
            fake.BadUsbMode = true; report = ReaderDiagnostics.Inspect(reader);
            Check(report.Contains("USB_MODE_RESPONSE"), "Invalid mode has a fixed reason code: " + report);
            Check(report.Contains("USB-format Integer; längd 1; värde 01"), "Invalid mode includes bounded shape metadata: " + report);
            Check(report.Contains("ReaderSafety.DecodeMode"), "Invalid mode retains the safe validation boundary: " + report);
            Check(report.Contains("UsbOriginal:"), "Invalid mode does not stop independent local-state checks: " + report);
            Check(fake.Writes == 0 && Directory.GetFiles(directory).Length == 0, "All diagnostic error paths remain read only");
        }
        private sealed class FakeReader
        {
            internal byte Board = 6; internal int Writes; internal bool BadSettings; internal bool BadUsbMode; internal bool WideUsbMode;
            internal int SoundFailure = -1;
            internal byte[] SoundTable = Enumerable.Range(0, 130).Select(i => (byte)(i < 39 ? 0 : 165)).ToArray();
            private bool present;
            private readonly System.Collections.Generic.Dictionary<string, Tlv> settings = new System.Collections.Generic.Dictionary<string, Tlv>();
            internal FakeReader()
            {
                for (int sequence = 0; sequence < 3; sequence++) { SoundTable[sequence * 13] = 17; SoundTable[sequence * 13 + 1] = 2; SoundTable[sequence * 13 + 2] = 1; SoundTable[sequence * 13 + 3] = 1; }
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
                byte[] cmd = Codec.Hex(command);
                if (cmd[2] == 3) {
                    if (command == "FF680380") return Codec.Hex("9000");
                    int sequence = cmd[3] - 0x40; if (sequence < 0 || sequence > 2 || cmd.Length != 18 || cmd[4] != 13) throw new InvalidOperationException("Unexpected sound write");
                    Writes++; Array.Copy(cmd, 5, SoundTable, sequence * 13, 13);
                    if (SoundFailure == sequence) { SoundFailure = -1; throw new IOException("Simulated lost sound response"); }
                    return Codec.Hex("9000");
                }
                int p = 8; Tlv oid = Codec.ReadTlv(cmd, ref p); Tlv value = Codec.ReadTlv(cmd, ref p);
                string key = Codec.ToHex(oid.Value);
                if (cmd[5] == 2) { Writes++; settings[key] = value; }
                else if (key == "010200") value = new Tlv { Tag = 2, Value = new byte[] { Board } };
                else if (key == "010100") value = new Tlv { Tag = 2, Value = Codec.Hex("01050176") };
                else if (key == "010A00") value = new Tlv { Tag = 4, Value = Enumerable.Repeat((byte)0xAA, 16).ToArray() };
                else if (key == "04010500") value = new Tlv { Tag = 2, Value = BadUsbMode ? new byte[] { 1 } : WideUsbMode ? new byte[] { 0, 255 } : new byte[] { 255 } };
                else if (key == ReaderClient.SoundOid) value = new Tlv { Tag = 4, Value = SoundTable };
                else if (key == "0302010300") value = new Tlv { Tag = 4, Value = present ? Codec.Hex("3B00") : new byte[0] };
                else value = BadSettings ? new Tlv { Tag = 4, Value = new byte[0] } : settings[key];
                return Codec.Hex("0300000D" + oid.Value.Length.ToString("X2") + key + value.Tag.ToString("X2") + (value.Value.Length >= 128 ? "81" : "") + value.Value.Length.ToString("X2") + Codec.ToHex(value.Value) + "9000");
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
                RenderSnapshot(form, name);
            }
        }
        private static void RenderSnapshot(Form form, string name)
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
