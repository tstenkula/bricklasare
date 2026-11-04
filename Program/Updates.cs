namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;
    using System.Windows.Forms;
    internal sealed class UpdateAsset
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
        public string Notes { get; set; }
        internal byte[] SignedFeed;
        internal string Channel;
    }
    internal sealed class UpdateFeed
    {
        public int Schema { get; set; }
        public string Channel { get; set; }
        public UpdateAsset Windows { get; set; }
        public UpdateAsset Mac { get; set; }
    }
    internal enum UpdatePhase { Download, Verify, PrepareInstallation }
    internal sealed class UpdateProgress
    {
        internal readonly UpdatePhase Phase;
        internal readonly long Received, Total;
        internal readonly int Percent;
        internal UpdateProgress(UpdatePhase phase, long received, long total, bool complete = false)
        {
            if (total < 1 || total > 52428800 || received < 0 || received > total || (complete && received != total)) throw new InvalidDataException("Ogiltigt nedladdningsförlopp.");
            Phase = phase; Received = received; Total = total;
            Percent = complete ? 100 : (int)Math.Min(99, received * 100 / total);
        }
        internal string Detail
        {
            get { return Percent.ToString(CultureInfo.InvariantCulture) + " % · " + (Received / 1000000.0).ToString("0.00", CultureInfo.GetCultureInfo("sv-SE")) + " av " + (Total / 1000000.0).ToString("0.00", CultureInfo.GetCultureInfo("sv-SE")) + " MB"; }
        }
    }
    internal sealed class UpdateProgressBar : Control
    {
        private readonly Timer animation = new Timer { Interval = 60 };
        private int percent, frame;
        private bool indeterminate;
        internal int Percent { get { return percent; } set { percent = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        internal bool Indeterminate { get { return indeterminate; } set { indeterminate = value; UpdateAnimation(); Invalidate(); } }
        internal UpdateProgressBar()
        {
            DoubleBuffered = true; Height = 12; Dock = DockStyle.Top; Margin = new Padding(0, 6, 0, 6);
            AccessibleRole = AccessibleRole.ProgressBar; AccessibleName = "Uppdateringsförlopp"; TabStop = false;
            animation.Tick += delegate { frame = (frame + 1) % 50; Invalidate(); };
        }
        private void UpdateAnimation() { animation.Enabled = Visible && indeterminate && !IsDisposed; }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 2 || Height < 2) return;
            using (GraphicsPath outline = new GraphicsPath())
            using (SolidBrush track = new SolidBrush(Color.FromArgb(222, 231, 235)))
            using (SolidBrush fill = new SolidBrush(Ui.Accent))
            {
                float diameter = Math.Min(Height - 1, 12F);
                outline.AddArc(0, 0, diameter, Height - 1, 90, 180);
                outline.AddArc(Width - diameter - 1, 0, diameter, Height - 1, 270, 180); outline.CloseFigure();
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.FillPath(track, outline);
                System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save(); e.Graphics.SetClip(outline);
                if (indeterminate) e.Graphics.FillRectangle(fill, (Width + Width / 3F) * frame / 49F - Width / 3F, 0, Width / 3F, Height);
                else e.Graphics.FillRectangle(fill, 0, 0, Width * percent / 100F, Height);
                e.Graphics.Restore(state);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
    internal static class Updates
    {
        internal const string Current = "3.6.1";
        internal const string PublicKey = "<RSAKeyValue><Modulus>n8JTA0cbbRrraGNb11fX4Dyanoa8S9TzTBGl8Edfagx743HROUj9lDCwJvuVF6cNpH4mB2dN33O+XfYKZMrUaJ0K/3Q9gmPLqedlvbpuHWlEaeUzekdT4Npo7EZpwo+Cwcxjlmlahgoll1MkjEu8QERzAQEeuTB4vZCsKdXdYcRlKAQuPEKqCWkiH49prMDvVdqUUYLl2TPLcHPD8MDdkbFh/glWJDTRB1No++PCUU8PVG80X94BnMe1E/+LLyTjxaNYVSsIFZlD9XX3eup7t/UTKUk9RC1u8WY23l74ccXwy71lirntfOFW6MdK4kFsIGOk+2PF/QGwoIcpwfhSt7+Id0y85VGEqe002CngsfxUQ7BkAAAB2+D4vPcU3FBLkleYkG5B+WXasbCslTGFXG+hnFpTU2R5XQECRJ2UXjuJN/lDaw8AE7dwUTTfibfYGs9mAUZ6nECBsL3OmpXtCK9ZyTHx2AWs+hvXP+7hzcluf12K2H4MveyyNcOFSNed</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
        internal const string Unavailable = "Det gick inte att söka efter uppdateringar. Kontrollera internetanslutningen och försök igen senare. Du kan fortsätta använda Brickläsaren.";
        internal static Version ParseVersion(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\z")) throw new InvalidDataException("Ogiltigt versionsnummer.");
            Version result = new Version(value);
            if (result.Major > 65535 || result.Minor > 65535 || result.Build > 65535) throw new InvalidDataException();
            return result;
        }
        internal static UpdateAsset Decode(byte[] bytes, string channel)
        {
            if (bytes.Length > 65536 || (channel != "stable" && channel != "preview")) throw new InvalidDataException();
            string[] lines = Encoding.UTF8.GetString(bytes).TrimEnd('\r','\n').Split('\n');
            if (lines.Length != 3 || lines[0] != "BRICKLASARE-UPDATE-1") throw new InvalidDataException("Ogiltig uppdateringsfil.");
            byte[] payload = Convert.FromBase64String(lines[1]), signature = Convert.FromBase64String(lines[2]);
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp = false; rsa.FromXmlString(PublicKey);
                if (!rsa.VerifyData(payload, CryptoConfig.MapNameToOID("SHA256"), signature)) throw new InvalidDataException("Det gick inte att kontrollera uppdateringen. Försök igen senare.");
            }
            UpdateFeed feed = new JavaScriptSerializer { MaxJsonLength = 65536 }.Deserialize<UpdateFeed>(Encoding.UTF8.GetString(payload));
            if (feed == null || feed.Schema != 1 || feed.Channel != channel) throw new InvalidDataException();
            UpdateAsset asset = feed.Windows;
            if (asset != null) { ValidateAsset(asset); asset.SignedFeed = (byte[])bytes.Clone(); asset.Channel = channel; }
            return asset;
        }
        internal static void ValidateAsset(UpdateAsset asset)
        {
            ParseVersion(asset.Version);
            Uri url;
            if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out url) || url.Scheme != "https" || url.Host != "github.com" || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
                !url.AbsolutePath.StartsWith("/tstenkula/bricklasare/releases/download/", StringComparison.Ordinal) || !url.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal) ||
                asset.Size <= 0 || asset.Size > 52428800 || asset.Sha256 == null || !Regex.IsMatch(asset.Sha256, @"\A[0-9a-fA-F]{64}\z") || (asset.Notes ?? "").Length > 4000) throw new InvalidDataException("Ogiltigt uppdateringspaket.");
        }
        internal static byte[] Download(string url, int maximum, long expected = 0, Action<UpdateProgress> progress = null)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Uri uri = new Uri(url);
            for (int redirect = 0; redirect < 6; redirect++) {
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || (uri.Host != "raw.githubusercontent.com" && uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" && uri.Host != "objects.githubusercontent.com")) throw new InvalidDataException("Otillåten nedladdningsadress.");
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri); request.AllowAutoRedirect = false; request.Timeout = 20000; request.ReadWriteTimeout = 30000; request.UserAgent = "Tobtech-Bricklasaren/" + Current;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code <= 399) { uri = new Uri(uri, response.Headers["Location"]); continue; }
                    if (code != 200 || response.ContentLength > maximum || (expected > 0 && response.ContentLength >= 0 && response.ContentLength != expected)) throw new InvalidDataException();
                    using (Stream input = response.GetResponseStream()) return ReadDownload(input, maximum, expected, progress, null);
                }
            }
            throw new InvalidDataException("För många omdirigeringar.");
        }
        internal static byte[] ReadDownload(Stream input, int maximum, long expected, Action<UpdateProgress> progress, Func<long> clock)
        {
            if (expected < 0 || expected > maximum || maximum < 1) throw new InvalidDataException();
            Stopwatch timer = Stopwatch.StartNew(); if (clock == null) clock = delegate { return timer.ElapsedMilliseconds; };
            long lastReport = 0;
            using (MemoryStream output = new MemoryStream())
            {
                if (expected > 0 && progress != null) progress(new UpdateProgress(UpdatePhase.Download, 0, expected));
                byte[] buffer = new byte[32768]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    long elapsed = clock();
                    if (output.Length + count > maximum || (expected > 0 && output.Length + count > expected) || elapsed > 180000) throw new InvalidDataException();
                    output.Write(buffer, 0, count);
                    if (expected > 0 && progress != null && elapsed - lastReport >= 100)
                    {
                        progress(new UpdateProgress(UpdatePhase.Download, output.Length, expected)); lastReport = elapsed;
                    }
                }
                if (clock() > 180000 || (expected > 0 && output.Length != expected)) throw new InvalidDataException("Nedladdningen blev inte fullständig.");
                if (expected > 0 && progress != null) progress(new UpdateProgress(UpdatePhase.Download, output.Length, expected, true));
                return output.ToArray();
            }
        }
        internal static UpdateAsset Check(string channel) { return Decode(Download("https://raw.githubusercontent.com/tstenkula/bricklasare/main/Updates/" + channel + ".feed", 65536), channel); }
        internal static UpdateAsset Authenticate(UpdateAsset offered)
        {
            if (offered == null || offered.SignedFeed == null) throw new InvalidDataException("Signerad uppdateringsinformation saknas.");
            UpdateAsset signed = Decode(offered.SignedFeed, offered.Channel);
            if (signed == null || signed.Version != offered.Version || signed.Url != offered.Url || signed.Sha256 != offered.Sha256 || signed.Size != offered.Size)
                throw new InvalidDataException("Uppdateringsinformationen har ändrats.");
            if (ParseVersion(signed.Version) <= ParseVersion(Current)) throw new InvalidDataException("Uppdateringen är inte nyare än den installerade versionen.");
            return signed;
        }
        internal static bool Pending(string directory)
        {
            return Directory.GetFiles(directory, "aterstall-*.json").Length > 0 || Directory.GetFiles(directory, "usb-pending-*.json").Length > 0;
        }
        internal static void VerifyPackage(byte[] bytes, UpdateAsset asset)
        {
            ValidateAsset(asset);
            using (SHA256 sha = SHA256.Create()) if (bytes.LongLength != asset.Size || !String.Equals(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""), asset.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Den hämtade uppdateringen kunde inte kontrolleras. Försök hämta den igen.");
        }
        internal static void Extract(byte[] bytes, string directory)
        {
            Directory.CreateDirectory(directory);
            using (MemoryStream input = new MemoryStream(bytes)) using (ZipArchive zip = new ZipArchive(input, ZipArchiveMode.Read)) {
                long total = 0; var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (zip.Entries.Count > 1000) throw new InvalidDataException();
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    string name = entry.FullName;
                    if (String.IsNullOrEmpty(name) || name.StartsWith("/") || name.Contains("\\") || name.Contains(":") || name.Contains("\0") || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Osäker sökväg i paketet.");
                    foreach (string part in name.TrimEnd('/').Split('/')) if (part == ".." || part == "." || part.Length == 0 || part.TrimEnd(' ','.') != part) throw new InvalidDataException();
                    total += entry.Length; if (total > 104857600) throw new InvalidDataException();
                    string target = Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar));
                    if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream from = entry.Open()) using (FileStream to = new FileStream(target, FileMode.CreateNew)) { byte[] buffer = new byte[32768]; int count;
                        while ((count = from.Read(buffer, 0, buffer.Length)) > 0) { if (to.Length + count > entry.Length) throw new InvalidDataException(); to.Write(buffer, 0, count); }
                        if (to.Length != entry.Length) throw new InvalidDataException(); }
                }
            }
        }
        internal static string Stage(UpdateAsset offered, Action<UpdateProgress> progress = null)
        {
            UpdateAsset asset = Authenticate(offered);
            if (Pending(ReceptionFiles.DataPath)) throw new InvalidOperationException("Återställ läsaren innan programmet uppdateras.");
            if (Directory.Exists(Path.Combine(ReceptionFiles.PackagePath, ".git"))) throw new InvalidOperationException("Uppdatera utvecklingskopian via Git.");
            string target = Path.GetFullPath(ReceptionFiles.PackagePath).TrimEnd(Path.DirectorySeparatorChar);
            string parent = Path.GetDirectoryName(target);
            if (String.IsNullOrEmpty(parent)) throw new InvalidOperationException("Välj en vanlig programmapp.");
            string probe = Path.Combine(parent, ".bricklasare-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(probe); Directory.Delete(probe);
            string directory = Path.Combine(ReceptionFiles.DataPath, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                byte[] bytes = Download(asset.Url, 52428800, asset.Size, progress);
                if (progress != null) progress(new UpdateProgress(UpdatePhase.Verify, bytes.LongLength, asset.Size, true));
                VerifyPackage(bytes, asset);
                if (progress != null) progress(new UpdateProgress(UpdatePhase.PrepareInstallation, bytes.LongLength, asset.Size, true));
                string package = Path.Combine(directory, "package"); Extract(bytes, package);
                foreach (string file in ReceptionFiles.RequiredPackageFiles) if (!File.Exists(Path.Combine(package, file))) throw new InvalidDataException("Paketet är ofullständigt.");
                File.WriteAllBytes(Path.Combine(directory, "package.zip"), bytes);
                File.WriteAllBytes(Path.Combine(directory, "release.feed"), asset.SignedFeed);
                File.Copy(Path.Combine(ReceptionFiles.PackagePath, "Program", "Install-Update.ps1"), Path.Combine(directory,"Install-Update.ps1"));
                var plan = new { Target = Path.GetFullPath(ReceptionFiles.PackagePath), Stage = package, ParentId = Process.GetCurrentProcess().Id, Data = ReceptionFiles.DataPath, Channel = asset.Channel };
                ReceptionFiles.WriteAtomic(Path.Combine(directory,"plan.json"), new JavaScriptSerializer().Serialize(plan));
                return directory;
            } catch { try { Directory.Delete(directory,true); } catch { } throw; }
        }
    }
    public sealed partial class ReceptionForm
    {
        private bool updateChecking;
        private int updateProgressGeneration;
        private readonly UiCard updatePanel = new UiCard { Visible = false, Padding = new Padding(16, 10, 16, 10), Margin = new Padding(0, 0, 0, 12) };
        private readonly Label updateTitle = Ui.Label("Hämtar uppdatering", 11F, true);
        private readonly Label updateDetail = Ui.Label("", 10F, false);
        private readonly UpdateProgressBar updateBar = new UpdateProgressBar();
        private void InitializeUpdateProgress(TableLayoutPanel root)
        {
            updateDetail.ForeColor = Ui.Muted; updateDetail.TextAlign = ContentAlignment.MiddleRight; updateDetail.Margin = new Padding(12, 0, 0, 0); updateDetail.MaximumSize = new Size(500, 0);
            updateTitle.Margin = new Padding(0); updateTitle.Dock = DockStyle.None; updateTitle.Anchor = AnchorStyles.Left;
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(updateTitle, 0, 0); header.Controls.Add(updateDetail, 1, 0);
            updateBar.Height = 10; updateBar.Margin = new Padding(0, 6, 0, 0);
            Ui.Add(updatePanel, header); Ui.Add(updatePanel, updateBar); Ui.Add(root, updatePanel);
            updatePanel.SizeChanged += delegate { Ui.WrapLabels(updatePanel); };
        }
        private void ShowUpdateProgress(UpdateProgress value, int generation)
        {
            if (IsDisposed || Disposing || generation != updateProgressGeneration) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(delegate { ShowUpdateProgress(value, generation); })); } catch (InvalidOperationException) { }
                return;
            }
            if (!busy || !updatePanel.Visible) return;
            updateBar.Indeterminate = value.Phase != UpdatePhase.Download;
            updateBar.Percent = value.Percent;
            updateTitle.Text = value.Phase == UpdatePhase.Download ? "Hämtar uppdatering" : value.Phase == UpdatePhase.Verify ? "Kontrollerar uppdateringen" : "Förbereder installation";
            updateDetail.Text = value.Phase == UpdatePhase.Download ? value.Detail : value.Phase == UpdatePhase.Verify ? "Kontrollerar att hela filen är hämtad och äkta." : "Appen startas om när uppdateringen är klar. Dina inställningar sparas.";
            updateBar.AccessibleDescription = updateTitle.Text + ". " + updateDetail.Text;
        }
        private async Task CheckUpdates(bool automatic = false)
        {
            if (busy || updateChecking) return;
            updateChecking = true;
            UpdateAsset asset;
            string channel = workflow.Channel;
            try { asset = await Task.Run(delegate { return Updates.Check(channel); }); }
            catch (WebException) { if (IsDisposed || Disposing) return; if (!automatic) MessageBox.Show(this, Updates.Unavailable, "Uppdateringar"); return; }
            catch { if (IsDisposed || Disposing) return; if (!automatic) MessageBox.Show(this, "Uppdateringsinformationen kunde inte verifieras. Ingen fil har installerats.", "Uppdateringar"); return; }
            finally { updateChecking = false; }
            if (IsDisposed || busy || workflow.Channel != channel) return;
            if (asset == null || Updates.ParseVersion(asset.Version) <= Updates.ParseVersion(Updates.Current)) { if (!automatic) MessageBox.Show(this, "Ingen nyare version finns i vald kanal.", "Uppdateringar"); return; }
            if (automatic) { notice.Text = "Version " + asset.Version + " finns. Välj Hjälp → IT-inställningar → Sök efter uppdatering."; return; }
            if (Updates.Pending(ReceptionFiles.DataPath)) { MessageBox.Show(this, "Återställ den avbrutna läsningen innan du uppdaterar."); return; }
            if (MessageBox.Show(this, "Version " + asset.Version + "\r\n\r\n" + asset.Notes + "\r\n\r\nHämta och installera? Appen startas om när uppdateringen är klar. Dina inställningar sparas.", "Uppdatera Brickläsaren", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            bool launched = false;
            try {
                launched = await RunConfirmedUpdate(channel, ReceptionFiles.DataPath, async delegate(Action<UpdateProgress> progress) {
                    string staged = await Task.Run(delegate { return Updates.Stage(asset, progress); });
                    if (reconnectPending || Updates.Pending(ReceptionFiles.DataPath)) throw new InvalidOperationException();
                    string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                    using (Process helper = Process.Start(new ProcessStartInfo(shell, "-NoProfile -File \"" + Path.Combine(staged,"Install-Update.ps1") + "\" -Plan \"" + Path.Combine(staged,"plan.json") + "\"") { UseShellExecute = false }))
                        if (helper == null) throw new IOException("Installationshjälpen kunde inte startas.");
                });
            } catch { if (!IsDisposed && !Disposing) MessageBox.Show(this, "Uppdateringen kunde inte förberedas. Nuvarande version finns kvar. Kontrollera internet och att programmappen är skrivbar."); }
            if (launched) Close();
        }
        private async Task<bool> RunConfirmedUpdate(string channel, string dataDirectory, Func<Action<UpdateProgress>, Task> operation)
        {
            // A native confirmation dialog pumps messages. Revalidate after it closes,
            // before claiming busy; declining must never release another operation.
            if (busy || reconnectPending || IsDisposed || Disposing || workflow.Channel != channel || Updates.Pending(dataDirectory)) return false;
            SetBusy(true);
            int generation = ++updateProgressGeneration; bool complete = false;
            try
            {
                ClearResults(); updatePanel.Visible = true; updateBar.Percent = 0; updateBar.Indeterminate = true;
                updateTitle.Text = "Ansluter till nedladdningen"; updateDetail.Text = "Väntar på uppdateringsfilen …";
                SetInstruction("UPPDATERAR", "Hämtar en ny version", "Låt programmet arbeta klart. Dina inställningar sparas.", false);
                await operation(delegate(UpdateProgress value) { ShowUpdateProgress(value, generation); }); complete = true; return true;
            }
            finally
            {
                ++updateProgressGeneration; updatePanel.Visible = false; updateBar.Indeterminate = false; SetBusy(false);
                if (!complete) SetInstruction("UPPDATERING AVBRUTEN", "Uppdateringen blev inte klar", "Nuvarande version finns kvar. Försök igen när läsaren är klar.", true);
            }
        }
    }
}
