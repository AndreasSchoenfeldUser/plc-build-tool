using System.Globalization;
using PlcBuild.Core.Adapters;
using PlcBuild.Core.Config;
using PlcBuild.Core.Processes;
using PlcBuild.Protocol;

namespace PlcBuild.Core.Steps;

public sealed class StepInvocation
{
    public required PlcConfig Config { get; init; }
    public required ProjectConfig Project { get; init; }
    /// <summary>"run" for a combined session, otherwise equal to the single entry in <see cref="Steps"/>.</summary>
    public required string Step { get; init; }
    public required IReadOnlyList<string> Steps { get; init; }
    public required string OutDir { get; init; }
    public required TimeSpan Timeout { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Runs one adapter session for one project and maps the result to an exit code.
/// Exit 1 only if the adapter delivered a valid response with outcome "failed".
/// </summary>
public static class StepRunner
{
    public static int Run(StepInvocation inv, RunContext ctx)
    {
        var project = inv.Project;
        var layout = new WorkLayout(inv.OutDir, project.Id);
        var adapter = AdapterLocator.Locate(project.Adapter, ctx);
        var settings = project.GetSection(adapter.Manifest.SettingsKey);

        if (!Directory.Exists(project.Root))
        {
            throw PlcBuildException.Usage("PROJECT_PATH_NOT_FOUND",
                $"Projekt „{project.Id}“: Verzeichnis {project.Root} (path: {project.Path}) nicht gefunden.");
        }
        CheckInputs(project, adapter, settings);

        Directory.CreateDirectory(layout.ProjectDir);
        foreach (var step in inv.Steps)
        {
            // A stale response from an earlier run must never be read as the result of this one.
            File.Delete(layout.ResponsePath(step));
        }

        var request = new AdapterRequest
        {
            Step = inv.Step,
            Steps = inv.Steps.ToList(),
            Project = new ProjectInfo
            {
                Id = project.Id,
                Adapter = project.Adapter,
                Root = project.Root,
                WorkDir = layout.WorkDir,
                Settings = settings,
            },
            OutDir = layout.ProjectDir,
            TimeoutSeconds = (int)Math.Ceiling(inv.Timeout.TotalSeconds),
            Options = inv.Options.ToDictionary(),
            Secrets = adapter.Manifest.Secrets.ToDictionary(),
        };
        var requestPath = layout.RequestPath(inv.Step);
        Json.WriteFile(requestPath, request);

        var (fileName, args) = CommandTemplate.Expand(adapter, project, settings, requestPath);
        ctx.Out.WriteLine($"plc-build {inv.Step} {project.Id} (Adapter {project.Adapter})");

        var run = AdapterProcess.Run(
            fileName, args, project.Root, ctx.Environment, inv.Timeout, layout.LogPath(inv.Step),
            onStarted: process => PidLedger.Add(layout.PidsPath,
            [
                new PidEntry
                {
                    Pid = process.Id,
                    Name = AdapterProcessName(fileName),
                    StartTime = ProcessProbe.TryGetStartTime(process),
                    Step = inv.Step,
                    Origin = "adapter",
                },
            ]),
            echo: ctx.Verbose ? ctx.Out : null);
        PidLedger.Prune(layout.PidsPath);

        var exitCode = ExitCodes.Ok;
        var previousOk = true;
        foreach (var step in inv.Steps)
        {
            if (!previousOk)
            {
                ctx.Out.WriteLine($"{step} {project.Id}: übersprungen");
                continue;
            }

            var responsePath = layout.ResponsePath(step);
            AdapterResponse response;
            var (read, problem) = run.Started && !run.TimedOut
                ? ResponseReader.Read(responsePath, step)
                : (null, null);
            if (read is null && run.TimedOut && File.Exists(responsePath))
            {
                // Steps finished before the time limit keep their result.
                (read, _) = ResponseReader.Read(responsePath, step);
            }

            if (read is not null)
            {
                response = read;
            }
            else
            {
                var (code, message) = AdapterFailure(run, problem, adapter, inv, layout);
                response = ResponseReader.Synthetic(step, code, message, (long)run.Duration.TotalMilliseconds);
                Json.WriteFile(responsePath, response);
            }

            RecordProcesses(layout, step, response);
            var stepExit = ExitCodes.FromOutcome(response.Outcome);
            PrintResult(ctx, step, project.Id, response, layout);
            exitCode = ExitCodes.Worst(exitCode, stepExit);
            previousOk = stepExit == ExitCodes.Ok;
        }
        return exitCode;
    }

    private static string AdapterProcessName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>Input files and directories declared by the adapter must exist when set in plc.yml.</summary>
    private static void CheckInputs(ProjectConfig project, LocatedAdapter adapter, Dictionary<string, object?> settings)
    {
        foreach (var key in adapter.Manifest.ManifestInputs)
        {
            if (!settings.TryGetValue(key, out var value) || value is not string relative || relative.Length == 0)
            {
                continue;
            }
            var full = Path.GetFullPath(Path.Combine(project.Root, relative));
            if (!File.Exists(full) && !Directory.Exists(full))
            {
                throw PlcBuildException.Usage("INPUT_NOT_FOUND",
                    $"Projekt „{project.Id}“: {adapter.Manifest.SettingsKey}.{key} = „{relative}“ nicht gefunden ({full}).");
            }
        }
    }

    private static (string Code, string Message) AdapterFailure(
        AdapterRunResult run, ResponseProblem? problem, LocatedAdapter adapter, StepInvocation inv, WorkLayout layout)
    {
        var log = $"Log: {layout.LogPath(inv.Step)}";
        if (!run.Started)
        {
            return ("ADAPTER_START_FAILED", $"Adapter „{adapter.Manifest.Name}“ ließ sich nicht starten: {run.StartError}. {log}");
        }
        if (run.TimedOut)
        {
            var minutes = inv.Timeout.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
            return ("ADAPTER_TIMEOUT", $"Adapter „{adapter.Manifest.Name}“ hat das Zeitlimit von {minutes} min überschritten und wurde beendet. {log}");
        }
        if (problem is { Code: "ADAPTER_NO_RESPONSE" } && run.ExitCode is not 0)
        {
            return ("ADAPTER_CRASHED", $"Adapter „{adapter.Manifest.Name}“ endete mit Exit-Code {run.ExitCode} ohne Antwort. {log}");
        }
        return (problem!.Code, $"{problem.Message} {log}");
    }

    private static void RecordProcesses(WorkLayout layout, string step, AdapterResponse response)
    {
        if (response.Processes.Count == 0)
        {
            return;
        }
        PidLedger.Add(layout.PidsPath, response.Processes.Select(p => new PidEntry
        {
            Pid = p.Pid,
            Name = p.Name,
            StartTime = p.StartTime,
            Step = step,
            Origin = "reported",
        }));
    }

    private static void PrintResult(RunContext ctx, string step, string projectId, AdapterResponse response, WorkLayout layout)
    {
        var seconds = (response.DurationMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
        var summary = string.IsNullOrEmpty(response.Summary) ? "" : $" – {response.Summary}";
        ctx.Out.WriteLine($"{step} {projectId}: {response.Outcome}{summary} ({seconds} s)");

        foreach (var d in Ordering.Diagnostics(response.Diagnostics).Where(d => d.Severity != Severities.Info))
        {
            var label = d.Severity == Severities.Error ? "Fehler " : "Warnung";
            var code = d.Code is null ? "" : $"[{d.Code}] ";
            var obj = d.Object is null ? "" : $"{d.Object}: ";
            ctx.Out.WriteLine($"  {label} {code}{obj}{d.Message}");
        }
        foreach (var t in response.Tests.Where(t => t.Outcome is TestOutcomes.Failed or TestOutcomes.Error))
        {
            var detail = t.Expected is null && t.Actual is null ? "" : $" (erwartet {t.Expected}, ist {t.Actual})";
            ctx.Out.WriteLine($"  Test fehlgeschlagen {t.Suite}.{t.Name}: {t.Message}{detail}");
        }
        foreach (var issue in response.EnvironmentIssues)
        {
            ctx.Out.WriteLine($"  [{issue.Code}] {issue.Message}");
            if (!string.IsNullOrEmpty(issue.Remedy))
            {
                ctx.Out.WriteLine($"    Behebung: {issue.Remedy}");
            }
        }
        if (response.Outcome == Outcomes.Error && response.GeneratedBy is null)
        {
            ctx.Out.WriteLine($"  Log: {layout.LogPath(step)}");
        }
    }
}

/// <summary>Deterministic ordering shared by console output and reports.</summary>
public static class Ordering
{
    public static int SeverityRank(string severity) => severity switch
    {
        Severities.Error => 0,
        Severities.Warning => 1,
        _ => 2,
    };

    public static IEnumerable<Diagnostic> Diagnostics(IEnumerable<Diagnostic> diagnostics) => diagnostics
        .OrderBy(d => SeverityRank(d.Severity))
        .ThenBy(d => d.Object ?? "", StringComparer.Ordinal)
        .ThenBy(d => d.Location?.Network ?? 0)
        .ThenBy(d => d.Location?.Line ?? 0)
        .ThenBy(d => d.Code ?? "", StringComparer.Ordinal)
        .ThenBy(d => d.Message, StringComparer.Ordinal);

    public static IEnumerable<TestResult> Tests(IEnumerable<TestResult> tests) => tests
        .OrderBy(t => t.Suite, StringComparer.Ordinal)
        .ThenBy(t => t.Name, StringComparer.Ordinal);
}
