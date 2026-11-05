namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;
    using System.Windows.Forms;

    internal sealed class ReaderSoundBackup
    {
        public ReaderSoundBackup() { }
        public int Schema { get; set; }
        public string ReaderId { get; set; }
        public string Firmware { get; set; }
        public string Original { get; set; }
        public string Pending { get; set; }
    }
    public sealed partial class ReaderClient
    {
        // HID 5127-903 B.7 A.5.18/B.2.6: ten 13-byte sequences.
        // Only the first three public sequences are editable; internal sequences stay untouched.
        internal const string SoundOid = "02020104010100";
        private string SoundPath { get { return Path.Combine(directory, "lasarljud-" + ReaderId + ".json"); } }
        internal static byte[] QuietSound(byte[] data)
        {
            ValidateSound(data);
            byte[] next = (byte[])data.Clone();
            for (int sequence = 0; sequence < 3; sequence++)
                for (int step = 0; step < 6; step++) next[sequence * 13 + step * 2] &= 0xEF;
            return next;
        }
        private static void ValidateSound(byte[] data)
        {
            if (data == null || data.Length != 130) throw new InvalidDataException("Läsarens ljudformat stöds inte.");
            for (int sequence = 0; sequence < 3; sequence++)
                for (int step = 0; step < 6; step++)
                    if ((data[sequence * 13 + step * 2] & ~0x13) != 0) throw new InvalidDataException("Läsarens ljudformat stöds inte.");
        }
        private byte[] ReadSoundTable()
        {
            Tlv value = GetMib(SoundOid);
            if (value.Tag != 4) throw new InvalidDataException("Läsaren kunde inte lämna sitt ljudläge.");
            ValidateSound(value.Value); return value.Value;
        }
        internal static bool HasSound(byte[] table)
        {
            ValidateSound(table);
            for (int sequence = 0; sequence < 3; sequence++)
                for (int step = 0; step < 6 && table[sequence * 13 + step * 2 + 1] != 0; step++)
                    if ((table[sequence * 13 + step * 2] & 0x10) != 0) return true;
            return false;
        }
        private ReaderSoundBackup ReadSoundBackup()
        {
            if (!File.Exists(SoundPath)) return null;
            if (new FileInfo(SoundPath).Length > 4096) throw new InvalidDataException("Den sparade ljudinställningen är ogiltig.");
            ReaderSoundBackup value = json.Deserialize<ReaderSoundBackup>(File.ReadAllText(SoundPath));
            if (value == null || value.Schema != 1 || value.ReaderId != ReaderId || value.Firmware != Firmware || Codec.Hex(value.Original ?? "").Length != 39 ||
                (!String.IsNullOrEmpty(value.Pending) && Codec.Hex(value.Pending).Length != 39)) throw new InvalidDataException("Ljudinställningen gäller inte den här läsaren och firmware.");
            return value;
        }
        private void WriteSoundTable(byte[] target)
        {
            ValidateSound(target); CheckSoundIdentity();
            // Store and automatic-control commands must be separate. Never run a test beep.
            for (int sequence = 0; sequence < 3; sequence++)
            {
                CheckSoundIdentity();
                byte[] command = new byte[] { 255, 104, 3, (byte)(0x40 + sequence), 13 }.Concat(target.Skip(sequence * 13).Take(13)).ToArray();
                if (Codec.Success(Send(Codec.ToHex(command))).Length != 0) throw new IOException("Läsaren bekräftade inte ljudändringen.");
            }
            if (Codec.Success(Send("FF680380")).Length != 0) throw new IOException("Läsarens automatiska indikering kunde inte återställas.");
            CheckSoundIdentity(); Apply();
            if (!ReadSoundTable().SequenceEqual(target)) throw new IOException("Läsarens ljudändring kunde inte verifieras.");
        }
        private void CheckSoundIdentity()
        {
            CheckIdentity(); Tlv fw = GetMib("010100");
            if (fw.Tag != 2 || Codec.ToHex(fw.Value) != Firmware) throw new IOException("Läsarens firmware har ändrats. Anslut igen.");
            if (!Connected || HasPendingRecovery) throw new InvalidOperationException("Återställ läsaren innan ljudet ändras.");
        }
        internal bool ReadReaderSound()
        {
            CheckSoundIdentity();
            ReaderSoundBackup backup = ReadSoundBackup();
            byte[] table = ReadSoundTable();
            if (backup != null && !String.IsNullOrEmpty(backup.Pending))
            {
                Array.Copy(Codec.Hex(backup.Pending), table, 39); WriteSoundTable(table);
                backup.Pending = null; AtomicWrite(SoundPath, json.Serialize(backup));
            }
            return HasSound(table);
        }
        internal bool SetReaderSound(bool enabled)
        {
            ReadReaderSound(); CheckSoundIdentity();
            byte[] before = ReadSoundTable(), target = QuietSound(before);
            ReaderSoundBackup backup = ReadSoundBackup();
            if (HasSound(before) == enabled) return enabled;
            if (enabled)
            {
                if (backup == null)
                {
                    // A pre-muted reader can be enabled using its existing first two card-event steps.
                    // Their LED values and durations remain unchanged; an explicit off-step is mandatory.
                    if (before[14] == 0 || before[16] == 0) throw new InvalidOperationException("Läsaren saknar ett pipmönster som kan aktiveras utan att ändra ljussekvensen. Be IT konfigurera ljudsekvensen i läsaren.");
                    target[13] |= 0x10;
                    backup = new ReaderSoundBackup { Schema = 1, ReaderId = ReaderId, Firmware = Firmware, Original = Codec.ToHex(target.Take(39).ToArray()) };
                }
                byte[] original = Codec.Hex(backup.Original);
                // A later external edit of LED colours/timing must never be overwritten.
                for (int i = 0; i < 39; i++)
                {
                    bool state = i % 13 < 12 && (i % 13) % 2 == 0;
                    int mask = state ? 0xEF : 0xFF;
                    if ((before[i] & mask) != (original[i] & mask)) throw new InvalidOperationException("Läsarens ljus- eller ljudsekvens har ändrats utanför appen. Kontrollera inställningarna innan ljudet återställs.");
                }
                Array.Copy(original, target, 39); ValidateSound(target);
            }
            else
            {
                backup = new ReaderSoundBackup { Schema = 1, ReaderId = ReaderId, Firmware = Firmware, Original = Codec.ToHex(before.Take(39).ToArray()) };
            }
            backup.Pending = Codec.ToHex(before.Take(39).ToArray());
            AtomicWrite(SoundPath, json.Serialize(backup)); // Durable before the first reader write.
            try
            {
                WriteSoundTable(target); backup.Pending = null; AtomicWrite(SoundPath, json.Serialize(backup));
                return HasSound(target);
            }
            catch
            {
                // If rollback fails, leave the journal for this exact reader's next connection.
                try { CheckSoundIdentity(); WriteSoundTable(before); backup.Pending = null; AtomicWrite(SoundPath, json.Serialize(backup)); } catch { }
                throw;
            }
        }
    }

    // Pinned Lucide assets retain their upstream licence in Assets/Lucide/LICENSE.txt.
    internal sealed class SoundButton : Button
    {
        internal bool ReaderIcon, Muted;
        private readonly Image onIcon, offIcon;
        internal SoundButton(bool reader)
        {
            ReaderIcon = reader; Size = new Size(48, 48); MinimumSize = Size; MaximumSize = Size;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            BackColor = Color.White; Margin = new Padding(0); TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            onIcon = LoadIcon("volume-2.png"); offIcon = LoadIcon("volume-off.png");
        }
        private static Image LoadIcon(string name)
        {
            string path = Path.Combine(ReceptionFiles.PackagePath, "Assets", "Lucide", name);
            using (Image original = Image.FromFile(path)) return new Bitmap(original);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { onIcon.Dispose(); offIcon.Dispose(); }
            base.Dispose(disposing);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Focused || ClientRectangle.Contains(PointToClient(Control.MousePosition)) ? Ui.AccentLight : Color.White);
            float scale = e.Graphics.DpiX / 96F;
            int size = (int)Math.Round(24 * scale), top = (int)Math.Round(4 * scale);
            Rectangle iconArea = new Rectangle((Width - size) / 2, top, size, size);
            Color ink = Enabled ? Ui.Accent : Ui.Muted;
            Image icon = Muted ? offIcon : onIcon;
            using (System.Drawing.Imaging.ImageAttributes tint = new System.Drawing.Imaging.ImageAttributes())
            {
                System.Drawing.Imaging.ColorMatrix matrix = new System.Drawing.Imaging.ColorMatrix(new float[][] {
                    new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 1, 0 }, new float[] { ink.R / 255F, ink.G / 255F, ink.B / 255F, 0, 1 } });
                tint.SetColorMatrix(matrix);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(icon, iconArea, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, tint);
            }
            Rectangle labelArea = new Rectangle(0, Height - (int)Math.Round(16 * scale), Width, (int)Math.Round(14 * scale));
            TextRenderer.DrawText(e.Graphics, ReaderIcon ? "Läsare" : "App", Ui.GetFont(8F), labelArea, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }
    public sealed partial class ReceptionForm
    {
        private readonly SoundButton readerSoundButton = new SoundButton(true), appSoundButton = new SoundButton(false);
        private readonly ToolTip soundTips = new ToolTip();
        private bool? readerSound;
        private void InitializeSoundButtons(TableLayoutPanel tools)
        {
            FlowLayoutPanel row = Ui.Row(); row.FlowDirection = FlowDirection.LeftToRight; row.WrapContents = false; row.Margin = new Padding(0, 4, 0, 0);
            row.Controls.Add(readerSoundButton); row.Controls.Add(appSoundButton); row.Controls.Add(help); Ui.Add(tools, row);
            readerSoundButton.Click += async delegate { await ToggleReaderSound(); };
            appSoundButton.Click += delegate {
                if (busy) return;
                WorkflowSettings next = new WorkflowSettings { Copy = workflow.Copy, Sounds = !workflow.Sounds, Channel = workflow.Channel, AutoCheck = workflow.AutoCheck };
                try { ReceptionFiles.WriteAtomic(Path.Combine(appearanceDirectory, "arbetsflode.json"), new JavaScriptSerializer().Serialize(next)); workflow = next; UpdateButtons(); }
                catch { MessageBox.Show(this, "Appens ljudinställning kunde inte sparas."); }
            };
            Disposed += delegate { soundTips.Dispose(); };
        }
        private void UpdateSoundButtons()
        {
            readerSoundButton.Muted = readerSound == false; readerSoundButton.Enabled = !busy && IsReady && readerSound.HasValue;
            appSoundButton.Muted = !workflow.Sounds; appSoundButton.Enabled = !busy;
            string r = readerSound.HasValue ? "Läsarens pip: " + (readerSound.Value ? "på. Klicka för att stänga av." : "av. Klicka för att slå på.") : "Läsarens pip: läget är inte tillgängligt. Anslut läsaren igen.";
            string a = "Appens ljud: " + (workflow.Sounds ? "på. Klicka för att stänga av." : "av. Klicka för att slå på.");
            readerSoundButton.AccessibleName = r; appSoundButton.AccessibleName = a;
            soundTips.SetToolTip(readerSoundButton, r); soundTips.SetToolTip(appSoundButton, a);
            readerSoundButton.Invalidate(); appSoundButton.Invalidate();
        }
        private async Task ToggleReaderSound()
        {
            ReaderClient active = reader; bool? current = readerSound;
            if (!current.HasValue || !TryBeginReaderOperation(active, false)) return;
            try
            {
                bool value = await Task.Run(delegate { return active.SetReaderSound(!current.Value); });
                readerSound = reconnectPending || !Object.ReferenceEquals(reader, active) ? (bool?)null : value;
            }
            catch (Exception error)
            {
                readerSound = null; SaveError(error);
                notice.Text = "Läsarens ljud kunde inte ändras. Anslut läsaren igen för att kontrollera läget.";
            }
            finally { SetBusy(false); }
        }
    }
}
