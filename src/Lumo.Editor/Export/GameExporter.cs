using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lumo.Editor.Export;

/// <summary>Target platform for a standalone game export.</summary>
public sealed record ExportTarget(string Id, string Label, string Rid, string ExeName)
{
    public static readonly ExportTarget WindowsX64 = new("win-x64", "Windows (x64)", "win-x64", "Lumo.Runtime.exe");
    public static readonly ExportTarget LinuxX64 = new("linux-x64", "Linux (x64)", "linux-x64", "Lumo.Runtime");

    public static readonly ExportTarget[] DesktopTargets = [WindowsX64, LinuxX64];
}

/// <summary>Publishes Lumo.Runtime self-contained for a target RID and copies the
/// project content next to the executable so the build runs on its own.</summary>
public static class GameExporter
{
    public sealed record ExportResult(bool Ok, string Message, string? OutputDir);

    public static string OutputDir(string projectPath, string projectName, ExportTarget target, string? outputBase = null)
    {
        string safe = new string(projectName.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0) safe = "Game";
        string baseDir = outputBase ?? projectPath;
        return Path.Combine(baseDir, "Builds", safe + "-" + target.Id);
    }

    public static async Task<ExportResult> ExportAsync(ExportTarget target, string projectPath, string projectName, Action<string> log)
    {
        string outDir = OutputDir(projectPath, projectName, target);
        string? runtimeProj = FindRuntimeProject();

        if (runtimeProj != null)
        {
            string? dotnet = FindDotnet();
            if (dotnet == null)
                return new(false, "Export failed: dotnet SDK not found.", null);

            log($"Export [{target.Rid}]: publishing Lumo.Runtime -> {outDir}");
            try
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);

                var psi = new ProcessStartInfo
                {
                    FileName = dotnet,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("publish");
                psi.ArgumentList.Add(runtimeProj);
                psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("Release");
                psi.ArgumentList.Add("-r"); psi.ArgumentList.Add(target.Rid);
                psi.ArgumentList.Add("--self-contained"); psi.ArgumentList.Add("true");
                psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outDir);
                psi.ArgumentList.Add("--nologo");

                using var proc = Process.Start(psi);
                if (proc == null)
                    return new(false, "Export failed: could not start dotnet.", null);

                int printed = 0;
                void HandleLine(string? line)
                {
                    if (line == null) return;
                    int n = Interlocked.Increment(ref printed);
                    bool important = line.Contains("error", StringComparison.OrdinalIgnoreCase)
                                  || line.Contains("succeeded", StringComparison.OrdinalIgnoreCase)
                                  || line.Contains("warning", StringComparison.OrdinalIgnoreCase);
                    if (n > 40 && !important) return;
                    log($"[pub] {line}");
                }

                proc.OutputDataReceived += (_, e) => HandleLine(e.Data);
                proc.ErrorDataReceived += (_, e) => HandleLine(e.Data);
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0)
                    return new(false, $"Export failed (exit {proc.ExitCode}).", null);
            }
            catch (Exception ex)
            {
                return new(false, $"Export failed: {ex.Message}", null);
            }

            CopyProjectContent(projectPath, outDir);
            return Finish(target, outDir, log);
        }

        // No source checkout: publish from an already published Lumo.Runtime output
        // (editor publish folder / standalone game folder) so installed builds can
        // export without a dev checkout.
        string? sourceRoot = FindPublishedRuntimeRoot();
        if (sourceRoot == null)
            return new(false, "Export failed: Lumo.Runtime project not found and no published runtime output found (source checkout or installed editor required).", null);

        if (!RidIsAvailable(sourceRoot, target.Rid))
            return new(false, $"Export failed: published runtime output in '{sourceRoot}' has no native libraries for '{target.Rid}'. Use a source checkout or an editor/game install for that platform.", null);

        if (!Directory.Exists(projectPath) || !IsWritableDirectory(projectPath))
            outDir = OutputDir(projectPath, projectName, target, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumoEngine"));

        log($"Export [{target.Rid}]: copying published Lumo.Runtime from {sourceRoot} -> {outDir}");
        try
        {
            CopyRuntimeOutput(sourceRoot, outDir);
        }
        catch (Exception ex)
        {
            return new(false, $"Export failed: {ex.Message}", null);
        }

        CopyProjectContent(projectPath, outDir);
        return Finish(target, outDir, log);
    }

    private static ExportResult Finish(ExportTarget target, string outDir, Action<string> log)
    {
        string exe = Path.Combine(outDir, target.ExeName);
        if (!File.Exists(exe))
            return new(false, $"Publish finished but exe not found: {exe}", null);

        if (target.Rid.StartsWith("linux", StringComparison.Ordinal))
            log("Linux: run 'chmod +x Lumo.Runtime' on the target machine if needed.");
        string msg = $"Export OK: {exe}";
        log(msg);
        return new(true, msg, outDir);
    }

    /// <summary>Locates a published Lumo.Runtime output directory (editor publish
    /// folder or standalone game folder) so exports can run without a source checkout.</summary>
    public static string? FindPublishedRuntimeRoot(string? startDir = null)
    {
        string? dir = startDir ?? AppContext.BaseDirectory;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            string root = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(root, "Lumo.Runtime.dll")) &&
                File.Exists(Path.Combine(root, "Lumo.Runtime.deps.json")) &&
                File.Exists(Path.Combine(root, "Lumo.Runtime.runtimeconfig.json")))
                return root;
            dir = Path.GetDirectoryName(dir ?? "");
        }
        return null;
    }

    private static bool RidIsAvailable(string sourceRoot, string rid)
    {
        string ridDir = Path.Combine(sourceRoot, "runtimes", rid);
        if (Directory.Exists(ridDir)) return true;
        return Directory.Exists(Path.Combine(ridDir, "native"));
    }

    private static bool IsWritableDirectory(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".lumo_write_probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyRuntimeOutput(string sourceRoot, string outDir)
    {
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        string root = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file);
            if (rel.StartsWith("Lumo.Editor.", StringComparison.OrdinalIgnoreCase)) continue;
            if (rel.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            string dst = Path.Combine(outDir, rel);
            string? dstDir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
            File.Copy(file, dst, true);
        }
    }

    private static void CopyProjectContent(string projectPath, string outDir)
    {
        foreach (string dir in new[] { "Scenes", "Graphs", "Scripts", "Assets", "Plugins" })
        {
            string src = Path.Combine(projectPath, dir);
            if (Directory.Exists(src))
                CopyDirectory(src, Path.Combine(outDir, dir));
        }
        string projectFile = Path.Combine(projectPath, "Project.json");
        if (File.Exists(projectFile))
            File.Copy(projectFile, Path.Combine(outDir, "Project.json"), true);
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (string sub in Directory.GetDirectories(src))
            CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));
    }

    public static string? FindRuntimeProject()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "src", "Lumo.Runtime", "Lumo.Runtime.csproj");
            if (File.Exists(candidate)) return candidate;
            string flat = Path.Combine(dir, "Lumo.Runtime.csproj");
            if (File.Exists(flat)) return flat;
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return null;
    }

    public static string? FindDotnet()
    {
        var candidates = new List<string>();
        string env = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "";
        if (env.Length > 0) candidates.Add(env);
        string self = Environment.ProcessPath ?? "";
        if (self.Length > 0 && Path.GetFileName(self).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            candidates.Add(self);
        candidates.Add(@"C:\Program Files\dotnet\dotnet.exe");
        candidates.Add(@"C:\Program Files (x86)\dotnet\dotnet.exe");
        candidates.Add("/usr/share/dotnet/dotnet");
        candidates.Add("/usr/local/share/dotnet/dotnet");
        foreach (string c in candidates)
            if (File.Exists(c)) return c;

        foreach (string pathDir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (pathDir.Length == 0) continue;
            string c = Path.Combine(pathDir, "dotnet.exe");
            if (File.Exists(c)) return c;
            c = Path.Combine(pathDir, "dotnet");
            if (File.Exists(c)) return c;
        }
        return null;
    }
}
