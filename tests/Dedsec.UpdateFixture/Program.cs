// This bounded, windowless fixture can acknowledge startup or withhold it to force rollback.
// It exercises the real updater without starting a game or publishing a release.
var folder = AppContext.BaseDirectory;
File.AppendAllText(Path.Combine(folder, "fixture-launches.log"), File.ReadAllText(Path.Combine(folder, "payload.txt")) + "\n");
var ackIndex = Array.IndexOf(args, "--update-ack");
if (File.ReadAllText(Path.Combine(folder, "payload.txt")) == "NEW-ACK" && ackIndex >= 0 && ackIndex + 1 < args.Length)
    File.WriteAllText(args[ackIndex + 1], "fixture-ready");
