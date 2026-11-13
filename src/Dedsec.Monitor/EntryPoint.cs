using System.Diagnostics;

namespace Dedsec.Monitor;

// Wait before App creates its single-instance mutex when elevating an existing session.
internal static class StartupWait
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void WaitForPrevious()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--wait-for");
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pid)) return;
        try { using var previous = Process.GetProcessById(pid); previous.WaitForExit(15000); }
        catch (ArgumentException) { }
    }
}
