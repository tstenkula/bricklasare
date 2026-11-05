namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Drawing;
    using System.IO;
    using System.Media;
    using System.Web.Script.Serialization;
    using System.Windows.Forms;
    internal sealed class WorkflowSettings
    {
        public int Copy { get; set; }
        public bool Sounds { get; set; }
        public string Channel { get; set; }
        public bool AutoCheck { get; set; }
        public WorkflowSettings() { Channel = "stable"; AutoCheck = true; }
        internal static WorkflowSettings Load(string directory)
        {
            try {
                string path = Path.Combine(directory, "arbetsflode.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 4096) return new WorkflowSettings();
                WorkflowSettings value = new JavaScriptSerializer().Deserialize<WorkflowSettings>(File.ReadAllText(path));
                if (value == null || value.Copy < 0 || value.Copy > 3 || (value.Channel != "stable" && value.Channel != "preview")) return new WorkflowSettings();
                return value;
            } catch { return new WorkflowSettings(); }
        }
        internal static string CopyValue(int choice, string first, string second, bool combo)
        {
            if (choice == 1) return first;
            if (choice == 2 && second != "—") return second;
            if (choice == 3 && combo) return ReceptionText.Both(first, second);
            return "";
        }
        internal static void Cue(bool enabled, int phase)
        {
            if (!enabled || phase == 0) return;
            // System sounds respect the user's Windows sound settings. No unbounded tasks/handles.
            try { if (phase == 1) SystemSounds.Question.Play(); else if (phase == 2) SystemSounds.Exclamation.Play(); else SystemSounds.Asterisk.Play(); } catch { }
        }
    }
    internal sealed class WorkflowDialog : Form
    {
        internal WorkflowSettings Value;
        internal WorkflowDialog(WorkflowSettings value, string directory)
        {
            Text = "Arbetsflöde och uppdateringar"; ClientSize = new Size(600, 460); MinimumSize = new Size(520, 360);
            StartPosition = FormStartPosition.CenterParent; Font = Ui.GetFont(10); BackColor = Ui.Paper;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F); MinimizeBox = false;
            SuspendLayout();
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabIndex = 0 };
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(24, 20, 24, 10); scroll.Controls.Add(root); Controls.Add(scroll);
            Ui.Add(root, Ui.Label("Arbetsflöde", 21, true));
            Ui.Add(root, Ui.Label("Kopiera automatiskt efter en helt avslutad läsning", 11, false));
            ComboBox copy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "Kopiera automatiskt", TabIndex = 0, Margin = new Padding(0, 0, 0, 8) };
            copy.Items.AddRange(new object[] { "Av", "Första numret", "Andra numret (om det finns)", "Båda med rubriker (MIFARE + EM)" }); copy.SelectedIndex = value.Copy; Ui.Add(root, copy);
            Label shortcuts = Ui.Label("Ctrl+R: Läs · Esc: Avbryt · Ctrl+1 / 2 / 3: Kopiera", 9.5F, false); shortcuts.ForeColor = Ui.Muted; shortcuts.Margin = new Padding(0, 0, 0, 18); Ui.Add(root, shortcuts);
            Ui.Add(root, Ui.Label("Uppdateringar", 17, true));
            Ui.Add(root, Ui.Label("Versioner", 10, true));
            ComboBox channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = "Versioner", TabIndex = 2, Margin = new Padding(0, 0, 0, 8) };
            channel.Items.AddRange(new object[] { "Stabil", "Förhandsversioner" }); channel.SelectedIndex = value.Channel == "preview" ? 1 : 0; Ui.Add(root, channel);
            CheckBox auto = new CheckBox { Text = "Sök efter nya versioner när appen startar", Checked = value.AutoCheck, AutoSize = true, TabIndex = 3, Margin = new Padding(0, 0, 0, 6) }; Ui.Add(root, auto);
            Label info = Ui.Label("Du väljer själv när en uppdatering ska installeras.", 10, false); info.MaximumSize = new Size(530, 0); Ui.Add(root, info);
            FlowLayoutPanel actions = Ui.Row(); actions.Dock = DockStyle.Bottom; actions.Padding = new Padding(24, 10, 24, 14); actions.TabIndex = 1;
            Button save = Ui.Button("Spara", true), cancel = Ui.Button("Avbryt", false); save.TabIndex = 0; cancel.TabIndex = 1;
            actions.Controls.Add(save); actions.Controls.Add(cancel); Controls.Add(actions);
            root.SizeChanged += delegate {
                Ui.WrapLabels(root); int width = Math.Max(120, root.ClientSize.Width - root.Padding.Horizontal - 8);
                auto.MaximumSize = new Size(width, 0);
            };
            save.Click += delegate {
                try {
                    WorkflowSettings next = new WorkflowSettings { Copy = copy.SelectedIndex, Sounds = value.Sounds, Channel = channel.SelectedIndex == 1 ? "preview" : "stable", AutoCheck = auto.Checked };
                    ReceptionFiles.WriteAtomic(Path.Combine(directory, "arbetsflode.json"), new JavaScriptSerializer().Serialize(next)); Value = next; DialogResult = DialogResult.OK; Close();
                } catch { MessageBox.Show(this, "Inställningarna kunde inte sparas."); }
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            AcceptButton = save; CancelButton = cancel;
            Shown += delegate { copy.Focus(); }; ResumeLayout(true);
        }
    }
}
