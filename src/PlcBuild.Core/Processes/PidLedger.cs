using System.Diagnostics;

namespace PlcBuild.Core.Processes;

public sealed class PidEntry
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset? StartTime { get; set; }
    public string Step { get; set; } = "";
    /// <summary>"adapter" for the adapter process the core started, "reported" for processes the adapter reported.</summary>
    public string Origin { get; set; } = "";
}

public sealed class PidFile
{
    public List<PidEntry> Processes { get; set; } = [];
}

/// <summary>
/// Bookkeeping in <c>pids.json</c> so that <c>cleanup</c> only kills processes plc-build started itself,
/// even after the core was killed by the CI server.
/// </summary>
public static class PidLedger
{
    /// <summary>Process start times are compared with this tolerance (adapters may report rounded times).</summary>
    public static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    public static PidFile Load(string path)
    {
        if (!File.Exists(path))
        {
            return new PidFile();
        }
        try
        {
            return Json.Deserialize<PidFile>(File.ReadAllText(path)) ?? new PidFile();
        }
        catch (System.Text.Json.JsonException)
        {
            return new PidFile();
        }
    }

    /// <summary>Adds entries and drops those whose process is gone.</summary>
    public static void Add(string path, IEnumerable<PidEntry> entries)
    {
        var file = Load(path);
        file.Processes.AddRange(entries);
        file.Processes = file.Processes
            .Where(e => ProcessProbe.IsSameProcessRunning(e.Pid, e.StartTime))
            .GroupBy(e => (e.Pid, e.StartTime))
            .Select(g => g.First())
            .OrderBy(e => e.Pid)
            .ToList();
        Json.WriteFile(path, file);
    }

    /// <summary>Drops entries whose process has exited.</summary>
    public static void Prune(string path)
    {
        if (File.Exists(path))
        {
            Add(path, []);
        }
    }

    public sealed record CleanupResult(int Killed, int Skipped, int Failed);

    /// <summary>Kills recorded processes that are still running with matching start time.</summary>
    public static CleanupResult Cleanup(string path, TextWriter log)
    {
        var file = Load(path);
        int killed = 0, skipped = 0, failed = 0;
        var remaining = new List<PidEntry>();
        foreach (var entry in file.Processes)
        {
            if (entry.StartTime is null)
            {
                log.WriteLine($"  [PID_NO_START_TIME] PID {entry.Pid} ({entry.Name}) ohne Startzeit gemeldet, wird nicht beendet.");
                skipped++;
                continue;
            }
            if (!ProcessProbe.IsSameProcessRunning(entry.Pid, entry.StartTime))
            {
                continue;
            }
            try
            {
                using var process = Process.GetProcessById(entry.Pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
                log.WriteLine($"  PID {entry.Pid} ({entry.Name}, Schritt {entry.Step}) beendet.");
                killed++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                if (ProcessProbe.IsSameProcessRunning(entry.Pid, entry.StartTime))
                {
                    log.WriteLine($"  [KILL_FAILED] PID {entry.Pid} ({entry.Name}) ließ sich nicht beenden: {ex.Message}");
                    remaining.Add(entry);
                    failed++;
                }
                else
                {
                    killed++;
                }
            }
        }
        file.Processes = remaining;
        Json.WriteFile(path, file);
        return new CleanupResult(killed, skipped, failed);
    }
}

public static class ProcessProbe
{
    public static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True if a process with this PID runs and was started at the recorded time (protects against PID reuse).</summary>
    public static bool IsSameProcessRunning(int pid, DateTimeOffset? recordedStart)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return false;
            }
            if (recordedStart is null)
            {
                return true;
            }
            var actual = TryGetStartTime(process);
            return actual is not null && (actual.Value - recordedStart.Value).Duration() <= PidLedger.StartTimeTolerance;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
