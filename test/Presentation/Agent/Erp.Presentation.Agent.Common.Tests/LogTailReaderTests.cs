using Erp.Presentation.Agent.Common;

namespace Erp.Presentation.Agent.Common.Tests;

public class LogTailReaderTests : IDisposable
{
    private readonly string _tempDir;

    public LogTailReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"erp-log-tail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void First_Read_Primes_At_EOF_And_Returns_Empty()
    {
        var file = Path.Combine(_tempDir, "agent-2026-05-22.log");
        File.WriteAllText(file, "line A\nline B\nline C\n");

        var reader = new LogTailReader(_tempDir);

        // Backlog from before the agent started must NOT be replayed.
        var first = reader.ReadNewLines(maxLines: 100);

        Assert.Empty(first);
        Assert.Equal(file, reader.CurrentPath);
    }

    [Fact]
    public void Subsequent_Read_Returns_Only_New_Lines()
    {
        var file = Path.Combine(_tempDir, "agent-2026-05-22.log");
        File.WriteAllText(file, "old 1\nold 2\n");

        var reader = new LogTailReader(_tempDir);
        reader.ReadNewLines(100); // primes at EOF

        File.AppendAllText(file, "new 1\nnew 2\nnew 3\n");

        var lines = reader.ReadNewLines(100);

        Assert.Equal(new[] { "new 1", "new 2", "new 3" }, lines);
    }

    [Fact]
    public void Respects_MaxLines_Cap()
    {
        var file = Path.Combine(_tempDir, "agent-2026-05-22.log");
        File.WriteAllText(file, "");

        var reader = new LogTailReader(_tempDir);
        reader.ReadNewLines(100);

        File.AppendAllText(file, "a\nb\nc\nd\ne\n");
        var first = reader.ReadNewLines(maxLines: 2);
        Assert.Equal(new[] { "a", "b" }, first);

        var second = reader.ReadNewLines(maxLines: 10);
        Assert.Equal(new[] { "c", "d", "e" }, second);
    }

    [Fact]
    public void Daily_Rotation_Switches_To_New_File_And_Reads_From_Zero()
    {
        var day1 = Path.Combine(_tempDir, "agent-2026-05-22.log");
        File.WriteAllText(day1, "yesterday\n");

        var reader = new LogTailReader(_tempDir);
        reader.ReadNewLines(100); // primes at end of day1
        Assert.Equal(day1, reader.CurrentPath);

        // Serilog rolls — new file appears with later name.
        var day2 = Path.Combine(_tempDir, "agent-2026-05-23.log");
        File.WriteAllText(day2, "today line 1\ntoday line 2\n");

        var lines = reader.ReadNewLines(100);

        Assert.Equal(day2, reader.CurrentPath);
        Assert.Equal(new[] { "today line 1", "today line 2" }, lines);
    }

    [Fact]
    public void File_Shrunk_Resets_Position_To_Zero()
    {
        // If the file is somehow shorter than our last read position (e.g.
        // in-place log rotation that truncates), restart from the beginning
        // rather than failing silently.
        var file = Path.Combine(_tempDir, "agent-2026-05-22.log");
        File.WriteAllText(file, "line 1\nline 2\nline 3\n");

        var reader = new LogTailReader(_tempDir);
        reader.ReadNewLines(100);

        // Truncate so file is shorter than tracked position.
        using (var fs = new FileStream(file, FileMode.Truncate, FileAccess.Write))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("only\n");
            fs.Write(bytes, 0, bytes.Length);
        }

        var lines = reader.ReadNewLines(100);
        Assert.Equal(new[] { "only" }, lines);
    }

    [Fact]
    public void Partial_Last_Line_Without_Newline_Is_Held_Back()
    {
        var file = Path.Combine(_tempDir, "agent-2026-05-22.log");
        File.WriteAllText(file, "");

        var reader = new LogTailReader(_tempDir);
        reader.ReadNewLines(100);

        File.AppendAllText(file, "complete\npartial without newline yet");
        var first = reader.ReadNewLines(100);
        Assert.Equal(new[] { "complete" }, first);

        // Once the line completes, the next tick picks it up.
        File.AppendAllText(file, "\n");
        var second = reader.ReadNewLines(100);
        Assert.Equal(new[] { "partial without newline yet" }, second);
    }

    [Fact]
    public void Missing_Directory_Returns_Empty()
    {
        var reader = new LogTailReader(Path.Combine(_tempDir, "does-not-exist"));
        Assert.Empty(reader.ReadNewLines(100));
    }

    [Fact]
    public void Empty_Directory_Returns_Empty()
    {
        var reader = new LogTailReader(_tempDir);
        Assert.Empty(reader.ReadNewLines(100));
    }
}
