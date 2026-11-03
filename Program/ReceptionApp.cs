// Reception UI, C# 5 / Windows Forms. Local reference card data is not part of the source.
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    public static class ReceptionProgram
    {
        public static void Run(string packagePath)
        {
            ReceptionFiles.PackagePath = Path.GetFullPath(packagePath);
            bool owner;
            // Share the v2 mutex: never let two versions control the reader at once.
            using (Mutex mutex = new Mutex(true, "Local\\Tobtech.OmnikeyRcoV2", out owner))
            {
                if (!owner) { MessageBox.Show("Brickläsaren är redan öppen. Stäng den andra versionen först.", "Brickläsaren"); return; }
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Ui.SetAppearance(AppearanceFiles.Load(ReceptionFiles.DataPath));
                    using (ReceptionForm form = new ReceptionForm()) Application.Run(form);
                }
                finally { Ui.DisposeFonts(); mutex.ReleaseMutex(); }
            }
        }
    }
    internal sealed class UiCard : TableLayoutPanel
    {
        private Color surface = Color.White;
        internal Color SurfaceColor { get { return surface; } set { if (surface != value) { surface = value; Invalidate(true); } } }
        internal UiCard()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true; BackColor = Color.Transparent;
            AutoSize = true; Dock = DockStyle.Top; ColumnCount = 1; Margin = new Padding(0);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            float diameter = Math.Min(16F * e.Graphics.DpiX / 96F, Math.Min(ClientSize.Width - 1, ClientSize.Height - 1));
            RectangleF area = new RectangleF(0.5F, 0.5F, ClientSize.Width - 1F, ClientSize.Height - 1F);
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush brush = new SolidBrush(surface))
            using (Pen border = new Pen(Color.FromArgb(225, 232, 236)))
            {
                path.AddArc(area.Left, area.Top, diameter, diameter, 180, 90);
                path.AddArc(area.Right - diameter, area.Top, diameter, diameter, 270, 90);
                path.AddArc(area.Right - diameter, area.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(area.Left, area.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
                SmoothingMode previous = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(border, path); e.Graphics.SmoothingMode = previous;
            }
        }
    }
    internal static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(27, 48, 63);
        public static readonly Color Muted = Color.FromArgb(75, 95, 111);
        public static ThemePalette Theme = new ThemePalette(AppearanceSettings.DefaultAccentHex);
        public static Color Accent { get { return Theme.Accent; } }
        public static Color AccentDark { get { return Theme.TextAccent; } }
        public static Color AccentLight { get { return Theme.Light; } }
        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        public static Font GetFont(float size, bool bold = false, string family = "Segoe UI")
        {
            string key = family + ":" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + bold;
            Font font;
            if (!fonts.TryGetValue(key, out font))
            {
                font = new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular);
                fonts.Add(key, font);
            }
            return font;
        }
        public static int FontCount { get { return fonts.Count; } }
        public static void DisposeFonts()
        {
            foreach (Font font in fonts.Values) font.Dispose();
            fonts.Clear();
        }
        public static void SetAppearance(AppearanceSettings settings) { Theme = new ThemePalette(settings.AccentHex); }
        private static void StyleButton(Button button)
        {
            bool primary = Object.Equals(button.Tag, "primary");
            button.BackColor = primary ? (button.Enabled ? Accent : Color.FromArgb(220, 230, 235)) : Color.White;
            button.ForeColor = primary ? (button.Enabled ? Theme.ButtonText : Muted) : Ink;
            button.FlatAppearance.MouseOverBackColor = primary ? Theme.Hover : AccentLight;
            button.FlatAppearance.MouseDownBackColor = primary ? Theme.Hover : AccentLight;
        }
        public static void RefreshButtons(Control root)
        {
            Button button = root as Button;
            if (button != null && (Object.Equals(button.Tag, "primary") || Object.Equals(button.Tag, "secondary"))) StyleButton(button);
            foreach (Control child in root.Controls) RefreshButtons(child);
        }
        public static readonly Color Paper = Color.FromArgb(245, 248, 249);
        public static Label Label(string text, float size, bool bold)
        {
            return new Label { Text = text, AutoSize = true, Dock = DockStyle.Top,
                Font = GetFont(size, bold),
                ForeColor = Ink, Margin = new Padding(0, 0, 0, 6), TabStop = false };
        }
        public static Button Button(string text, bool primary)
        {
            Button b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat, Font = GetFont(11F, true),
                Padding = new Padding(15, 7, 15, 7), Margin = new Padding(0, 0, 12, 0),
                MinimumSize = new Size(100, 45), Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
                Tag = primary ? "primary" : "secondary", BackColor = primary ? Accent : Color.White, ForeColor = primary ? Theme.ButtonText : Ink };
            b.AccessibleName = text;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(186, 200, 203);
            StyleButton(b);
            b.EnabledChanged += delegate { StyleButton(b); };
            return b;
        }
        public static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
                Margin = new Padding(0, 3, 0, 9) };
        }
        public static TableLayoutPanel Stack()
        {
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return p;
        }
        public static void Add(TableLayoutPanel p, Control c)
        {
            int row = p.RowCount++; p.RowStyles.Add(new RowStyle(SizeType.AutoSize)); p.Controls.Add(c, 0, row);
        }
        internal static void WrapLabels(Control parent)
        {
            int width = Math.Max(120, parent.ClientSize.Width - parent.Padding.Horizontal - 8);
            foreach (Control child in parent.Controls)
            {
                Label label = child as Label;
                if (label != null && label.AutoSize && label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
            }
        }
    }
    public sealed partial class ReceptionForm : Form
    {
        private Icon appIcon;
        private WorkflowSettings workflow;
        private readonly System.Windows.Forms.Timer reconnectTimer = new System.Windows.Forms.Timer { Interval = 1800 };
        private bool reconnectPending;
        private readonly LocalLog log = new LocalLog();
        private ReaderClient reader;
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool cancelling;
        private bool reference;
        private bool profileChosen;
        private bool outputProtected;
        private string address;
        private string lastCopied = "";
        private bool statusWarning;
        private bool resourcesDisposed;
        private readonly string appearanceDirectory;
        private readonly Label brand = Ui.Label("", 9F, true);
        private readonly Label brandSubtitle = Ui.Label("", 10F, false);
        private readonly PictureBox logo = new PictureBox { Size = new Size(180, 64), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 8), AccessibleName = "Organisationens logotyp" };
        private string mode = FormatProfiles.Combo;
        private string rcoChip = "M";
        private readonly Label subtitle = Ui.Label("En bricka. Två passersystem.", 11F, false);
        private readonly Label leftTitle = Ui.Label("Eget passersystem", 12F, true);
        private readonly Label leftDetail = Ui.Label("mR · MIFARE", 9.5F, false);
        private readonly Label rightTitle = Ui.Label("Hyresvärdens passersystem", 12F, true);
        private readonly Label rightDetail = Ui.Label("eR · EM", 9.5F, false);
        private readonly Label leftState = Ui.Label("Väntar på läsning", 9.5F, false);
        private readonly Label rightState = Ui.Label("Väntar på läsning", 9.5F, false);
        private Control rightCard;
        private TableLayoutPanel outputs;

        private readonly Label connection = Ui.Label("Ansluter läsaren …", 9.5F, false);
        private readonly Label stage = Ui.Label("VÄLKOMMEN", 9.5F, true);
        private readonly Label instruction = Ui.Label("Gör klart för en ny bricka", 20F, true);
        private readonly Label hint = Ui.Label("Håll brickan borta tills du blir ombedd att lägga på den.", 10.5F, false);
        private readonly Label notice = Ui.Label("", 10F, false);
        private readonly UiCard statusPanel = new UiCard();
        private readonly Button read = Ui.Button("Läs bricka", true);
        private readonly Button cancel = Ui.Button("Avbryt", false);
        private readonly Button copyM = Ui.Button("Kopiera till eget system", false);
        private readonly Button copyE = Ui.Button("Kopiera till hyresvärden", false);
        private readonly Button copyBoth = Ui.Button("Kopiera båda numren", false);
        private readonly Button clear = Ui.Button("Rensa", false);
        private readonly Button help = Ui.Button("Hjälp", false);
        private readonly Button setup = Ui.Button("Anslut läsare", false);
        private readonly TextBox mr = NumberBox("Första ID-numret");
        private readonly TextBox er = NumberBox("Andra ID-numret");
        private readonly TextBox keyboardSink = new TextBox { Location = new Point(-200, -200), Size = new Size(1, 1), TabStop = false, MaxLength = 256 };

        public ReceptionForm() : this(false, ReceptionFiles.DataPath) { }
        internal ReceptionForm(bool previewOnly, string appearancePath)
        {
            appearanceDirectory = appearancePath;
            workflow = WorkflowSettings.Load(appearancePath);
            if (!previewOnly) { reconnectTimer.Tick += async delegate { if (!busy && OwnedForms.Length == 0 && reconnectPending) { reconnectPending = false; reconnectTimer.Stop(); await ConnectReader(); } }; }
            if (!previewOnly) Directory.CreateDirectory(ReceptionFiles.DataPath);
            address = previewOnly ? "192.168.63.99" : ReceptionFiles.LoadAddress(log);
            FormatPreference preference = previewOnly ? null : ReceptionFiles.LoadFormatPreference();
            profileChosen = preference != null;
            if (profileChosen) { mode = preference.Mode; rcoChip = preference.RcoChip; }

            string iconPath = Path.Combine(ReceptionFiles.PackagePath, "Program", "Bricklasaren.ico");
            if (File.Exists(iconPath)) { try { using (Icon source = new Icon(iconPath)) appIcon = (Icon)source.Clone(); Icon = appIcon; } catch (ArgumentException) { } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            Text = "Brickläsaren · 3.6.0";
            Font = Ui.GetFont(10F); ForeColor = Ui.Ink; BackColor = Ui.Paper;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 650); MinimumSize = new Size(800, 600);
            AutoScroll = true; KeyPreview = true; DoubleBuffered = true;
            SuspendLayout();
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(24, 20, 24, 16); root.TabIndex = 0; Controls.Add(root);
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12), TabIndex = 3 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            TableLayoutPanel heading = Ui.Stack();
            heading.SizeChanged += delegate { Ui.WrapLabels(heading); };
            Ui.Add(heading, logo); Ui.Add(heading, brand); Ui.Add(heading, brandSubtitle);
            Ui.Add(heading, Ui.Label("Brickläsaren", 22F, true));
            subtitle.ForeColor = Ui.Muted; Ui.Add(heading, subtitle);
            TableLayoutPanel tools = Ui.Stack(); tools.Margin = new Padding(18, 3, 0, 0);
            connection.ForeColor = Ui.Muted; connection.MaximumSize = new Size(250, 0); connection.TextAlign = ContentAlignment.MiddleRight;
            connection.AccessibleRole = AccessibleRole.StaticText;
            help.Anchor = AnchorStyles.Top | AnchorStyles.Right; help.Margin = new Padding(0, 4, 0, 0); help.TabIndex = 0;
            Ui.Add(tools, connection); Ui.Add(tools, help);
            header.Controls.Add(heading, 0, 0); header.Controls.Add(tools, 1, 0); Ui.Add(root, header);
            statusPanel.SurfaceColor = Ui.AccentLight; statusPanel.Padding = new Padding(18, 12, 18, 12);
            statusPanel.Margin = new Padding(0, 0, 0, 14); statusPanel.TabIndex = 0; statusPanel.MinimumSize = new Size(0, 132);
            statusPanel.ColumnCount = 2; statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusPanel.RowCount = 1; statusPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stage.ForeColor = Ui.Accent; hint.ForeColor = Ui.Muted;
            stage.Margin = new Padding(0, 0, 0, 2); instruction.Margin = new Padding(0, 0, 0, 4); hint.Margin = new Padding(0, 0, 0, 10);
            instruction.AccessibleRole = AccessibleRole.StaticText; hint.AccessibleRole = AccessibleRole.StaticText;
            TableLayoutPanel instructions = Ui.Stack(); Ui.Add(instructions, stage); Ui.Add(instructions, instruction); Ui.Add(instructions, hint);
            instructions.Anchor = AnchorStyles.Left; instructions.Dock = DockStyle.Fill;
            instructions.SizeChanged += delegate { Ui.WrapLabels(instructions); }; statusPanel.Controls.Add(instructions, 0, 0);
            FlowLayoutPanel actions = Ui.Row(); actions.FlowDirection = FlowDirection.TopDown; actions.WrapContents = false;
            actions.Dock = DockStyle.None; actions.Anchor = AnchorStyles.Right; actions.Margin = new Padding(20, 0, 0, 0); actions.TabIndex = 0;
            read.MinimumSize = new Size(200, 44); cancel.MinimumSize = new Size(200, 44); setup.MinimumSize = new Size(200, 44);
            read.Margin = new Padding(0); cancel.Margin = new Padding(0, 8, 0, 0); setup.Margin = new Padding(0, 8, 0, 0);
            read.TabIndex = 0; cancel.TabIndex = 1; setup.TabIndex = 2;
            read.AccessibleDescription = "Starta en läsning med Ctrl+R."; cancel.AccessibleDescription = "Avbryt läsningen med Escape.";
            actions.Controls.Add(read); actions.Controls.Add(cancel); actions.Controls.Add(setup); statusPanel.Controls.Add(actions, 1, 0); Ui.Add(root, statusPanel);
            TableLayoutPanel resultHeader = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 0, 0, 8), TabIndex = 2 };
            resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); resultHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label resultTitle = Ui.Label("Brickans nummer", 12F, true); resultTitle.Anchor = AnchorStyles.Left; resultTitle.Dock = DockStyle.None; resultTitle.Margin = new Padding(0);
            resultHeader.Controls.Add(resultTitle, 0, 0);
            foreach (Button small in new Button[] { copyBoth, clear }) { small.Font = Ui.GetFont(9.5F); small.MinimumSize = new Size(70, 34); small.Padding = new Padding(10, 4, 10, 4); small.Margin = new Padding(8, 0, 0, 0); }
            copyBoth.TabIndex = 0; clear.TabIndex = 1; copyBoth.AccessibleDescription = "Kopiera båda numren med Ctrl+3.";
            clear.AccessibleDescription = "Rensa visade nummer och urklippet om det fortfarande innehåller det kopierade resultatet.";
            resultHeader.Controls.Add(copyBoth, 1, 0); resultHeader.Controls.Add(clear, 2, 0); Ui.Add(root, resultHeader);
            outputs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10), TabIndex = 1 };
            outputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); outputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            outputs.Controls.Add(ResultCard(leftTitle, leftDetail, mr, leftState, copyM, true), 0, 0);
            rightCard = ResultCard(rightTitle, rightDetail, er, rightState, copyE, false);
            outputs.Controls.Add(rightCard, 1, 0); Ui.Add(root, outputs);
            notice.ForeColor = Ui.Accent; notice.AccessibleRole = AccessibleRole.StaticText; notice.Visible = false;
            notice.TextChanged += delegate { notice.Visible = notice.Text.Length > 0; }; Ui.Add(root, notice); Controls.Add(keyboardSink);
            root.SizeChanged += delegate
            {
                SetWrap(statusPanel);
                int width = Math.Max(200, root.ClientSize.Width - root.Padding.Horizontal - 8);
                notice.MaximumSize = new Size(width, 0);
            };
            help.Click += async delegate { await ShowHelp(); };
            setup.Click += async delegate { await SetupReader(); };
            read.Click += async delegate { await Scan(false); };
            cancel.Click += delegate { RequestCancel(); };
            copyM.Click += delegate { Copy(mr.Text, "Numret är kopierat."); };
            copyE.Click += delegate { Copy(er.Text, "Numret är kopierat."); };
            copyBoth.Click += delegate { if (HasResults && mode == FormatProfiles.Combo) Copy(ReceptionText.Both(mr.Text, er.Text), "Båda numren är kopierade med rubriker."); };
            clear.Click += delegate { ClearDisplayedResults(); };
            if (!previewOnly) Shown += async delegate
            {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                if (Width > work.Width || Height > work.Height)
                {
                    MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
                    Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
                    Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
                }
                if (!profileChosen && !IsDisposed && !Disposing)
                {
                    using (FormatSelector first = new FormatSelector(null, "M", true))
                    {
                        if (first.ShowDialog(this) != DialogResult.OK) { Close(); return; }
                        mode = first.Mode; rcoChip = first.Chip; profileChosen = true;
                        ApplyModeUi();
                    }
                }
                await ConnectReader();
                if (reader == null && !IsDisposed && !Disposing) await SetupReader();
                if (!IsDisposed && workflow.AutoCheck) await CheckUpdates(true);
            };
            Activated += delegate { if (busy) keyboardSink.Focus(); };
            keyboardSink.KeyPress += delegate(object sender, KeyPressEventArgs e) { e.Handled = true; };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!busy) return;
                e.Cancel = true; RequestCancel();
                notice.Text = "Låt programmet arbeta klart. Stäng sedan fönstret igen.";
            };
            log.Add("Receptionsgränssnitt 3.6.0 (PowerShell). Lokal diagnostik utan råa kortdata.");
            ApplyAppearance(); ApplyModeUi(); UpdateButtons(); ResumeLayout(true);
            if (previewOnly) connection.Text = "Förhandsvisning · ingen läsarkontakt";
        }
        private void ApplyAppearance()
        {
            AppearanceSettings settings = AppearanceFiles.Load(appearanceDirectory);
            Ui.SetAppearance(settings); Ui.RefreshButtons(this);
            brand.MaximumSize = new Size(570, 0); brandSubtitle.MaximumSize = new Size(570, 0);
            brand.Text = settings.Organization; brand.Visible = brand.Text.Length > 0; brand.ForeColor = Ui.AccentDark;
            brandSubtitle.Text = settings.Subtitle; brandSubtitle.Visible = brandSubtitle.Text.Length > 0; brandSubtitle.ForeColor = Ui.Muted;
            Image previous = logo.Image;
            logo.Image = AppearanceFiles.LoadDisplayLogo(appearanceDirectory, settings, new Size(Math.Max(1, logo.Width * 2), Math.Max(1, logo.Height * 2)));
            if (previous != null) previous.Dispose();
            logo.Visible = logo.Image != null;
            notice.ForeColor = Ui.AccentDark;
            statusPanel.SurfaceColor = statusWarning ? Color.FromArgb(255, 241, 221) : Ui.AccentLight;
            stage.ForeColor = statusWarning ? Color.FromArgb(127, 71, 9) : Ui.AccentDark;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                resourcesDisposed = true; reconnectTimer.Stop(); reconnectTimer.Dispose();
                Icon = null; if (appIcon != null) { appIcon.Dispose(); appIcon = null; }
                ClearResults();
                Image previous = logo.Image; logo.Image = null;
                if (previous != null) previous.Dispose();
            }
            base.Dispose(disposing);
        }
        private static TextBox NumberBox(string name)
        {
            return new TextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White,
                ForeColor = Ui.Ink, Dock = DockStyle.Top, TextAlign = HorizontalAlignment.Left, Font = Ui.GetFont(32F, true, "Consolas"),
                Margin = new Padding(0, 5, 0, 10), TabStop = false, AccessibleName = name, Text = "—" };
        }
        private static Control ResultCard(Label title, Label detail, TextBox number, Label state, Button copy, bool left)
        {
            UiCard card = new UiCard(); card.Dock = DockStyle.Fill; card.Padding = new Padding(16, 12, 16, 12); card.TabIndex = left ? 0 : 1;
            card.Margin = left ? new Padding(0, 0, 8, 0) : new Padding(8, 0, 0, 0);
            title.Margin = new Padding(0, 0, 0, 4); detail.Margin = new Padding(0);
            number.Margin = new Padding(0, 3, 0, 6); state.ForeColor = Ui.Muted; state.AutoSize = false; state.Height = 18; state.Margin = new Padding(0, 0, 0, 6);
            copy.AccessibleDescription = left ? "Kopiera det första numret med Ctrl+1." : "Kopiera det andra numret med Ctrl+2."; copy.TabIndex = 0;
            copy.Font = Ui.GetFont(10F, true); copy.MinimumSize = new Size(100, 40); copy.Padding = new Padding(10, 5, 10, 5);
            Ui.Add(card, title); detail.ForeColor = Ui.Muted; Ui.Add(card, detail); Ui.Add(card, number); Ui.Add(card, state);
            copy.Dock = DockStyle.Top; copy.Margin = new Padding(0); Ui.Add(card, copy);
            card.SizeChanged += delegate { Ui.WrapLabels(card); }; return card;
        }
        private void SetWrap(Control p)
        {
            Ui.WrapLabels(p);
            foreach (Control child in p.Controls) if (child is TableLayoutPanel) Ui.WrapLabels(child);
        }
        // Keyboard-wedge output is not our data source. Ignore Enter and digits on this form.
        // This is local to our window, not a global keyboard hook.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (!busy && keyData == (Keys.Control | Keys.R)) { read.PerformClick(); return true; }
            if (!busy && keyData == (Keys.Control | Keys.D1)) { copyM.PerformClick(); return true; }
            if (!busy && keyData == (Keys.Control | Keys.D2)) { copyE.PerformClick(); return true; }
            if (!busy && keyData == (Keys.Control | Keys.D3)) { copyBoth.PerformClick(); return true; }
            if (busy && k == Keys.Escape) { RequestCancel(); return true; }
            if (busy && k != Keys.F4 && k != Keys.Menu) return true;
            if (k == Keys.Enter || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            // A USB/power event invalidates results immediately. Reconnect only after any
            // running scan has completed its journalled restoration and dialogs have closed.
            if (!resourcesDisposed && ((m.Msg == 0x219 && (m.WParam.ToInt64() == 0x8000 || m.WParam.ToInt64() == 0x8004)) ||
                (m.Msg == 0x218 && (m.WParam.ToInt64() == 18 || m.WParam.ToInt64() == 7))))
                InvalidateReaderConnection();
            base.WndProc(ref m);
        }
        private void InvalidateReaderConnection()
        {
            ClearResults(); outputProtected = false; RequestCancel();
            reconnectPending = true; reconnectTimer.Start(); UpdateButtons();
        }
        private bool HasResults
        {
            get
            {
                if (mode == FormatProfiles.Combo) return FormatProfiles.IsDigits(mr.Text, 9) && FormatProfiles.IsDigits(er.Text, 9);
                if (mode == FormatProfiles.Rco) return FormatProfiles.IsDigits(mr.Text, 9) && FormatProfiles.IsDigits(er.Text, 10);
                if (mode == FormatProfiles.Axema) return FormatProfiles.IsDigits(mr.Text, 14);
                return FormatProfiles.IsDigits(mr.Text, 13);
            }
        }
        private void ApplyModeUi()
        {
            SuspendLayout(); outputs.SuspendLayout();
            subtitle.Text = profileChosen ? FormatProfiles.Label(mode) : "Välj passersystem vid första starten";
            bool hasTwo = mode == FormatProfiles.Combo || mode == FormatProfiles.Rco;
            rightCard.Visible = hasTwo;
            outputs.ColumnStyles[0].Width = hasTwo ? 50 : 100;
            outputs.ColumnStyles[1].Width = hasTwo ? 50 : 0;
            copyBoth.Visible = mode == FormatProfiles.Combo;
            if (mode == FormatProfiles.Combo)
            {
                leftTitle.Text = "Eget passersystem"; leftDetail.Text = "mR · MIFARE";
                rightTitle.Text = "Hyresvärdens passersystem"; rightDetail.Text = "eR · EM";
                copyM.Text = "Kopiera till eget system"; copyE.Text = "Kopiera till hyresvärden";
            }
            else if (mode == FormatProfiles.Rco)
            {
                leftTitle.Text = "RCO · kort ID-kod"; leftDetail.Text = (rcoChip == "M" ? "MIFARE" : "EM") + " · 9 siffror";
                rightTitle.Text = "RCO · lång ID-kod"; rightDetail.Text = (rcoChip == "M" ? "MIFARE" : "EM") + " · 10 siffror";
                copyM.Text = "Kopiera kort ID"; copyE.Text = "Kopiera långt ID";
            }
            else if (mode == FormatProfiles.Axema)
            {
                leftTitle.Text = "Axema VAKA"; leftDetail.Text = "EM · 14 siffror";
                copyM.Text = "Kopiera VAKA-numret";
            }
            else
            {
                leftTitle.Text = "Bewator 2010 / Omnis"; leftDetail.Text = "EM · 13 siffror";
                copyM.Text = "Kopiera Bewator-numret";
            }
            mr.AccessibleName = leftTitle.Text + ", " + leftDetail.Text;
            er.AccessibleName = rightTitle.Text + ", " + rightDetail.Text;
            copyM.AccessibleName = copyM.Text; copyE.AccessibleName = copyE.Text;
            ClearResults();
            if (reader != null) ReadyMessage();
            UpdateButtons(); outputs.ResumeLayout(true); ResumeLayout(true);
        }

        private bool IsReady { get { return !reconnectPending && profileChosen && outputProtected && reader != null && reader.CanRead && !reader.HasPendingRecovery; } }
        private void UpdateButtons()
        {
            setup.Visible = reader == null; setup.Enabled = !busy;
            read.Enabled = !busy && IsReady; read.Text = HasResults ? "Läs nästa bricka" : "Läs bricka";
            read.AccessibleName = read.Text;
            cancel.Visible = busy && cancellation != null; cancel.Enabled = cancel.Visible && !cancelling;
            copyM.Enabled = !busy && IsReady && HasResults;
            copyE.Enabled = !busy && IsReady && HasResults && (mode == FormatProfiles.Combo || mode == FormatProfiles.Rco);
            copyBoth.Enabled = !busy && IsReady && HasResults && mode == FormatProfiles.Combo;
            clear.Enabled = !busy && HasResults;
            leftState.Text = HasResults ? "Ctrl+1 för att kopiera" : "Väntar på läsning";
            rightState.Text = HasResults ? "Ctrl+2 för att kopiera" : "Väntar på läsning";
            help.Enabled = !busy;
        }
        private void SetBusy(bool value)
        {
            busy = value; UpdateButtons();
            if (value) keyboardSink.Focus();
            else if (keyboardSink.Focused) FocusAction();
        }
        private void FocusAction()
        {
            if (!Visible || !ContainsFocus) return;
            if (read.Enabled) read.Focus();
            else if (setup.Visible && setup.Enabled) setup.Focus();
            else if (help.Enabled) help.Focus();
        }
        private bool TryBeginFileOperation(bool requiresReader)
        {
            // Native file dialogs pump Windows messages. A reconnect may have started
            // while the dialog was open; only claim an operation after it has closed.
            if (busy || IsDisposed || Disposing || (requiresReader && !IsReady)) return false;
            SetBusy(true); return true;
        }
        private void ClearDisplayedResults()
        {
            if (busy) return;
            ClearResults(); ReadyMessage(); UpdateButtons(); FocusAction();
        }
        private void SetInstruction(string eyebrow, string title, string text, bool warning)
        {
            if (IsDisposed || Disposing) return;
            statusPanel.SuspendLayout();
            statusWarning = warning; stage.Text = eyebrow; instruction.Text = title; hint.Text = text;
            statusPanel.SurfaceColor = warning ? Color.FromArgb(255, 241, 221) : Ui.AccentLight;
            stage.ForeColor = warning ? Color.FromArgb(127, 71, 9) : Ui.AccentDark;
            SetWrap(statusPanel); statusPanel.ResumeLayout(true);
        }
        private void Progress(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(delegate { Progress(message); })); } catch (InvalidOperationException) { } return; }
            if (cancelling) { SetInstruction("AVBRYTER", "Avslutar läsningen", "Håll brickan borta. Låt läsaren vara inkopplad tills detta är klart.", false); return; }
            ReceptionStep s = ReceptionText.Step(message, reference);
            WorkflowSettings.Cue(workflow.Sounds, message.StartsWith("Lägg", StringComparison.Ordinal) ? 1 : message.StartsWith("Lyft", StringComparison.Ordinal) ? 2 : 0);
            if (!reference && mode != FormatProfiles.Combo)
            {
                if (message.StartsWith("Lägg SAMMA", StringComparison.Ordinal) || message.StartsWith("Steg 2 av 2", StringComparison.Ordinal))
                    s = new ReceptionStep("LÄSER BRICKAN", "Lägg på brickan", "Håll kvar tills programmet ber dig lyfta bort den.");
                else if (message.StartsWith("Lägg brickan mot", StringComparison.Ordinal))
                    s = new ReceptionStep("LÄSER BRICKAN", "Lägg på brickan", "Håll kvar tills programmet ber dig lyfta bort den.");
                else if (message.StartsWith("MIFARE läst.", StringComparison.Ordinal) || message.StartsWith("EM läst.", StringComparison.Ordinal))
                    s = new ReceptionStep("ID-NUMRET ÄR LÄST", "Lyft bort brickan", "Resultatet visas när läsaren har återställts.");
            }
            SetInstruction(s.Eyebrow, s.Title, s.Hint, false);
        }
        private void ClearResults()
        {
            mr.Text = er.Text = "—"; notice.Text = ""; log.Clear();
            // Remove only a clipboard value that is still our own previous result.
            if (lastCopied.Length > 0)
            {
                try { if (Clipboard.ContainsText() && Clipboard.GetText() == lastCopied) Clipboard.Clear(); } catch { }
                lastCopied = "";
            }
        }
        private void ReadyMessage()
        {
            if (reader == null) return;
            if (reconnectPending)
            {
                connection.Text = "Anslutningen kontrolleras";
                SetInstruction("ANSLUTER", "Kontrollerar läsaren igen", "Låt USB-kabeln sitta i. Vänta tills läsaren är klar innan du lägger på en bricka.", false);
            }
            else if (reader.HasPendingRecovery)
            {
                connection.Text = "Läsaren behöver återställas";
                SetInstruction("HJÄLP BEHÖVS", "En läsning blev inte avslutad", "Låt läsaren vara inkopplad. Välj Hjälp och be IT återställa den.", true);
            }
            else if (!outputProtected)
            {
                connection.Text = "Läsaren är inte färdig att användas";
                SetInstruction("FÖRBEREDELSE KRÄVS", "Läsaren behöver förberedas", "Välj Hjälp och Försök ansluta igen. Läsning blir tillgänglig när skyddet mot tangentbordsinmatning är klart.", true);
            }
            else if (!profileChosen)
            {
                connection.Text = "Läsaren är ansluten";
                SetInstruction("FÖRSTA STARTEN", "Välj passersystem", "Valet sparas och används automatiskt nästa gång du öppnar programmet.", false);
            }
            else if (!reader.CanRead)
            {
                connection.Text = "Läsaren är ansluten";
                SetInstruction("KONTROLL BEHÖVS", "Kontrollera den sparade läsarprofilen", reader.CalibrationIssue, true);
            }
            else
            {
                connection.Text = "Läsaren är ansluten";
                SetInstruction("KLAR ATT BÖRJA", "Läs en ny bricka", "Håll brickan borta tills du blir ombedd att lägga på den.", false);
            }
        }
        private void PreparationProgress(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(delegate { PreparationProgress(message); })); }
                catch (InvalidOperationException) { }
                return;
            }
            connection.Text = "Förbereder läsaren …";
            SetInstruction("FÖRBEREDER", "Förbereder läsaren automatiskt", message, false);
        }
        private async Task ConnectReader()
        {
            if (busy) return;
            reconnectPending = false; reconnectTimer.Stop();
            ClearResults(); reader = null; outputProtected = false; SetBusy(true); connection.Text = "Ansluter läsaren …";
            SetInstruction("ANSLUTER", "Kontrollerar läsaren", "Låt USB-kabeln sitta i. Du behöver inte lägga på någon bricka ännu.", false);
            ReaderClient candidate = null;
            try
            {
                candidate = new ReaderClient(address, ReceptionFiles.DataPath, log);
                await Task.Run(delegate { candidate.Connect(); });
                reader = candidate;
                if (!candidate.HasPendingRecovery)
                {
                    candidate = await Task.Run(delegate { return ReaderSafety.EnsureProtected(candidate, address, ReceptionFiles.DataPath, log, PreparationProgress); });
                    reader = candidate; outputProtected = true;
                }
                string profile = Path.Combine(ReceptionFiles.PackagePath, ReceptionFiles.ProfileName);
                if (reader.Cal == null && !reader.HasPendingRecovery && File.Exists(profile))
                {
                    try
                    {
                        ReceptionFiles.ImportProfile(reader, profile);
                        await Task.Run(delegate { candidate.Connect(); });
                    }
                    catch (Exception profileError)
                    {
                        // A wrong profile must not disguise an otherwise working reader connection.
                        SaveError(profileError);
                        notice.Text = "Läsarprofilen kunde inte användas. Be IT kontrollera den via Hjälp.";
                    }
                }
                ReceptionFiles.SaveAddress(address); ReadyMessage();
            }
            catch (Exception ex)
            {
                outputProtected = false;
                reader = candidate != null && !String.IsNullOrEmpty(candidate.ReaderId) ? candidate : null;
                SaveError(ex);
                if (reader == null)
                {
                    connection.Text = "Ingen läsare är ansluten";
                    SetInstruction("INGEN KONTAKT", "Kontrollera USB-kabeln", "Välj Hjälp och Försök ansluta igen. Kontakta IT om det inte hjälper.", true);
                }
                else
                {
                    connection.Text = "Läsaren kunde inte förberedas";
                    SetInstruction("FÖRBEREDELSEN ÄR INTE KLAR", "Låt läsaren vara inkopplad", "Välj Hjälp och Försök ansluta igen. Inga brickor kan skannas innan förberedelsen är klar.", true);
                }
            }
            finally { SetBusy(false); }
        }
        private async Task SetupReader()
        {
            if (busy) return;
            string result = "";
            while (!IsDisposed && !Disposing)
            {
                using (FirstStartDialog dialog = new FirstStartDialog(result))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        if (reader != null) ReadyMessage();
                        else SetInstruction("INGEN KONTAKT", "Anslut läsaren när du är redo", "Välj Anslut läsare för att fortsätta. Ditt val av passersystem är sparat.", false);
                        return;
                    }
                    if (dialog.OpenDriver)
                    {
                        try { FirstStart.OpenDriverPage(); result = "Tillverkarens sida är öppnad. Efter installationen: välj Försök ansluta."; }
                        catch (Exception ex) { SaveError(ex); result = "Öppna " + FirstStart.DriverUrl + " i webbläsaren och hämta EEM-drivrutinen."; }
                        continue;
                    }
                }
                await ConnectReader();
                if (reader != null) return;
                result = "Ingen kontakt ännu. Kontrollera USB-kabeln. Om drivrutinen redan är installerad kan IT kontrollera EEM och läsaradressen under Hjälp.";
            }
        }
        private async Task Scan(bool isReference)
        {
            if (busy || reconnectPending || !profileChosen || !outputProtected || reader == null || reader.HasPendingRecovery || (!isReference && !reader.CanRead)) return;
            if (isReference)
            {
                try { ReferenceCard.Read(Path.Combine(ReceptionFiles.DataPath, ReferenceCard.FileName)); }
                catch (Exception ex)
                {
                    ReceptionFiles.RecordFailure(ex);
                    MessageBox.Show(this, "Referensbrickan saknas, är ogiltig eller kan inte läsas. Öppna Hjälp → IT-inställningar → Ställ in referensbricka och kontrollera uppgifterna. Ingen referensskanning har startats.", "Referensbricka behöver ställas in", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            reference = isReference; ReaderClient active = reader; ClearResults(); cancelling = false;
            string scanMode = isReference || mode == FormatProfiles.Combo ? "Both" : (mode == FormatProfiles.Rco ? rcoChip : "E");
            string chosenMode = mode; string chosenChip = rcoChip;
            cancellation = new CancellationTokenSource(); SetBusy(true);
            try
            {
                outputProtected = false;
                active = await Task.Run(delegate { return ReaderSafety.EnsureProtected(active, address, ReceptionFiles.DataPath, log, PreparationProgress); });
                reader = active; outputProtected = true;
                cancellation.Token.ThrowIfCancellationRequested();
                ScanResult result = await Task.Run(delegate { return active.Scan(scanMode, isReference, cancellation.Token, Progress); });
                // A late cancellation during restoration must still suppress displayed results.
                cancellation.Token.ThrowIfCancellationRequested();
                if (isReference || chosenMode == FormatProfiles.Combo)
                {
                    if (!ReceptionText.IsNumber(result.Mr) || !ReceptionText.IsNumber(result.Er)) throw new InvalidDataException("Ofullständigt läsresultat.");
                }
                outputProtected = false;
                active = await Task.Run(delegate { return ReaderSafety.EnsureProtected(active, address, ReceptionFiles.DataPath, log, PreparationProgress); });
                reader = active; outputProtected = true;
                PublishScanResult(result, active, chosenMode, chosenChip, isReference, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                ClearResults();
                if (reconnectPending) SetInstruction("ANSLUTER", "Kontrollerar läsaren igen", "Resultatet har rensats. Låt USB-kabeln sitta i medan läsaren kontrolleras.", false);
                else SetInstruction("AVBRUTET", "Läsningen har avbrutits", "Inga nummer lämnas ut. Klicka på Läs bricka när du vill börja igen.", false);
            }
            catch (Exception ex)
            {
                ClearResults(); SaveError(ex);
                if (!(ex is TimeoutException)) outputProtected = false;
                connection.Text = active.HasPendingRecovery ? "Läsaren behöver återställas" : "Läsningen blev inte klar";
                if (active.HasPendingRecovery)
                    SetInstruction("HJÄLP BEHÖVS", "Läsaren behöver återställas", "Låt USB-kabeln sitta i. Välj Hjälp och be IT hjälpa dig innan nästa bricka.", true);
                else if (ex is TimeoutException)
                    SetInstruction("FÖRSÖK IGEN", "Ingen bricka kunde läsas klart", "Håll en bricka i taget nära läsaren och följ stegen. Klicka på Läs bricka för ett nytt försök.", true);
                else
                    SetInstruction("FÖRSÖK IGEN", "Brickan kunde inte läsas klart", "Lyft bort brickan. Välj Hjälp och Försök ansluta igen för att förbereda läsaren på nytt.", true);
            }
            finally
            {
                if (cancellation != null) cancellation.Dispose(); cancellation = null;
                cancelling = false; reference = false; keyboardSink.Clear(); SetBusy(false);
                if (HasResults && IsReady && !reconnectPending && !isReference)
                {
                    WorkflowSettings.Cue(workflow.Sounds, 3);
                    string automatic = WorkflowSettings.CopyValue(workflow.Copy, mr.Text, er.Text, mode == FormatProfiles.Combo);
                    if (!String.IsNullOrEmpty(automatic)) Copy(automatic, "Automatiskt kopierat.");
                }
                if (HasResults) copyM.Focus(); else if (read.Enabled) read.Focus();
            }
        }
        private void PublishScanResult(ScanResult result, ReaderClient active, string chosenMode, string chosenChip, bool isReference, CancellationToken token)
        {
            // This is the last UI-thread gate, after all awaited restoration work.
            // No result or automatic clipboard value may outlive cancellation or USB invalidation.
            token.ThrowIfCancellationRequested();
            if (reconnectPending || !outputProtected || active == null || !Object.ReferenceEquals(reader, active) || active.HasPendingRecovery)
                throw new OperationCanceledException("Läsaranslutningen ändrades innan läsningen var klar.");
            connection.Text = "Läsaren är ansluten";
            if (isReference)
            {
                ReadyMessage(); notice.Text = "Kontrollen är godkänd. Den verifierade läsarprofilen används nu."; return;
            }
            if (chosenMode == FormatProfiles.Combo)
            {
                mr.Text = result.Mr; er.Text = result.Er;
                SetInstruction("KLART", "Läsningen är klar", "Kopiera numren och klicka på Läs nästa bricka.", false);
            }
            else if (chosenMode == FormatProfiles.Rco)
            {
                mr.Text = FormatProfiles.RcoShort(result, chosenChip);
                er.Text = FormatProfiles.RcoLong(result, chosenChip);
                SetInstruction("KLART", "Läsningen är klar", "Välj rätt längd för det aktuella RCO-systemet.", false);
            }
            else if (chosenMode == FormatProfiles.Axema)
            {
                mr.Text = FormatProfiles.Vaka14(result);
                SetInstruction("KLART", "Läsningen är klar", "Kopiera numret och klicka på Läs nästa bricka.", false);
            }
            else
            {
                mr.Text = FormatProfiles.Bewator13(result);
                SetInstruction("KLART", "Läsningen är klar", "Kopiera numret och klicka på Läs nästa bricka.", false);
            }
            notice.Text = "";
        }
        private void RequestCancel()
        {
            if (cancellation == null || cancelling) return;
            cancelling = true; cancellation.Cancel(); Progress(""); UpdateButtons();
        }
        private void Copy(string text, string message)
        {
            if (busy || !IsReady || !HasResults) return;
            try { Clipboard.SetText(text); lastCopied = text; notice.Text = message + " Klistra in med Ctrl+V."; }
            catch (Exception ex) { SaveError(ex); notice.Text = "Det gick inte att kopiera just nu. Prova knappen igen."; }
        }
        private void SaveError(Exception ex)
        {
            ReceptionFiles.RecordFailure(ex);
            try { ReceptionFiles.WriteAtomic(Path.Combine(ReceptionFiles.DataPath, "senaste-fel-reception.txt"), ReceptionFiles.Report()); } catch { }
        }
        private async Task SaveReport()
        {
            if (busy) return;
            using (SaveFileDialog d = new SaveFileDialog { Filter = "Textfil (*.txt)|*.txt", FileName = "Bricklasaren-felrapport-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt", Title = "Spara felrapport till IT" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!TryBeginFileOperation(false)) return;
                try
                {
                    string destination = d.FileName, report = ReceptionFiles.Report(); notice.Text = "Sparar felrapport …";
                    await Task.Run(delegate { File.WriteAllText(destination, report, ReceptionFiles.Utf8); }); notice.Text = "Felrapporten är sparad utan skannade nummer. Dela den med IT.";
                }
                catch (Exception ex) { SaveError(ex); MessageBox.Show(this, "Filen kunde inte sparas. Välj en annan mapp.", "Brickläsaren"); }
                finally { SetBusy(false); }
            }
        }
        private async Task Recover()
        {
            if (busy || reader == null || !reader.HasPendingRecovery) return;
            if (MessageBox.Show(this, "Ta bort alla brickor och låt USB-kabeln sitta i. Återställa läsaren nu?", "Återställ läsaren", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            ClearResults(); outputProtected = false; SetBusy(true); SetInstruction("ÅTERSTÄLLER", "Håll brickorna borta", "Låt läsaren vara inkopplad tills detta är klart.", false);
            try
            {
                await Task.Run(delegate { reader.Recover(); });
                reader = await Task.Run(delegate { return ReaderSafety.EnsureProtected(reader, address, ReceptionFiles.DataPath, log, PreparationProgress); });
                outputProtected = true; ReadyMessage();
            }
            catch (Exception ex) { SaveError(ex); SetInstruction("HJÄLP BEHÖVS", "Återställningen är inte klar", "Låt läsaren vara inkopplad. Spara en felrapport via Hjälp och kontakta IT.", true); }
            finally { SetBusy(false); }
        }
        private async Task ExportReceptionPackage()
        {
            if (busy || !IsReady) return;
            using (FolderBrowserDialog d = new FolderBrowserDialog { Description = "Välj var den färdiga receptionsmappen ska skapas. En ny undermapp skapas." })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!TryBeginFileOperation(true)) return;
                try
                {
                    string parent = d.SelectedPath, source = ReceptionFiles.PackagePath;
                    // Snapshot reader-derived text on the UI thread. The worker only touches files.
                    string profile = reader.Cal == null ? null : ReceptionFiles.ProfileText(reader, address);
                    notice.Text = "Skapar paketet …";
                    string destination = await Task.Run(delegate { return ExportPackageFiles(source, parent, profile, ReceptionFiles.CopyPackageFiles); });
                    MessageBox.Show(this, "Receptionspaketet är skapat:\r\n\r\n" + destination + "\r\n\r\nFlytta hela mappen. En eventuell verifierad profil gäller samma läsare. Inga sparade avläsningar eller felloggar följer med.", "Klart", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    notice.Text = profile == null ? "Paketet är skapat med standardformat, utan lokal läsarprofil." : "Paketet är skapat med läsarens verifierade profil.";
                }
                catch (Exception ex) { SaveError(ex); MessageBox.Show(this, "Paketet kunde inte skapas. Välj en tom, skrivbar mapp.\r\n\r\n" + ex.Message, "Brickläsaren"); }
                finally { SetBusy(false); }
            }
        }
        internal static string ExportPackageFiles(string source, string parent, string profile, Action<string, string> copy)
        {
            string token = Guid.NewGuid().ToString("N");
            string staging = Path.Combine(parent, ".bricklasaren-export-" + token);
            string destination = Path.Combine(parent, "Bricklasaren-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + token.Substring(0, 8));
            if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Arbetsmappen finns redan.");
            Directory.CreateDirectory(staging);
            try
            {
                string package = Path.Combine(staging, "paket");
                copy(source, package);
                if (profile != null) ReceptionFiles.WriteAtomic(Path.Combine(package, ReceptionFiles.ProfileName), profile);
                File.WriteAllText(Path.Combine(package, "Snabbstart.txt"), ReceptionText.QuickStart, ReceptionFiles.Utf8);
                Directory.Move(package, destination); // Never overwrite a user's existing folder.
                return destination;
            }
            finally
            {
                // Only our randomly named, newly created staging directory is eligible for cleanup.
                try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        private async Task ImportProfile()
        {
            if (reader == null || reader.HasPendingRecovery) return;
            using (OpenFileDialog d = new OpenFileDialog { Filter = "Läsarprofil (*.json)|*.json", Title = "Importera verifierad läsarprofil" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { ReceptionFiles.ImportProfile(reader, d.FileName); await ConnectReader(); }
                catch (Exception ex) { SaveError(ex); MessageBox.Show(this, ex.Message, "Profilen kunde inte importeras", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
        private void CreateDesktopShortcut()
        {
            object shell = null, shortcut = null;
            try
            {
                string launcher = Path.Combine(ReceptionFiles.PackagePath, ReceptionFiles.LauncherName);
                if (!File.Exists(launcher)) throw new FileNotFoundException("Startfilen saknas. Packa upp hela ZIP-filen.");
                string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Brickläsaren.lnk");
                if (File.Exists(link) && MessageBox.Show(this, "En genväg med samma namn finns redan. Uppdatera den?", "Skrivbordsgenväg", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Type shellType = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                Type t = shortcut.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { launcher });
                t.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "" });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { ReceptionFiles.PackagePath });
                t.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(ReceptionFiles.PackagePath, "Program", "Bricklasaren.ico") + ",0" });
                t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Läs brickans nummer för valt passersystem" });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, new object[0]);
                notice.Text = "Genvägen Brickläsaren finns nu på skrivbordet. Behåll programmappen på sin nuvarande plats.";
            }
            catch (Exception ex)
            {
                SaveError(ex);
                MessageBox.Show(this, "Genvägen kunde inte skapas. Det går fortfarande att starta via Starta-Bricklasaren.cmd.", "Brickläsaren", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                if (shortcut != null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        private async Task RestoreOriginalUsbMode()
        {
            if (busy || reader == null || reader.HasPendingRecovery) return;
            string description = "Läsaren återgår till USB-läget som sparades före den automatiska förberedelsen. Den kan då börja skriva kortnummer i aktiva Windows-fönster igen.\r\n\r\nBrickläsaren avslutas efter återställningen. Nästa gång du startar programmet aktiveras skyddet automatiskt igen.\r\n\r\nÅterställa och avsluta?";
            if (MessageBox.Show(this, description, "Återställ tidigare USB-läge (IT)", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ClearResults(); outputProtected = false; SetBusy(true);
            SetInstruction("IT-INSTÄLLNING", "Startar om USB-läsaren", "Låt USB-kabeln sitta kvar. Inga brickor ska ligga mot läsaren.", false);
            bool restored = false;
            try
            {
                ReaderClient active = reader;
                reader = await Task.Run(delegate { return ReaderSafety.RestoreOriginalMode(active, address, ReceptionFiles.DataPath, log, PreparationProgress); });
                restored = true;
            }
            catch (Exception ex)
            {
                SaveError(ex);
                reader = null;
                SetInstruction("IT HJÄLP BEHÖVS", "USB-läget kunde inte verifieras", "Låt USB-kabeln sitta i. Välj Hjälp och Försök ansluta igen för att återaktivera skyddet.", true);
            }
            finally { SetBusy(false); }
            if (restored) Close();
        }
        private async Task ShowHelp()
        {
            if (busy) return;
            using (ReceptionHelp d = new ReceptionHelp(address, reader, mode, rcoChip, outputProtected))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.Action == "Format")
                {
                    // FormatSelector commits before closing: failed saves never change active mode.
                    mode = d.Mode; rcoChip = d.Chip; profileChosen = true;
                    ApplyModeUi();
                    notice.Text = "Passersystemet är sparat och används även nästa gång programmet startas.";
                }
                else if (d.Action == "Workflow") { using (WorkflowDialog options = new WorkflowDialog(workflow, appearanceDirectory)) { if (options.ShowDialog(this) == DialogResult.OK) workflow = options.Value; } }
                else if (d.Action == "Updates") await CheckUpdates();
                else if (d.Action == "Diagnostics") { await ConnectReader(); MessageBox.Show(this, reader == null ? "1. Ingen kontakt. Kontrollera USB-kabeln.\r\n2. Välj Anslut läsare för drivrutinsguiden.\r\n3. Spara felrapporten om det kvarstår." : !IsReady ? "Anslutningen fungerar. Profil eller återställning behöver åtgärdas under IT-inställningar. Spara felrapporten om det kvarstår." : "Kontroller godkända: kontakt, läsaridentitet, nummerformat och skydd mot tangentbordsinmatning. Läs en bricka för att kontrollera den i passersystemet.", "Felsökning"); }
                else if (d.Action == "License") { if (MessageBox.Show(this, "Tobtech\r\n\r\nGratis för privat bruk. Kommersiellt bruk kräver en betald licens.\r\n\r\nÖppna licensvillkoren?", "Om Brickläsaren", MessageBoxButtons.YesNo) == DialogResult.Yes) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(ReceptionFiles.PackagePath, "LICENSE.txt")) { UseShellExecute = true }); }
                else if (d.Action == "Appearance") ApplyAppearance();
                else if (d.Action == "PrivateOff") await RestoreOriginalUsbMode();
                else if (d.Action == "Shortcut") CreateDesktopShortcut();
                else if (d.Action == "Connect") { address = d.Address; await ConnectReader(); }
                else if (d.Action == "Setup") await SetupReader();
                else if (d.Action == "Report") await SaveReport();
                else if (d.Action == "Recover") await Recover();
                else if (d.Action == "Export") await ExportReceptionPackage();
                else if (d.Action == "Import") await ImportProfile();
                else if (d.Action == "Reference")
                {
                    if (MessageBox.Show(this, "Använd endast den lokalt konfigurerade referensbrickan. Lägg inte på den förrän programmet ber om det.\r\n\r\nStarta kontrollen?", "Kontroll av läsaren", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK) await Scan(true);
                }
            }
        }
    }
    internal sealed class ReceptionHelp : Form
    {
        private readonly TextBox address = new TextBox { Width = 200 };
        public string Action { get; private set; }
        public string Mode { get; private set; }
        public string Chip { get; private set; }
        public string Address { get { return address.Text.Trim(); } }
        public ReceptionHelp(string currentAddress, ReaderClient reader, string currentMode, string currentChip, bool protectedOutput)
        {
            Mode = currentMode; Chip = currentChip;
            Text = "Hjälp · Brickläsaren"; Font = Ui.GetFont(10F); BackColor = Ui.Paper;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(610, 425);
            MinimumSize = new Size(610, 425); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
            MaximizeBox = false; MinimizeBox = false; AutoScroll = true;
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(24); Controls.Add(root);
            Ui.Add(root, Ui.Label("Behöver du hjälp?", 19F, true));
            Button format = Ui.Button("Inställningar – Välj passersystem", false);
            format.Click += delegate {
                using (FormatSelector options = new FormatSelector(Mode, Chip))
                {
                    if (options.ShowDialog(this) == DialogResult.OK)
                    { Mode = options.Mode; Chip = options.Chip; Action = "Format"; DialogResult = DialogResult.OK; Close(); }
                }
            };
            Ui.Add(root, format);
            Ui.Add(root, Ui.Label("Kontrollera USB-kabeln om läsaren inte svarar. Följ alltid anvisningarna i huvudfönstret.", 10.5F, false));
            FlowLayoutPanel simple = Ui.Row(); simple.Controls.Add(ActionButton("Försök ansluta igen", "Connect")); simple.Controls.Add(ActionButton("Spara felrapport", "Report")); Ui.Add(root, simple);
            Label privacy = Ui.Label("Skannade nummer sparas inte. En felrapport innehåller bara tekniska feluppgifter.", 9.5F, false); privacy.MaximumSize = new Size(540, 0); Ui.Add(root, privacy);
            Ui.Add(root, ActionButton("Ny dator – anslut läsare", "Setup"));
            Button more = Ui.Button("IT-inställningar", false); more.Font = Ui.GetFont(9.5F);
            FlowLayoutPanel moreActions = Ui.Row(); moreActions.Controls.Add(more); moreActions.Controls.Add(ActionButton("Om och licens", "License")); Ui.Add(root, moreActions);
            TableLayoutPanel advanced = Ui.Stack(); advanced.Visible = false; advanced.Margin = new Padding(0, 18, 0, 0); Ui.Add(root, advanced);
            Ui.Add(advanced, Ui.Label("Läsare och utseende", 11F, true));
            Button appearance = Ui.Button("Utseende – färg och logotyp", false);
            appearance.Click += delegate {
                using (AppearanceDialog options = new AppearanceDialog())
                {
                    if (options.ShowDialog(this) == DialogResult.OK) { Action = "Appearance"; DialogResult = DialogResult.OK; Close(); }
                }
            };
            Ui.Add(advanced, appearance);
            Ui.Add(advanced, ActionButton("Arbetsflöde och uppdateringar", "Workflow"));
            Ui.Add(advanced, ActionButton("Sök efter uppdatering", "Updates"));
            Ui.Add(advanced, ActionButton("Guidad felsökning", "Diagnostics"));
            Button referenceSettings = Ui.Button("Ställ in referensbricka", false);
            referenceSettings.Click += delegate { using (ReferenceDialog options = new ReferenceDialog()) options.ShowDialog(this); };
            Ui.Add(advanced, referenceSettings);
            address.Text = currentAddress;
            Ui.Add(advanced, Ui.Label("Läsarens lokala IP-adress", 10F, false)); Ui.Add(advanced, address);
            FlowLayoutPanel connectRow = Ui.Row(); connectRow.Controls.Add(ActionButton("Anslut med adressen", "Connect")); Ui.Add(advanced, connectRow);
            string details = reader == null ? "Ingen verifierad anslutning." : reader.Board + "\r\nFirmware: " + reader.Firmware + "\r\nID: " + reader.ReaderId + "\r\nNummerformat: " + (!reader.CanRead ? "Profilen behöver kontrolleras" : reader.Cal == null ? "Standardformat" : "Verifierad lokal profil") + " · Återställning kvar: " + (reader.HasPendingRecovery ? "Ja" : "Nej");
            Label info = Ui.Label(details, 9F, false); info.MaximumSize = new Size(540, 0); Ui.Add(advanced, info);
            bool connected = reader != null; bool pending = connected && reader.HasPendingRecovery;
            FlowLayoutPanel profiles = Ui.Row();
            Button export = ActionButton("Skapa receptionspaket", "Export"); export.Enabled = protectedOutput && connected && reader.CanRead && !pending;
            Button import = ActionButton("Importera läsarprofil", "Import"); import.Enabled = connected && !pending;
            profiles.Controls.Add(export); profiles.Controls.Add(import); Ui.Add(advanced, profiles);
            FlowLayoutPanel desktop = Ui.Row(); desktop.Controls.Add(ActionButton("Skapa skrivbordsgenväg", "Shortcut")); Ui.Add(advanced, desktop);
            Ui.Add(advanced, Ui.Label("USB-säkerhet – ingen tangentbordsinmatning utanför programmet", 11F, true));
            Ui.Add(advanced, Ui.Label("Skyddet aktiveras automatiskt när programmet ansluter. Läsaren skickar inga tangenttryckningar; avläsning i programmet startas med Läs bricka. Radiofältet är fortfarande på.", 9.5F, false));
            FlowLayoutPanel security = Ui.Row();
            Button privateOff = ActionButton("Återställ tidigare USB-läge och avsluta", "PrivateOff");
            privateOff.Enabled = connected && !pending && ReaderSafety.HasOriginalMode(reader, ReceptionFiles.DataPath);
            security.Controls.Add(privateOff); Ui.Add(advanced, security);
            FlowLayoutPanel service = Ui.Row();
            Button test = ActionButton("Kontrollera referensbricka", "Reference"); test.Enabled = protectedOutput && connected && !pending;
            Button restore = ActionButton("Återställ läsaren", "Recover"); restore.Enabled = pending;
            service.Controls.Add(test); service.Controls.Add(restore); Ui.Add(advanced, service);
            Ui.Add(advanced, Ui.Label("Version 3.6.0 · PowerShell · Programmet läser bricknummer.\r\nTobtech", 9F, false));
            more.Click += delegate
            {
                advanced.Visible = !advanced.Visible;
                int availableHeight = Math.Max(300, Screen.FromControl(this).WorkingArea.Height - 90);
                ClientSize = new Size(610, Math.Min(advanced.Visible ? 1020 : 425, availableHeight));
                more.Text = advanced.Visible ? "Dölj IT-inställningar" : "IT-inställningar";
            };
        }
        private Button ActionButton(string title, string action)
        {
            Button b = Ui.Button(title, false); b.Font = Ui.GetFont(9.5F, true);
            b.Click += delegate { Action = action; DialogResult = DialogResult.OK; Close(); }; return b;
        }
    }

    internal sealed class FormatSelector : Form
    {
        private readonly ComboBox profiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 440, AccessibleName = "Passersystem" };
        private readonly ComboBox chips = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 440, AccessibleName = "RFID-teknik för RCO" };
        private readonly Label details = Ui.Label("", 10F, false);
        private readonly Label chipLabel = Ui.Label("RFID-teknik för RCO", 10F, false);
        private readonly Button save = Ui.Button("Spara inställningar", true);
        public string Mode { get; private set; }
        public string Chip { get; private set; }
        public FormatSelector(string initialMode, string initialChip, bool firstRun = false)
        {
            Text = firstRun ? "Välkommen · Välj passersystem" : "Inställningar · Passersystem";
            Font = Ui.GetFont(10F); BackColor = Ui.Paper;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(535, 340); MinimumSize = new Size(535, 310);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F); MaximizeBox = false; MinimizeBox = false; AutoScroll = true;
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabIndex = 0 };
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(22); scroll.Controls.Add(root); Controls.Add(scroll);
            Ui.Add(root, Ui.Label("Välj passersystem", 19F, true));
            Label intro = Ui.Label(firstRun ? "Gör valet en gång för det här Windows-kontot. Nästa gång används det automatiskt." : "Valet sparas direkt och används även nästa gång du startar programmet.", 10F, false);
            intro.MaximumSize = new Size(470, 0); Ui.Add(root, intro);
            Ui.Add(root, Ui.Label("Passersystem", 10F, true));
            profiles.Dock = DockStyle.Top; profiles.TabIndex = 0; chips.Dock = DockStyle.Top; chips.TabIndex = 1;
            profiles.Items.AddRange(new object[] { "RCO – MIFARE + EM (mR + eR)", "RCO – kort/lång ID-kod", "Axema VAKA – 14 siffror", "Bewator 2010/Omnis – 13 siffror" });
            Ui.Add(root, profiles); Ui.Add(root, chipLabel);
            chips.Items.AddRange(new object[] { "MIFARE (13,56 MHz)", "EM (125 kHz)" });
            chips.SelectedIndex = initialChip == "E" ? 1 : 0; Ui.Add(root, chips);
            details.MaximumSize = new Size(450, 0); details.ForeColor = Ui.Muted; Ui.Add(root, details);
            FlowLayoutPanel row = Ui.Row(); Button cancel = Ui.Button(firstRun ? "Avsluta" : "Avbryt", false);
            save.Text = firstRun ? "Spara och börja" : "Spara inställningar";
            save.AccessibleName = save.Text; save.TabIndex = 0; cancel.TabIndex = 1;
            row.Controls.Add(save); row.Controls.Add(cancel); row.Dock = DockStyle.Bottom; row.Padding = new Padding(22, 10, 22, 14); row.TabIndex = 1; Controls.Add(row);
            root.SizeChanged += delegate { Ui.WrapLabels(root); };
            Mode = FormatProfiles.ValidMode(initialMode) ? initialMode : FormatProfiles.Combo;
            Chip = FormatProfiles.ValidChip(initialChip) ? initialChip : "M";
            profiles.SelectedIndex = firstRun ? -1 : Mode == FormatProfiles.Rco ? 1 : Mode == FormatProfiles.Axema ? 2 : Mode == FormatProfiles.Bewator ? 3 : 0;
            profiles.SelectedIndexChanged += delegate { UpdateChoice(); };
            save.Click += delegate
            {
                if (profiles.SelectedIndex < 0 || profiles.SelectedIndex > 3) return;
                string selectedMode = new string[] { FormatProfiles.Combo, FormatProfiles.Rco, FormatProfiles.Axema, FormatProfiles.Bewator }[profiles.SelectedIndex];
                string selectedChip = chips.SelectedIndex == 1 ? "E" : "M";
                try
                {
                    ReceptionFiles.SaveFormatPreference(selectedMode, selectedChip);
                    Mode = selectedMode; Chip = selectedChip;
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex)
                {
                    ReceptionFiles.RecordFailure(ex);
                    MessageBox.Show(this, "Inställningen kunde inte sparas. Valet har inte ändrats. Kontakta IT eller försök igen.", "Kunde inte spara", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = save; CancelButton = cancel;
            Shown += delegate { profiles.Focus(); };
            UpdateChoice();
        }
        private void UpdateChoice()
        {
            bool rco = profiles.SelectedIndex == 1;
            chipLabel.Visible = rco; chips.Visible = rco;
            save.Enabled = profiles.SelectedIndex >= 0 && profiles.SelectedIndex < 4;
            details.Text = profiles.SelectedIndex < 0 ? "Välj vilket passersystem du vill läsa brickor för. Du kan ändra valet senare under Hjälp → Inställningar."
                : profiles.SelectedIndex == 0 ? "Två avläsningar av samma bricka: MIFARE (mR) och EM (eR), båda som 9-siffriga RCO-ID."
                : rco ? "En skanning: visar både kort (9 siffror) och långt (10 siffror) RCO-ID för vald chipteknik."
                : profiles.SelectedIndex == 2 ? "En skanning: Axema VAKA-format från EM-chip, 14 siffror. Inte samma format som Axema Axbase."
                : "En skanning: Bewator 2010/Omnis-format från EM-chip, 13 siffror. Inte Entro 8 siffror.";
        }
    }
}
