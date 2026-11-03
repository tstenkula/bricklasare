namespace Tobtech.OmnikeyRco
{
    using System;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Web.Script.Serialization;

    public sealed class AppearanceSettings
    {
        // Default navy; one definition for startup, preset and reset.
        public const string DefaultAccentHex = "#003B55";
        public int Schema { get; set; }
        public string AccentHex { get; set; }
        public string Organization { get; set; }
        public string Subtitle { get; set; }
        public string LogoFile { get; set; }
        public static AppearanceSettings Default()
        {
            return new AppearanceSettings { Schema = 1, AccentHex = DefaultAccentHex, Organization = "", Subtitle = "", LogoFile = "" };
        }
    }
    internal sealed class ThemePalette
    {
        internal readonly Color Accent, Light, TextAccent, ButtonText, Hover;
        internal ThemePalette(string hex)
        {
            Accent = Parse(hex);
            Light = Mix(Accent, Color.White, 0.94);
            TextAccent = Accent;
            while (Contrast(TextAccent, Light) < 4.5 || Contrast(TextAccent, Color.FromArgb(245, 248, 249)) < 4.5)
                TextAccent = Mix(TextAccent, Color.Black, 0.12);
            ButtonText = Contrast(Accent, Color.White) >= 4.5 ? Color.White : Color.Black;
            // Hover keeps the same readable foreground as the normal state.
            Hover = Mix(Accent, ButtonText == Color.White ? Color.Black : Color.White, 0.12);
        }
        internal static Color Parse(string hex)
        {
            if (!Regex.IsMatch(hex ?? "", @"\A#[0-9A-Fa-f]{6}\z"))
                throw new InvalidDataException("Ange en färg som #003B55, med sex hexadecimala tecken.");
            return Color.FromArgb(255, Color.FromArgb(Int32.Parse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }
        internal static Color Mix(Color a, Color b, double amount)
        {
            return Color.FromArgb((int)Math.Round(a.R * (1 - amount) + b.R * amount),
                (int)Math.Round(a.G * (1 - amount) + b.G * amount), (int)Math.Round(a.B * (1 - amount) + b.B * amount));
        }
        private static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        private static double Luminance(Color c) { return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B); }
        internal static double Contrast(Color a, Color b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
    }
    public static class AppearanceFiles
    {
        public const string FileName = "utseende.json";
        private const int MaxBytes = 2 * 1024 * 1024;
        private static bool OwnLogo(string name) { return Regex.IsMatch(name ?? "", @"\Alogo-[a-f0-9]{32}\.png\z"); }
        public static void Validate(AppearanceSettings settings)
        {
            if (settings == null || settings.Schema != 1) throw new InvalidDataException("Ogiltiga utseendeinställningar.");
            ThemePalette.Parse(settings.AccentHex);
            ValidateText(settings.Organization, 80); ValidateText(settings.Subtitle, 120);
            if (!String.IsNullOrEmpty(settings.LogoFile) && !OwnLogo(settings.LogoFile)) throw new InvalidDataException("Ogiltig logotypfil.");
        }
        private static void ValidateText(string text, int limit)
        {
            if (text == null || text.Length > limit || Regex.IsMatch(text, @"[\p{Cc}\p{Cf}]"))
                throw new InvalidDataException("Namn och underrubrik ska vara korta texter på en rad.");
        }
        public static AppearanceSettings Load(string directory)
        {
            try
            {
                string path = Path.Combine(directory, FileName);
                FileInfo file = new FileInfo(path);
                if (!file.Exists || file.Length > 16384) return AppearanceSettings.Default();
                AppearanceSettings settings = new JavaScriptSerializer().Deserialize<AppearanceSettings>(File.ReadAllText(path, Encoding.UTF8));
                Validate(settings); return settings;
            }
            catch (InvalidDataException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return AppearanceSettings.Default();
        }
        public static Bitmap ReadLogo(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length < 24 || file.Length > MaxBytes)
                throw new InvalidDataException("Välj en PNG-bild på högst 2 MB och 2048 × 2048 bildpunkter.");
            byte[] data;
            // Bound the read as well as metadata: a file can change after FileInfo.
            using (FileStream stream = File.OpenRead(path))
            {
                if (stream.Length < 24 || stream.Length > MaxBytes) throw new InvalidDataException("Logotypen är för stor eller ogiltig.");
                data = new byte[(int)stream.Length];
                int offset = 0, read;
                while (offset < data.Length && (read = stream.Read(data, offset, data.Length - offset)) > 0) offset += read;
                if (offset != data.Length) throw new InvalidDataException("Logotypen kunde inte läsas helt.");
            }
            byte[] signature = {137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82};
            for (int i = 0; i < signature.Length; i++)
                if (data[i] != signature[i]) throw new InvalidDataException("Välj en giltig PNG-bild.");
            long width = BigEndian(data, 16), height = BigEndian(data, 20);
            if (width < 1 || height < 1 || width > 2048 || height > 2048)
                throw new InvalidDataException("Logotypen får vara högst 2048 × 2048 bildpunkter.");
            try
            {
                using (MemoryStream stream = new MemoryStream(data, false))
                using (Image image = Image.FromStream(stream, true, true))
                {
                    if (image.Width != width || image.Height != height) throw new InvalidDataException("Ogiltiga bildmått.");
                    return new Bitmap(image); // Detached image; no file or stream stays open.
                }
            }
            catch (ArgumentException) { throw new InvalidDataException("PNG-bilden kunde inte öppnas."); }
            catch (System.Runtime.InteropServices.ExternalException) { throw new InvalidDataException("PNG-bilden kunde inte öppnas."); }
            catch (OutOfMemoryException) { throw new InvalidDataException("PNG-bilden kunde inte öppnas."); }
        }
        private static long BigEndian(byte[] data, int i)
        {
            return ((long)data[i] << 24) | ((long)data[i + 1] << 16) | ((long)data[i + 2] << 8) | data[i + 3];
        }
        public static Bitmap LoadLogo(string directory, AppearanceSettings settings)
        {
            if (settings == null || !OwnLogo(settings.LogoFile)) return null;
            try { return ReadLogo(Path.Combine(directory, "branding", settings.LogoFile)); }
            catch (InvalidDataException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public static void Save(string directory, AppearanceSettings settings, Image logo)
        {
            Validate(settings);
            AppearanceSettings previous = Load(directory);
            string created = null;
            string originalLogo = settings.LogoFile;
            try
            {
                if (logo != null)
                {
                    if (logo.Width > 2048 || logo.Height > 2048) throw new InvalidDataException("Logotypen är för stor.");
                    string folder = Path.Combine(directory, "branding"); Directory.CreateDirectory(folder);
                    settings.LogoFile = "logo-" + Guid.NewGuid().ToString("N") + ".png";
                    created = Path.Combine(folder, settings.LogoFile);
                    logo.Save(created, ImageFormat.Png);
                    if (new FileInfo(created).Length > MaxBytes) throw new InvalidDataException("Den sparade logotypen överstiger 2 MB.");
                }
                else settings.LogoFile = "";
                ReceptionFiles.WriteAtomic(Path.Combine(directory, FileName), new JavaScriptSerializer().Serialize(settings));
            }
            catch
            {
                settings.LogoFile = originalLogo;
                DeleteLogo(created); throw;
            }
            // Only remove our former image after the new configuration is committed.
            if (OwnLogo(previous.LogoFile) && previous.LogoFile != settings.LogoFile)
                DeleteLogo(Path.Combine(directory, "branding", previous.LogoFile));
        }
        private static void DeleteLogo(string path)
        {
            if (path == null) return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
