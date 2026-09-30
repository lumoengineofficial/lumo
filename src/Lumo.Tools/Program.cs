using Lumo.Tools;

string basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string logoPath = Path.Combine(basePath, "lumologo.png");
string outputDir = Path.Combine(basePath, "src", "Lumo.Editor", "Assets");

if (!File.Exists(logoPath))
{
    Console.WriteLine($"Logo not found at: {logoPath}");
    return;
}

Directory.CreateDirectory(outputDir);

// 1. Remove background -> transparent PNG
string transparentPath = Path.Combine(outputDir, "lumo_logo_transparent.png");
LogoProcessor.RemoveBackground(logoPath, transparentPath);

// 2. Window icon: logo as-is on a rounded dark tile (visible on any surface)
string iconPath = Path.Combine(outputDir, "lumo_icon.png");
LogoProcessor.CreateIcon(logoPath, iconPath, 128);

// 3. Multi-size .ico for the executable file icon
string icoPath = Path.Combine(outputDir, "lumo_icon.ico");
LogoProcessor.CreateIco(logoPath, icoPath);

// 4. Create splash (512x512)
string splashPath = Path.Combine(outputDir, "lumo_splash.png");
LogoProcessor.Resize(transparentPath, splashPath, 512);

Console.WriteLine("\nDone! All assets generated.");
