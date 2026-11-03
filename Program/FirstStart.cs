namespace Tobtech.OmnikeyRco
{
    using System.Diagnostics;
    using System.Drawing;
    using System.Windows.Forms;
    internal static class FirstStart
    {
        internal const string DriverUrl = "https://www.hidglobal.com/drivers/34881";
        internal static void OpenDriverPage() { Process.Start(new ProcessStartInfo(DriverUrl) { UseShellExecute = true }); }
    }
    internal sealed class FirstStartDialog : Form
    {
        internal bool OpenDriver { get; private set; }
        internal FirstStartDialog(string result)
        {
            Text = "Kom igång · Brickläsaren"; Font = Ui.GetFont(10F); BackColor = Ui.Paper; ForeColor = Ui.Ink;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(630, 480);
            MinimumSize = new Size(560, 400); MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F); AutoScroll = true;
            TableLayoutPanel root = Ui.Stack(); root.Padding = new Padding(24); Controls.Add(root);
            Ui.Add(root, Ui.Label("Anslut din läsare", 21F, true));
            AddText(root, "1. Anslut OMNIKEY 5427 CK till USB. Håll alla brickor borta tills programmet ber om en bricka.");
            AddText(root, "2. Välj Försök ansluta. Om det inte fungerar: välj Hämta USB-drivrutin och följ anvisningarna på sidan. Be IT om hjälp om installationen kräver administratör.");
            AddText(root, "3. Välj Försök ansluta igen efter installationen. När läsaren är klar kan du börja läsa brickor.");
            AddText(root, "Har anslutningen redan fungerat: prova en annan USB-port och kontrollera att bara en läsare används. Ditt val av passersystem är sparat.");
            if (!string.IsNullOrEmpty(result)) AddText(root, result);
            FlowLayoutPanel buttons = Ui.Row();
            Button retry = Ui.Button("Försök ansluta", true), driver = Ui.Button("Hämta USB-drivrutin", false), later = Ui.Button("Senare", false);
            retry.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            driver.Click += delegate { OpenDriver = true; DialogResult = DialogResult.OK; Close(); };
            later.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(retry); buttons.Controls.Add(driver); buttons.Controls.Add(later); Ui.Add(root, buttons);
            AcceptButton = retry; CancelButton = later;
        }
        private static void AddText(TableLayoutPanel root, string text) { Label label = Ui.Label(text, 10F, false); label.MaximumSize = new Size(540, 0); Ui.Add(root, label); }
    }
}
