using System.Diagnostics;
using Dedsec.Core;
using System.Text.Json;

namespace Dedsec.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[0], out var parentPid)) return 2;
        var target = Path.GetFullPath(args[1]); var stage = Path.GetFullPath(args[2]); var ack = Path.GetFullPath(args[3]);
        var updateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dedsec", "Monitor", "Updates") + Path.DirectorySeparatorChar;
        if (!stage.StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) || !ack.StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase)) return 3;
        var log = Path.Combine(updateRoot, "updater.log"); Directory.CreateDirectory(updateRoot);
        var executable = UpdatePackage.ResolveFile(target, "Dedsec.Monitor.exe");
        try
        {
            try { using var parent = Process.GetProcessById(parentPid); if (!parent.WaitForExit(30000)) throw new IOException("The previous process did not exit."); }
            catch (ArgumentException) { }
            var backup = Path.Combine(Path.GetDirectoryName(stage)!, "backup");
            UpdatePackage.Apply(stage, target, backup, () =>
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = target };
                start.ArgumentList.Add("--update-ack"); start.ArgumentList.Add(ack);
                using var child = Process.Start(start) ?? throw new IOException("The updated program could not start.");
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(ack) && deadline.Elapsed < TimeSpan.FromSeconds(30) && !child.HasExited) Thread.Sleep(200);
                if (!File.Exists(ack))
                {
                    if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); }
                    throw new IOException("The updated program did not confirm startup; rolling back.");
                }
            });
            File.AppendAllText(log, $"{DateTimeOffset.Now:O} Update completed.\n");
            return 0;
        }
        catch (Exception ex)
        {
            File.AppendAllText(log, $"{DateTimeOffset.Now:O} {ex}\n");
            try
            {
                var pendingFile = Path.Combine(updateRoot, "pending.json");
                if (File.Exists(pendingFile))
                {
                    using var pending = JsonDocument.Parse(File.ReadAllText(pendingFile));
                    if (pending.RootElement.GetProperty("Stage").GetString() is { } pendingStage &&
                        Path.GetFullPath(pendingStage).Equals(stage, StringComparison.OrdinalIgnoreCase) &&
                        pending.RootElement.GetProperty("Version").GetString() is { } version && Version.TryParse(version, out _))
                    {
                        File.WriteAllText(Path.Combine(updateRoot, "failed-version.txt"), version);
                        File.Delete(pendingFile);
                    }
                }
            }
            catch (Exception markerError) { File.AppendAllText(log, markerError.Message + "\n"); }
            try
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = target };
                start.ArgumentList.Add("--skip-update"); Process.Start(start);
            }
            catch (Exception restart) { File.AppendAllText(log, restart.Message + "\n"); }
            return 1;
        }
    }
}
