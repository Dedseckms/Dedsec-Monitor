using System.Globalization;
using System.Text;

namespace Dedsec.Core;

// PresentMon writes RFC 4180 CSV. Resolve metrics by header rather than release-specific offsets.
public sealed class PresentMonParser
{
    private Dictionary<string, int>? _columns;

    public FrameEvent? Parse(string line, DateTimeOffset received)
    {
        var values = SplitCsv(line);
        if (values.Contains("ProcessID", StringComparer.OrdinalIgnoreCase) && values.Contains("Application", StringComparer.OrdinalIgnoreCase))
        {
            _columns = values.Select((name, index) => (name, index))
                .DistinctBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
            return null;
        }
        if (_columns is null) return null;
        string Get(string column) => _columns.TryGetValue(column, out var i) && i < values.Count ? values[i] : "";
        if (!int.TryParse(Get("ProcessID"), out var pid) || pid <= 0) return null;
        var time = Get("MsBetweenPresents");
        if (time.Length == 0) time = Get("FrameTime");
        if (!double.TryParse(time, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            || !double.IsFinite(ms) || ms <= 0 || ms > 1000) return null;
        return new FrameEvent(pid, Get("Application"), Get("SwapChainAddress"), ms, received);
    }

    public static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { result.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        result.Add(field.ToString());
        return result;
    }
}

public sealed record FrameEvent(int ProcessId, string Application, string SwapChain,
    double Milliseconds, DateTimeOffset Received);

public sealed class FrameStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(int Pid, string Chain), Queue<FrameEvent>> _frames = [];
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private DateTimeOffset _lastPrune;

    public void Add(FrameEvent frame)
    {
        lock (_gate)
        {
            Prune(frame.Received);
            var key = (frame.ProcessId, frame.SwapChain);
            if (!_frames.TryGetValue(key, out var queue))
            {
                if (_frames.Count >= 256) return;
                _frames[key] = queue = new Queue<FrameEvent>();
            }
            queue.Enqueue(frame);
            while (queue.Count > 8192) queue.Dequeue();
        }
    }

    public FrameSummary Read(int processId, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            var queue = _frames.Where(x => x.Key.Pid == processId)
                .Select(x => x.Value).OrderByDescending(x => x.Count).FirstOrDefault();
            if (queue is null || queue.Count < 2) return new(processId, "", null, null, null, 0);
            var times = queue.Select(x => x.Milliseconds).Order().ToArray();
            var average = times.Average();
            var slowCount = Math.Max(1, (int)Math.Ceiling(times.Length * .01));
            return new(processId, queue.Last().Application, 1000 / average, average,
                1000 / times.TakeLast(slowCount).Average(), times.Length);
        }
    }

    public IReadOnlyList<(int Pid, string Name)> Processes(DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            return _frames.GroupBy(x => x.Key.Pid)
                .Select(x => (x.Key, x.First().Value.Last().Application)).OrderBy(x => x.Item2).ToArray();
        }
    }

    public void Clear() { lock (_gate) _frames.Clear(); }

    private void Prune(DateTimeOffset now)
    {
        if (now - _lastPrune < TimeSpan.FromMilliseconds(200)) return;
        _lastPrune = now;
        foreach (var (key, queue) in _frames.ToArray())
        {
            while (queue.TryPeek(out var first) && now - first.Received > Window) queue.Dequeue();
            if (queue.Count == 0) _frames.Remove(key);
        }
    }
}
