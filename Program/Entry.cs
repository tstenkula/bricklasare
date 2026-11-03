// Script entry point, hosted by Windows PowerShell. No application EXE is built.
[assembly: System.Reflection.AssemblyTitle("Brickläsaren")]
[assembly: System.Reflection.AssemblyDescription("Läser RCO, Axema VAKA och Bewator med OMNIKEY 5427 CK")]
[assembly: System.Reflection.AssemblyCompany("Tobtech")]
[assembly: System.Reflection.AssemblyProduct("Brickläsaren")]
[assembly: System.Reflection.AssemblyVersion("3.5.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.5.2.0")]
namespace Tobtech.OmnikeyRco
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;

    public static class Entry
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        // The .ps1 passes the package directory explicitly. Under PowerShell,
        // AppDomain.CurrentDomain.BaseDirectory is NOT the package directory.
        public static int Run(string packagePath, bool selfTest)
        {
            try
            {
                ReceptionFiles.PackagePath = Path.GetFullPath(packagePath);
                int core = SelfTests.Run();
                int ui = ReceptionTests.Run();
                int reliability = selfTest ? ReliabilityTests.Run() : 0;
                string report = "BRICKLÄSAREN RECEPTION 3.5.2 – POWERSHELL\r\n" +
                    core + " tester av läsarmotorn godkända.\r\n" +
                    ui + " tester av receptionslogiken godkända.\r\n" +
                    reliability + " tester av inställningar och resurshantering godkända.\r\n" +
                    "Detta är inte ett prov mot fysisk läsare eller ett visuellt Windows-test.\r\n";
                ReceptionFiles.WriteAtomic(Path.Combine(ReceptionFiles.DataPath, "sjalvtest-reception.txt"), report);
                if (selfTest) return 0;
                try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                ReceptionProgram.Run(packagePath);
                return 0;
            }
            catch (Exception ex)
            {
                if (selfTest) Console.Error.WriteLine(ex.ToString());
                string file = Path.Combine(ReceptionFiles.DataPath, "startfel-reception.txt");
                try { ReceptionFiles.WriteAtomic(file, DateTime.Now.ToString("s") + "\r\n" + ReceptionFiles.SafeError(ex)); } catch { }
                if (!selfTest)
                    MessageBox.Show("Programmet kunde inte fortsätta. Kontakta IT.\r\n\r\n" +
                        "Om en läsning pågick: låt USB-läsaren vara inkopplad.\r\n" +
                        "Fellogg: " + file,
                        "Brickläsaren", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
