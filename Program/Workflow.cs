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
            Text = "Arbetsflöde och uppdateringar"; ClientSize = new Size(600, 530); AutoScroll = true;
            StartPosition = FormStartPosition.CenterParent; Font = Ui.GetFont(10); BackColor = Ui.Paper;
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(24); Controls.Add(root);
            Ui.Add(root, Ui.Label("Arbetsflöde", 21, true));
            Ui.Add(root, Ui.Label("Kopiera automatiskt efter en helt avslutad läsning", 11, false));
            ComboBox copy = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 500 };
            copy.Items.AddRange(new object[] { "Av", "Första numret", "Andra numret (om det finns)", "Båda med rubriker (MIFARE + EM)" }); copy.SelectedIndex = value.Copy; Ui.Add(root, copy);
            CheckBox sounds = new CheckBox { Text = "Ljud för lägg på, lyft bort och klart", Checked = value.Sounds, AutoSize = true }; Ui.Add(root, sounds);
            Ui.Add(root, Ui.Label("Ctrl+R: Läs • Esc: Avbryt • Ctrl+1 / 2 / 3: Kopiera", 10, false));
            Ui.Add(root, Ui.Label("Uppdateringar", 17, true));
            ComboBox channel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 500 };
            channel.Items.AddRange(new object[] { "Stabil", "Förhandsversioner" }); channel.SelectedIndex = value.Channel == "preview" ? 1 : 0; Ui.Add(root, channel);
            CheckBox auto = new CheckBox { Text = "Sök efter nya versioner när appen startar", Checked = value.AutoCheck, AutoSize = true }; Ui.Add(root, auto);
            Label info = Ui.Label("Du väljer själv när en uppdatering ska installeras.", 10, false); info.MaximumSize = new Size(530, 0); Ui.Add(root, info);
            Button save = Ui.Button("Spara", true); save.Click += delegate {
                try {
                    WorkflowSettings next = new WorkflowSettings { Copy = copy.SelectedIndex, Sounds = sounds.Checked, Channel = channel.SelectedIndex == 1 ? "preview" : "stable", AutoCheck = auto.Checked };
                    ReceptionFiles.WriteAtomic(Path.Combine(directory, "arbetsflode.json"), new JavaScriptSerializer().Serialize(next)); Value = next; DialogResult = DialogResult.OK; Close();
                } catch { MessageBox.Show(this, "Inställningarna kunde inte sparas."); }
            }; Ui.Add(root, save);
        }
    }
}
