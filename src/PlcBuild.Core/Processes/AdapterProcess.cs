using System.Diagnostics;

namespace PlcBuild.Core.Processes;

public sealed record AdapterRunResult(
    bool Started,
    string? StartError,
    bool TimedOut,
    int? ExitCode,
    TimeSpan Duration);

/// <summary>Starts one adapter process, enforces the time limit and writes its output to the step log.</summary>
public static class AdapterProcess
{
    /// <summary>Time to drain stdout/stderr after the adapter exited; child processes may hold the pipes open.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    public static AdapterRunResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        string logPath,
        Action<Process> onStarted,
        TextWriter? echo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        using var log = new StreamWriter(logPath, append: false, Files.Utf8NoBom) { AutoFlush = true };
        var logLock = new object();
        var closed = false;
        void Write(string stream, string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (logLock)
            {
                if (closed)
                {
                    return; // late output from a child process that kept the pipe open
                }
                log.WriteLine($"[{stream}] {line}");
                echo?.WriteLine($"  | {line}");
            }
        }

        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Files.Utf8NoBom,
            StandardErrorEncoding = Files.Utf8NoBom,
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }
        psi.Environment.Clear();
        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }

        Write("plc-build", $"Start: {fileName} {string.Join(' ', arguments)}");
        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdoutDone = new TaskCompletionSource();
        var stderrDone = new TaskCompletionSource();
        var exited = new TaskCompletionSource();
        process.OutputDataReceived += (_, e) => { if (e.Data is null) stdoutDone.TrySetResult(); else Write("out", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is null) stderrDone.TrySetResult(); else Write("err", e.Data); };
        process.Exited += (_, _) => exited.TrySetResult();

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Write("plc-build", $"Start fehlgeschlagen: {ex.Message}");
            return new AdapterRunResult(false, ex.Message, false, null, stopwatch.Elapsed);
        }

        onStarted(process);
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // The Exited event does not wait for EOF on redirected streams, unlike WaitForExitAsync().
        var timedOut = !exited.Task.Wait(timeout) && !process.HasExited;
        if (timedOut)
        {
            Write("plc-build", $"Zeitlimit {timeout} überschritten, beende Prozessbaum PID {process.Id}.");
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Write("plc-build", $"Beenden fehlgeschlagen: {ex.Message}");
            }
            process.WaitForExit(10_000);
        }

        Task.WaitAll([stdoutDone.Task, stderrDone.Task], DrainTimeout);
        stopwatch.Stop();
        int? exitCode = process.HasExited ? process.ExitCode : null;
        Write("plc-build", $"Ende nach {stopwatch.ElapsedMilliseconds} ms, Exit-Code des Adapters: {exitCode?.ToString() ?? "unbekannt"}");
        lock (logLock)
        {
            closed = true;
        }
        return new AdapterRunResult(true, null, timedOut, exitCode, stopwatch.Elapsed);
    }
}
