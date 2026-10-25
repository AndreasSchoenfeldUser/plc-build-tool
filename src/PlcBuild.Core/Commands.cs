using PlcBuild.Core.Adapters;
using PlcBuild.Core.Config;
using PlcBuild.Core.Processes;
using PlcBuild.Core.Reporting;
using PlcBuild.Core.Steps;
using PlcBuild.Protocol;

namespace PlcBuild.Core;

public sealed class CommonOptions
{
    public string? ConfigPath { get; init; }
    public string OutDir { get; init; } = "out";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);
    public bool Verbose { get; init; }
}

/// <summary>The commands of CLAUDE.md section 4, independent of command line parsing.</summary>
public static class Commands
{
    public static int Doctor(RunContext ctx, CommonOptions common, string? projectId) => Guard(ctx, common, () =>
    {
        var config = LoadConfig(ctx, common);
        var projects = projectId is null ? config.Projects : [config.GetProject(projectId)];
        var exit = ExitCodes.Ok;
        foreach (var project in projects)
        {
            var code = Guard(ctx, common, () => StepRunner.Run(Invocation(ctx, common, config, project, StepNames.Doctor), ctx));
            exit = ExitCodes.Worst(exit, code);
        }
        return exit;
    });

    public static int Step(RunContext ctx, CommonOptions common, string step, string projectId,
        IReadOnlyDictionary<string, string>? options = null) => Guard(ctx, common, () =>
    {
        var config = LoadConfig(ctx, common);
        var project = config.GetProject(projectId);
        return StepRunner.Run(Invocation(ctx, common, config, project, step, options: options), ctx);
    });

    public static int Archive(RunContext ctx, CommonOptions common, string projectId, string archiveFile) =>
        Step(ctx, common, StepNames.Archive, projectId, new Dictionary<string, string>
        {
            ["archiveFile"] = ctx.ResolvePath(archiveFile),
        });

    public static int Run(RunContext ctx, CommonOptions common, string projectId, IReadOnlyList<string> steps) => Guard(ctx, common, () =>
    {
        foreach (var step in steps)
        {
            if (!StepNames.RunnableInSession.Contains(step))
            {
                throw PlcBuildException.Usage("STEP_UNKNOWN",
                    $"Schritt „{step}“ ist in run nicht erlaubt. Erlaubt: {string.Join(",", StepNames.RunnableInSession)}.");
            }
        }
        if (steps.Count == 0 || steps.Distinct().Count() != steps.Count)
        {
            throw PlcBuildException.Usage("STEP_UNKNOWN", "--steps braucht mindestens einen Schritt, jeder Schritt höchstens einmal.");
        }
        var config = LoadConfig(ctx, common);
        var project = config.GetProject(projectId);
        return StepRunner.Run(Invocation(ctx, common, config, project, StepNames.Run, steps), ctx);
    });

    public static int Report(RunContext ctx, CommonOptions common, string projectId, string junitDir, string? sarifDir) => Guard(ctx, common, () =>
    {
        var config = LoadConfig(ctx, common);
        var project = config.GetProject(projectId);
        var layout = new WorkLayout(ctx.ResolvePath(common.OutDir), project.Id);
        var results = ProjectResults.Load(layout);
        var problems = results.Problems.ToList();

        Dictionary<string, object?> settings = [];
        IEnumerable<string> inputs = [];
        try
        {
            var adapter = AdapterLocator.Locate(project.Adapter, ctx);
            settings = project.GetSection(adapter.Manifest.SettingsKey);
            inputs = adapter.Manifest.ManifestInputs;
        }
        catch (PlcBuildException ex)
        {
            problems.Add($"[{ex.Code}] {ex.Message} Das Manifest enthält deshalb keine Eingangs-Hashes.");
        }

        var name = Files.SafeName(project.Id);
        var junitPath = Path.Combine(ctx.ResolvePath(junitDir), name + ".xml");
        Files.WriteText(junitPath, JUnitWriter.Write(results));
        ctx.Out.WriteLine($"JUnit:    {junitPath}");

        if (sarifDir is not null)
        {
            var sarifPath = Path.Combine(ctx.ResolvePath(sarifDir), name + ".sarif");
            Files.WriteText(sarifPath, SarifWriter.Write(results, project.Adapter, project.Root, ctx.ToolVersion, problems));
            ctx.Out.WriteLine($"SARIF:    {sarifPath}");
        }

        var manifest = ManifestBuilder.Build(project, config.RepositoryRoot, results, settings, inputs, ctx);
        var manifestPath = Path.Combine(layout.OutDir, "manifest", name + ".json");
        Json.WriteFile(manifestPath, manifest);
        ctx.Out.WriteLine($"Manifest: {manifestPath}");

        if (results.Steps.Count == 0)
        {
            problems.Add($"[NO_RESULTS] Für Projekt „{project.Id}“ liegen unter {layout.ProjectDir} keine Ergebnisse vor.");
        }
        foreach (var problem in problems)
        {
            ctx.Error.WriteLine($"Warnung: {problem}");
        }
        return ExitCodes.Ok;
    });

    /// <summary>Kills only processes recorded in pids.json with matching start time. Needs no plc.yml.</summary>
    public static int Cleanup(RunContext ctx, CommonOptions common, string? projectId) => Guard(ctx, common, () =>
    {
        var outDir = ctx.ResolvePath(common.OutDir);
        var dirs = projectId is null
            ? WorkLayout.AllProjectDirs(outDir).ToList()
            : [new WorkLayout(outDir, projectId).ProjectDir];

        int killed = 0, failed = 0;
        foreach (var dir in dirs)
        {
            var pids = Path.Combine(dir, "pids.json");
            if (!File.Exists(pids))
            {
                continue;
            }
            var result = PidLedger.Cleanup(pids, ctx.Out);
            killed += result.Killed;
            failed += result.Failed;
        }
        ctx.Out.WriteLine($"cleanup: {killed} Prozess(e) beendet" + (failed > 0 ? $", {failed} nicht beendbar" : ""));
        return failed > 0 ? ExitCodes.Aborted : ExitCodes.Ok;
    });

    private static StepInvocation Invocation(RunContext ctx, CommonOptions common, PlcConfig config, ProjectConfig project,
        string step, IReadOnlyList<string>? steps = null, IReadOnlyDictionary<string, string>? options = null) => new()
    {
        Config = config,
        Project = project,
        Step = step,
        Steps = steps ?? [step],
        OutDir = ctx.ResolvePath(common.OutDir),
        Timeout = common.Timeout,
        Options = options ?? new Dictionary<string, string>(),
    };

    private static PlcConfig LoadConfig(RunContext ctx, CommonOptions common) =>
        PlcConfigLoader.Load(ctx.ResolvePath(common.ConfigPath ?? PlcConfigLoader.DefaultFileName));

    private static int Guard(RunContext ctx, CommonOptions common, Func<int> action)
    {
        ctx.Verbose = common.Verbose;
        try
        {
            return action();
        }
        catch (PlcBuildException ex)
        {
            ctx.Error.WriteLine($"[{ex.Code}] {ex.Message}");
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ctx.Error.WriteLine($"[IO_ERROR] {ex.Message}");
            return ExitCodes.Aborted;
        }
    }
}
