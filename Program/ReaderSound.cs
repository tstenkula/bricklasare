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
        public string Protocol { get; set; }
        public int TableLength { get; set; }
    }
    internal enum SoundTableCheck { Length, Flags }
    internal sealed class SoundTableShape
    {
        internal readonly int Length;
        internal readonly SoundTableCheck Check;
        internal readonly int Sequence, Step;
        internal SoundTableShape(byte[] data, SoundTableCheck check, int sequence, int step)
        {
            Length = data == null ? -1 : Math.Min(data.Length, 4096);
            Check = check; Sequence = sequence; Step = step;
        }
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
        internal const string LegacyFlagOid = "04010900";
        internal Func<string, string> TestLegacySoundTransport;
        private bool LegacySound { get { return Compatibility != null && Compatibility.UsesLegacySoundWeb; } }
        private int[] SoundSequences { get { return LegacySound ? new int[] { 0, 1, 2, 10 } : new int[] { 0, 1, 2 }; } }
        private static InvalidDataException LegacySoundFormat(byte[] data, SoundTableCheck check, int sequence = -1, int step = -1)
        {
            InvalidDataException error = new InvalidDataException("Gen1-läsarens ljudformat stöds inte.");
            // Shape only: never retain or report the sequence bytes themselves.
            error.Data["Tobtech.SoundTableShape"] = new SoundTableShape(data, check, sequence, step);
            return error;
        }
        private void ValidateCurrentSound(byte[] data)
        {
            if (!LegacySound) { ValidateSound(data); return; }
            // The GUI uses 13-byte records and public sequence indices 0,1,2,10.
            // Its BER decoder supports up to 255 data bytes; preserve all other records.
            if (data == null || data.Length < 143 || data.Length > 247 || data.Length % 13 != 0)
                throw LegacySoundFormat(data, SoundTableCheck.Length);
            foreach (int sequence in SoundSequences)
            {
                for (int step = 0; step < 6; step++)
                    if ((data[sequence * 13 + step * 2] & ~0x13) != 0) throw LegacySoundFormat(data, SoundTableCheck.Flags, sequence, step);
                // HID 5127-902 E.5 section 5.1.3 permits repeats 0..255;
                // FF is a permanent/static event. Preserve the entire byte unchanged.
                // The older web form's max=127 is not the firmware's valid range.
            }
        }
        private Tlv ReadLegacySoundMib(string oid)
        {
            string query = ReaderNetwork.LegacySoundQuery(oid);
            if (TestTransport != null && TestLegacySoundTransport == null) throw new NotSupportedException("Simuleringen saknar Gen1-webbläsning.");
            log.Transport(false, query.Length);
            string answer = TestLegacySoundTransport != null ? TestLegacySoundTransport(query) : ReaderNetwork.ReadLegacySound(IPAddressForSound(), oid);
            log.Transport(true, answer.Length);
            byte[] pdu = Codec.HttpResponse(answer);
            // /ajax returns the MIB response PDU without an APDU status word.
            return Codec.MibResponse(oid, pdu.Concat(new byte[] { 0x90, 0 }).ToArray());
        }
        private System.Net.IPAddress IPAddressForSound() { return System.Net.IPAddress.Parse(endpoint.Host); }
        private void CheckLegacySoundFlag()
        {
            Tlv flag = ReadLegacySoundMib(LegacyFlagOid);
            if (flag.Tag != 1 || flag.Value.Length != 1 || (flag.Value[0] != 0 && flag.Value[0] != 1 && flag.Value[0] != 255))
                throw new InvalidDataException("Läsarens äldre tangentbordsindikering kunde inte verifieras.");
            if (flag.Value[0] != 0)
            {
                Compatibility.BlockLegacyOverride();
                throw new NotSupportedException(Compatibility.SoundExplanation);
            }
        }
        private bool CurrentHasSound(byte[] table)
        {
            ValidateCurrentSound(table);
            foreach (int sequence in SoundSequences)
                for (int step = 0; step < 6 && table[sequence * 13 + step * 2 + 1] != 0; step++)
                    if ((table[sequence * 13 + step * 2] & 0x10) != 0) return true;
            return false;
        }
        private byte[] CurrentQuietSound(byte[] data)
        {
            ValidateCurrentSound(data); byte[] next = (byte[])data.Clone();
            foreach (int sequence in SoundSequences)
                for (int step = 0; step < 6; step++) next[sequence * 13 + step * 2] &= 0xEF;
            return next;
        }
        private byte[] PublicSoundBytes(byte[] table)
        { return SoundSequences.SelectMany(delegate(int sequence) { return table.Skip(sequence * 13).Take(13); }).ToArray(); }
        private byte[] RestorePublicSound(byte[] current, string packed)
        {
            byte[] data = Codec.Hex(packed), result = (byte[])current.Clone();
            if (data.Length != SoundSequences.Length * 13) throw new InvalidDataException("Ogiltig sparad ljudsekvens.");
            for (int i = 0; i < SoundSequences.Length; i++) Array.Copy(data, i * 13, result, SoundSequences[i] * 13, 13);
            ValidateCurrentSound(result); return result;
        }
        private void CheckSoundFieldsUnchanged(byte[] before, byte[] target)
        {
            foreach (int sequence in SoundSequences)
                for (int i = 0; i < 13; i++)
                {
                    int mask = i < 12 && i % 2 == 0 ? 0xEF : 0xFF, index = sequence * 13 + i;
                    if ((before[index] & mask) != (target[index] & mask))
                        throw new InvalidOperationException("Läsarens ljus- eller ljudsekvens har ändrats utanför appen. Kontrollera inställningarna innan ljudet återställs.");
                }
        }
        private ReaderSoundBackup NewSoundBackup(byte[] table)
        {
            return new ReaderSoundBackup { Schema = LegacySound ? 2 : 1, Protocol = LegacySound ? "Gen1WebSequences-v1" : null,
                TableLength = LegacySound ? table.Length : 0, ReaderId = ReaderId, Firmware = Firmware, Original = Codec.ToHex(PublicSoundBytes(table)) };
        }
        private byte[] ReadSoundTable()
        {
            if (Compatibility == null) throw new InvalidOperationException("Anslut läsaren först.");
            Compatibility.BeginSoundProbe();
            Tlv value;
            try
            {
                if (LegacySound) { CheckLegacySoundFlag(); value = ReadLegacySoundMib(SoundOid); }
                else value = GetMib(SoundOid);
            }
            catch (ReaderMibException error)
            {
                if (error.Status != 2) throw;
                Compatibility.MissingSoundTable();
                throw new NotSupportedException(Compatibility.SoundExplanation, error);
            }
            if (value.Tag != 4) throw new InvalidDataException("Läsaren kunde inte lämna sitt ljudläge.");
            ValidateCurrentSound(value.Value); CheckSoundIdentity(); Compatibility.ConfirmSoundTable(); return value.Value;
        }
        internal static bool HasSound(byte[] table)
        {
            ValidateSound(table);
            for (int sequence = 0; sequence < 3; sequence++)
                for (int step = 0; step < 6 && table[sequence * 13 + step * 2 + 1] != 0; step++)
                    if ((table[sequence * 13 + step * 2] & 0x10) != 0) return true;
            return false;
        }
        private ReaderSoundBackup ReadSoundBackup(byte[] current)
        {
            if (!File.Exists(SoundPath)) return null;
            if (new FileInfo(SoundPath).Length > 4096) throw new InvalidDataException("Den sparade ljudinställningen är ogiltig.");
            ReaderSoundBackup value = json.Deserialize<ReaderSoundBackup>(File.ReadAllText(SoundPath));
            int count = SoundSequences.Length * 13;
            bool schema = value != null && (LegacySound ? value.Schema == 2 && value.Protocol == "Gen1WebSequences-v1" && value.TableLength == current.Length :
                value.Schema == 1 && String.IsNullOrEmpty(value.Protocol) && value.TableLength == 0);
            if (!schema || value.ReaderId != ReaderId || value.Firmware != Firmware || Codec.Hex(value.Original ?? "").Length != count ||
                (!String.IsNullOrEmpty(value.Pending) && Codec.Hex(value.Pending).Length != count))
                throw new InvalidDataException("Ljudinställningen gäller inte den här läsaren, firmware och ljudmetoden.");
            RestorePublicSound(current, value.Original);
            if (!String.IsNullOrEmpty(value.Pending)) RestorePublicSound(current, value.Pending);
            return value;
        }
        private void WriteSoundTable(byte[] target)
        {
            if (Compatibility == null) throw new InvalidOperationException("Anslut läsaren först.");
            Compatibility.RequireSoundWrite(); ValidateCurrentSound(target); CheckSoundIdentity();
            // Only the public sequence indices derived for the selected backend are stored.
            // Never run or clear a sequence and never modify the legacy wedge switch.
            foreach (int sequence in SoundSequences)
            {
                byte[] live = ReadSoundTable();
                if (live.Length != target.Length) throw new InvalidDataException("Läsarens ljudformat har ändrats.");
                CheckSoundFieldsUnchanged(live, target);
                CheckSoundIdentity();
                byte[] command = new byte[] { 255, 104, 3, (byte)(0x40 | sequence), 13 }.Concat(target.Skip(sequence * 13).Take(13)).ToArray();
                if (Codec.Success(Send(Codec.ToHex(command))).Length != 0) throw new IOException("Läsaren bekräftade inte ljudändringen.");
            }
            CheckSoundIdentity();
            if (LegacySound) CheckLegacySoundFlag();
            if (Codec.Success(Send("FF680380")).Length != 0) throw new IOException("Läsarens automatiska indikering kunde inte återställas.");
            // Gen1's GUI stores sequences directly with bit 0x40. It does not apply
            // unrelated pending MIB edits; keep that route distinct from the modern backend.
            if (!LegacySound) { CheckSoundIdentity(); Apply(); }
            if (!ReadSoundTable().SequenceEqual(target)) throw new IOException("Läsarens ljudändring kunde inte verifieras.");
        }
        private void CheckSoundIdentity()
        {
            CheckIdentity(); Tlv fw = GetMib("010100");
            if (fw.Tag != 2 || Codec.ToHex(fw.Value) != Firmware) throw new IOException("Läsarens firmware har ändrats. Anslut igen.");
            if (!Connected || HasPendingRecovery) throw new InvalidOperationException("Återställ läsaren innan ljudet ändras.");
        }
        internal string InspectReaderSound()
        {
            // Read-only diagnostic: never invoke pending-sound recovery here.
            CheckSoundIdentity();
            byte[] table = ReadSoundTable();
            ReaderSoundBackup backup = ReadSoundBackup(table);
            return "ReaderSound: " + (CurrentHasSound(table) ? "På" : "Av") + "\r\nSoundMethod: " + Compatibility.SoundMethod +
                "\r\nSoundRecovery: " + (backup != null && !String.IsNullOrEmpty(backup.Pending) ? "Väntar" : "Ingen");
        }
        internal bool ReadReaderSound()
        {
            CheckSoundIdentity();
            byte[] table = ReadSoundTable();
            ReaderSoundBackup backup = ReadSoundBackup(table);
            if (backup != null && !String.IsNullOrEmpty(backup.Pending))
            {
                byte[] target = RestorePublicSound(table, backup.Pending);
                CheckSoundFieldsUnchanged(table, target);
                WriteSoundTable(target); table = target;
                backup.Pending = null; AtomicWrite(SoundPath, json.Serialize(backup));
            }
            return CurrentHasSound(table);
        }
        internal bool SetReaderSound(bool enabled)
        {
            ReadReaderSound(); CheckSoundIdentity();
            byte[] before = ReadSoundTable(), target = CurrentQuietSound(before);
            ReaderSoundBackup backup = ReadSoundBackup(before);
            if (CurrentHasSound(before) == enabled) return enabled;
            if (enabled)
            {
                if (backup == null)
                {
                    // An existing timed off-step is required; do not invent a duration.
                    if (before[14] == 0 || before[16] == 0) throw new InvalidOperationException("Läsaren saknar ett pipmönster som kan aktiveras utan att ändra ljussekvensen. Be IT konfigurera ljudsekvensen i läsaren.");
                    target[13] |= 0x10;
                    backup = NewSoundBackup(target);
                }
                target = RestorePublicSound(before, backup.Original);
                CheckSoundFieldsUnchanged(before, target);
            }
            else backup = NewSoundBackup(before);
            backup.Pending = Codec.ToHex(PublicSoundBytes(before));
            AtomicWrite(SoundPath, json.Serialize(backup)); // Flushed before the first mutation.
            try
            {
                WriteSoundTable(target); backup.Pending = null; AtomicWrite(SoundPath, json.Serialize(backup));
                return CurrentHasSound(target);
            }
            catch
            {
                // Validate current non-buzzer fields before rollback too. An external LED
                // edit or a lost reader leaves the journal intact instead of being overwritten.
                try
                {
                    CheckSoundIdentity(); byte[] live = ReadSoundTable();
                    if (live.Length != before.Length) throw new InvalidDataException("Läsarens ljudformat har ändrats.");
                    byte[] rollback = RestorePublicSound(live, backup.Pending);
                    CheckSoundFieldsUnchanged(live, rollback);
                    WriteSoundTable(rollback); backup.Pending = null; AtomicWrite(SoundPath, json.Serialize(backup));
                }
                catch { }
                throw;
            }
        }
    }

    // Pinned Lucide assets retain their upstream licence in Assets/Lucide/LICENSE.txt.
    internal sealed class SoundButton : Button
    {
        internal bool ReaderIcon, Muted;
        internal bool StateKnown = true;
        private readonly Image onIcon, offIcon;
        internal SoundButton(bool reader)
        {
            ReaderIcon = reader; Size = new Size(44, 44); MinimumSize = Size; MaximumSize = Size;
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
            int size = (int)Math.Round(20 * scale), top = (int)Math.Round(4 * scale);
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
                if (StateKnown) e.Graphics.DrawImage(icon, iconArea, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, tint);
                else TextRenderer.DrawText(e.Graphics, "…", Ui.GetFont(12F), iconArea, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Rectangle labelArea = new Rectangle(0, Height - (int)Math.Round(16 * scale), Width, (int)Math.Round(12 * scale));
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
            readerSoundButton.StateKnown = readerSound.HasValue; readerSoundButton.Muted = readerSound == false; readerSoundButton.Enabled = !busy && IsReady && readerSound.HasValue;
            appSoundButton.Muted = !workflow.Sounds; appSoundButton.Enabled = !busy;
            string r = readerSound.HasValue ? "Läsarens pip: " + (readerSound.Value ? "på. Klicka för att stänga av." : "av. Klicka för att slå på.") : reader != null && reader.Compatibility != null ? reader.Compatibility.SoundExplanation : "Läsarens pip: anslut läsaren för att kontrollera ljudläget.";
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
