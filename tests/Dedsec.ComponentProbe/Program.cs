using Dedsec.Monitor;

// Explicit online QA probe. Downloads and verifies; never starts an installer.
if (args.Length != 1) return 2;
using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var service = new ComponentSetupService(client, Path.GetFullPath(args[0]));
var file = await service.DownloadRtssAsync(new Progress<string>(Console.WriteLine), CancellationToken.None);
Console.WriteLine(file);
Console.WriteLine("Official download and executable SHA-256 verified; no components installed.");
return 0;
