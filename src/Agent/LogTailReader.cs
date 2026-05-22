namespace Agent;

/// <summary>
/// Reads newly-appended lines from the Serilog daily-rolled file sink.
/// Position is tracked in-memory; on restart we start from EOF (no replay
/// of historical lines — that'd flood the server with stale data on every
/// agent reboot).
///
/// File rotation handling: when the latest file path changes (e.g. midnight
/// roll), the new file is read from byte 0. The old file's tail is NOT
/// re-read — if those last few lines hadn't been shipped yet they're lost,
/// which is acceptable for a PoC observability path.
/// </summary>
public sealed class LogTailReader
{
    private readonly string _logsDirectory;
    private string? _currentPath;
    private long _position;

    public LogTailReader(string logsDirectory)
    {
        _logsDirectory = logsDirectory;
    }

    /// <summary>
    /// Most recently observed log file. Public so tests can assert which
    /// file is being read.
    /// </summary>
    public string? CurrentPath => _currentPath;

    /// <summary>
    /// Read newly-appended lines from the latest log file. First call on a
    /// fresh reader returns an empty list (the existing file content is
    /// considered backlog, not "new since the agent started"). Subsequent
    /// calls return whatever was appended in between.
    /// </summary>
    public IReadOnlyList<string> ReadNewLines(int maxLines)
    {
        if (maxLines <= 0) return Array.Empty<string>();

        var latest = FindLatestLogFile();
        if (latest is null) return Array.Empty<string>();

        // First call OR file rotated: switch to the new file. We jump to its
        // current length so we don't replay history on agent start, and to 0
        // so we get the full content of the new file after a roll.
        if (_currentPath is null)
        {
            _currentPath = latest;
            try { _position = new FileInfo(latest).Length; } catch { _position = 0; }
            return Array.Empty<string>();
        }

        if (!string.Equals(_currentPath, latest, StringComparison.Ordinal))
        {
            _currentPath = latest;
            _position = 0;
        }

        // File may have been truncated externally (rare but possible — e.g.
        // operator clearing logs). If so, restart from the beginning.
        long currentLength;
        try { currentLength = new FileInfo(_currentPath).Length; }
        catch { return Array.Empty<string>(); }

        if (currentLength < _position) _position = 0;
        if (currentLength == _position) return Array.Empty<string>();

        // FileShare.ReadWrite so we don't conflict with Serilog's own write
        // handle (it opens the file with `shared: true`).
        //
        // We parse newlines directly off the byte buffer rather than going
        // through StreamReader. StreamReader's internal buffer would advance
        // the FileStream's position to EOF on the first ReadLine, which
        // makes maxLines-capping useless — we'd lose track of which line
        // the next tick should start from.
        try
        {
            using var fs = new FileStream(
                _currentPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024);
            fs.Position = _position;

            var available = (int)Math.Min(int.MaxValue, currentLength - _position);
            var buffer = new byte[available];
            var read = 0;
            while (read < available)
            {
                var n = fs.Read(buffer, read, available - read);
                if (n <= 0) break;
                read += n;
            }

            var collected = new List<string>(capacity: Math.Min(maxLines, 256));
            var lineStart = 0;
            var consumedBytes = 0;
            for (var i = 0; i < read && collected.Count < maxLines; i++)
            {
                if (buffer[i] != (byte)'\n') continue;

                var lineEnd = i;
                // Strip trailing \r if CRLF.
                if (lineEnd > lineStart && buffer[lineEnd - 1] == (byte)'\r')
                    lineEnd--;

                collected.Add(System.Text.Encoding.UTF8.GetString(buffer, lineStart, lineEnd - lineStart));
                lineStart = i + 1;
                consumedBytes = i + 1;
            }

            // Half-written trailing line (no terminating \n) stays for next tick.
            _position += consumedBytes;
            return collected;
        }
        catch
        {
            // Transient file errors (rotation race, permissions blip) — give up
            // this tick; the next interval will retry.
            return Array.Empty<string>();
        }
    }

    private string? FindLatestLogFile()
    {
        try
        {
            if (!Directory.Exists(_logsDirectory)) return null;
            // Serilog daily roll: `agent-YYYY-MM-DD.log`. Sorting alphanumerically
            // is equivalent to sorting by date — pick the last entry.
            var files = Directory.EnumerateFiles(_logsDirectory, "agent-*.log").ToList();
            files.Sort(StringComparer.Ordinal);
            return files.Count == 0 ? null : files[^1];
        }
        catch
        {
            return null;
        }
    }
}
