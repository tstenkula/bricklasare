namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Drawing;
    using System.IO;
    using System.Web.Script.Serialization;
    using System.Windows.Forms;

    internal sealed class AppearanceDialog : Form
    {
        private readonly string directory;
        private readonly TextBox organization = new TextBox { Dock = DockStyle.Top, MaxLength = 80, AccessibleName = "Organisationens namn", TabIndex = 0 };
        private readonly TextBox subtitle = new TextBox { Dock = DockStyle.Top, MaxLength = 120, AccessibleName = "Underrubrik", TabIndex = 1 };
        private readonly TextBox color = new TextBox { Width = 120, MaxLength = 7, AccessibleName = "Accentfärg i hexadecimal form" };
        private readonly ComboBox presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, DropDownWidth = 280, AccessibleName = "Färgtema" };
        private readonly Panel preview = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 135), Dock = DockStyle.Top, Padding = new Padding(14) };
        private readonly PictureBox logo = new PictureBox { Size = new Size(150, 55), MaximumSize = new Size(150, 55), SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "Förhandsvisning av logotyp" };
        private readonly Label titlePreview = Ui.Label("", 12F, true);
        private readonly Label subtitlePreview = Ui.Label("", 9.5F, false);
        private readonly Label colorError = Ui.Label("", 10F, false);
        private readonly Button previewButton = Ui.Button("Läs bricka", true);
        private readonly Button save = Ui.Button("Spara utseende", true);
        internal AppearanceDialog() : this(ReceptionFiles.DataPath) { }
        internal AppearanceDialog(string dataDirectory)
        {
            directory = dataDirectory;
            Text = "IT-inställningar · Utseende"; Font = Ui.GetFont(10F); BackColor = Ui.Paper;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(590, 740); MinimumSize = new Size(560, 420);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F); MinimizeBox = false;
            SuspendLayout();
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabIndex = 0 };
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(22); scroll.Controls.Add(root); Controls.Add(scroll);
            Ui.Add(root, Ui.Label("Färg och logotyp", 20F, true));
            Label explanation = Ui.Label("Sparas för ditt Windows-konto. Förhandsvisningen ändrar inte programmet förrän du väljer Spara.", 10F, false);
            explanation.MaximumSize = new Size(500, 0); Ui.Add(root, explanation);
            Ui.Add(root, Ui.Label("Organisationens namn (valfritt)", 10F, true)); Ui.Add(root, organization);
            Ui.Add(root, Ui.Label("Underrubrik (valfritt)", 10F, true)); Ui.Add(root, subtitle);
            Ui.Add(root, Ui.Label("Accentfärg", 10F, true));
            FlowLayoutPanel colors = Ui.Row(); colors.TabIndex = 2; presets.TabIndex = 0; color.TabIndex = 1; presets.Items.AddRange(new object[] { "Marinblå", "Grön", "Lila", "Egen färg" });
            Button picker = Ui.Button("Välj färg…", false);
            picker.TabIndex = 2;
            colors.Controls.Add(presets); colors.Controls.Add(color); colors.Controls.Add(picker); Ui.Add(root, colors);
            Ui.Add(root, colorError);
            FlowLayoutPanel images = Ui.Row(); images.TabIndex = 3; Button choose = Ui.Button("Välj logotyp…", false), remove = Ui.Button("Ta bort logotyp", false);
            choose.TabIndex = 0; remove.TabIndex = 1;
            images.Controls.Add(choose); images.Controls.Add(remove); Ui.Add(root, images);
            Label imageHelp = Ui.Label("PNG, högst 2 MB och 2048 × 2048 bildpunkter. En egen kopia sparas; originalbilden kan sedan flyttas.", 9.5F, false);
            imageHelp.MaximumSize = new Size(500, 0); Ui.Add(root, imageHelp);
            Ui.Add(root, Ui.Label("Förhandsvisning", 10F, true));
            TableLayoutPanel sample = Ui.Stack(); Ui.Add(sample, logo); Ui.Add(sample, titlePreview); Ui.Add(sample, subtitlePreview);
            sample.SizeChanged += delegate { Ui.WrapLabels(sample); }; previewButton.TabStop = false;
            FlowLayoutPanel buttonRow = Ui.Row(); buttonRow.Controls.Add(previewButton); Ui.Add(sample, buttonRow); preview.Controls.Add(sample); Ui.Add(root, preview);
            FlowLayoutPanel actions = Ui.Row(); Button reset = Ui.Button("Återställ", false), cancel = Ui.Button("Avbryt", false);
            actions.Controls.Add(save); actions.Controls.Add(reset); actions.Controls.Add(cancel);
            actions.Dock = DockStyle.Bottom; actions.Padding = new Padding(22, 10, 22, 10); actions.TabIndex = 1;
            save.TabIndex = 0; reset.TabIndex = 1; cancel.TabIndex = 2; Controls.Add(actions);
            root.SizeChanged += delegate { Ui.WrapLabels(root); };
            AppearanceSettings current = AppearanceFiles.Load(directory);
            organization.Text = current.Organization; subtitle.Text = current.Subtitle; color.Text = current.AccentHex;
            ReplaceLogo(AppearanceFiles.LoadLogo(directory, current));
            SyncPreset();
            organization.TextChanged += delegate { UpdatePreview(); }; subtitle.TextChanged += delegate { UpdatePreview(); };
            color.TextChanged += delegate { SyncPreset(); UpdatePreview(); };
            presets.SelectedIndexChanged += delegate
            {
                if (presets.SelectedIndex >= 0 && presets.SelectedIndex < 3)
                    color.Text = new string[] { AppearanceSettings.DefaultAccentHex, "#176B4D", "#7056A7" }[presets.SelectedIndex];
            };
            picker.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog())
                {
                    dialog.FullOpen = true;
                    try { dialog.Color = ThemePalette.Parse(color.Text); } catch (InvalidDataException) { dialog.Color = Ui.Accent; }
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        color.Text = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                }
            };
            choose.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Filter = "PNG-bilder (*.png)|*.png", Title = "Välj logotyp", CheckFileExists = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { ReplaceLogo(AppearanceFiles.ReadLogo(dialog.FileName)); UpdatePreview(); }
                    catch (Exception ex) { ReceptionFiles.RecordFailure(ex); MessageBox.Show(this, "Bilden kunde inte öppnas. Välj en giltig PNG-bild på högst 2 MB och 2048 × 2048 bildpunkter.", "Logotyp", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
            };
            remove.Click += delegate { ReplaceLogo(null); UpdatePreview(); };
            reset.Click += delegate { organization.Clear(); subtitle.Clear(); color.Text = AppearanceSettings.DefaultAccentHex; ReplaceLogo(null); UpdatePreview(); };
            save.Click += delegate
            {
                try
                {
                    AppearanceSettings settings = AppearanceSettings.Default();
                    settings.Organization = organization.Text.Trim(); settings.Subtitle = subtitle.Text.Trim(); settings.AccentHex = color.Text.ToUpperInvariant();
                    AppearanceFiles.Save(directory, settings, logo.Image);
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex) { ReceptionFiles.RecordFailure(ex); MessageBox.Show(this, "Utseendet kunde inte sparas. Kontrollera färg, namn och att din lokala datamapp är skrivbar. Tidigare inställningar behålls.", "Kunde inte spara", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = save; CancelButton = cancel;
            Shown += delegate { FitToScreen(); organization.Focus(); };
            UpdatePreview(); ResumeLayout(true);
        }
        private void FitToScreen()
        {
            Ui.FitToScreen(this);
        }
        private void SyncPreset()
        {
            string hex = color.Text.ToUpperInvariant();
            presets.SelectedIndex = hex == AppearanceSettings.DefaultAccentHex ? 0 : hex == "#176B4D" ? 1 : hex == "#7056A7" ? 2 : 3;
        }
        internal void ReplaceLogo(Image next)
        {
            Image previous = logo.Image; logo.Image = next;
            if (previous != null) previous.Dispose();
            logo.Visible = next != null; Ui.FitLogo(logo);
        }
        private void UpdatePreview()
        {
            titlePreview.Text = organization.Text.Trim().Length == 0 ? "Brickläsaren" : organization.Text.Trim();
            subtitlePreview.Text = subtitle.Text.Trim();
            subtitlePreview.Visible = subtitlePreview.Text.Length > 0;
            try
            {
                ThemePalette theme = new ThemePalette(color.Text);
                preview.BackColor = theme.Light; titlePreview.ForeColor = theme.TextAccent;
                previewButton.BackColor = theme.Accent; previewButton.ForeColor = theme.ButtonText;
                previewButton.FlatAppearance.MouseOverBackColor = previewButton.FlatAppearance.MouseDownBackColor = theme.Hover;
                colorError.Text = "Textfärgen anpassas automatiskt för läsbar kontrast."; colorError.ForeColor = Ui.Muted; save.Enabled = true;
            }
            catch (InvalidDataException) { colorError.Text = "Ange # följt av sex tecken, till exempel #003B55."; colorError.ForeColor = Color.FromArgb(127, 71, 9); save.Enabled = false; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) ReplaceLogo(null);
            base.Dispose(disposing);
        }
    }
    internal sealed class ReferenceDialog : Form
    {
        internal ReferenceDialog() : this(ReceptionFiles.DataPath) { }
        internal ReferenceDialog(string directory)
        {
            Text = "IT-inställningar · Referensprov"; Font = Ui.GetFont(10F); BackColor = Ui.Paper;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(600, 490); MinimumSize = new Size(560, 400);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F); AutoScroll = true; MinimizeBox = false;
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabIndex = 0 };
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(22); scroll.Controls.Add(root); Controls.Add(scroll);
            Ui.Add(root, Ui.Label("Referensprov för läsaren", 19F, true));
            Label intro = Ui.Label("Används endast när IT behöver verifiera läsarens byteordning. Vanliga taggar behöver inte registreras här. En läsare med verifierad profil behöver inget nytt referensprov för varje tagg.", 10F, false);
            intro.MaximumSize = new Size(530, 0); Ui.Add(root, intro);
            TextBox mifare = new TextBox { Dock = DockStyle.Top, MaxLength = 20, AccessibleName = "Verifierat MIFARE-UID", TabIndex = 0 };
            TextBox em = new TextBox { Dock = DockStyle.Top, MaxLength = 10, AccessibleName = "Verifierat EM-värde", TabIndex = 1 };
            Ui.Add(root, Ui.Label("MIFARE-UID · 8, 14 eller 20 hexadecimala tecken", 10F, true)); Ui.Add(root, mifare);
            Ui.Add(root, Ui.Label("EM · 10 hexadecimala tecken", 10F, true)); Ui.Add(root, em);
            Label status = Ui.Label("", 10F, false); status.MaximumSize = new Size(530, 0); Ui.Add(root, status);
            string path = Path.Combine(directory, ReferenceCard.FileName);
            try { ReferenceCard card = ReferenceCard.Read(path); mifare.Text = card.MifareHex; em.Text = card.EmHex; }
            catch (Exception) { status.Text = "Inget giltigt referensunderlag är sparat. Detta hindrar inte vanlig läsning med en redan verifierad läsarprofil."; }
            CheckBox verified = new CheckBox { AutoSize = true, MaximumSize = new Size(520, 0), Text = "Jag har fastställt båda värdena med en oberoende, verifierad metod.", TabIndex = 2 };
            Ui.Add(root, verified);
            FlowLayoutPanel actions = Ui.Row(); Button save = Ui.Button("Spara referensunderlag", true), cancel = Ui.Button("Avbryt", false);
            save.Enabled = false; verified.CheckedChanged += delegate { save.Enabled = verified.Checked; };
            actions.Controls.Add(save); actions.Controls.Add(cancel); actions.Dock = DockStyle.Bottom; actions.Padding = new Padding(22, 10, 22, 14); actions.TabIndex = 1;
            save.TabIndex = 0; cancel.TabIndex = 1; Controls.Add(actions);
            root.SizeChanged += delegate { Ui.WrapLabels(root); verified.MaximumSize = new Size(Math.Max(120, root.ClientSize.Width - root.Padding.Horizontal - 8), 0); };
            save.Click += delegate
            {
                if (!verified.Checked) return;
                try
                {
                    ReferenceCard card = ReferenceCard.Validate(new ReferenceCard { MifareHex = mifare.Text.Trim(), EmHex = em.Text.Trim() });
                    ReceptionFiles.WriteAtomic(path, new JavaScriptSerializer().Serialize(card));
                    DialogResult = DialogResult.OK; Close();
                }
                catch (InvalidDataException ex) { status.Text = ex.Message; }
                catch (Exception ex) { ReceptionFiles.RecordFailure(ex); status.Text = "Kunde inte spara. Kontrollera att din lokala datamapp är skrivbar."; }
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            FormClosed += delegate { mifare.Clear(); em.Clear(); };
            AcceptButton = save; CancelButton = cancel;
        }
    }
}
