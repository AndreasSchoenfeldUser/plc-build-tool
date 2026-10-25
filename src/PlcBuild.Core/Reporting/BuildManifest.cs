using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PlcBuild.Core.Config;
using PlcBuild.Protocol;

namespace PlcBuild.Core.Reporting;

/// <summary>
/// Evidence of what a state was built and checked with (CLAUDE.md section 6). Not a compliance claim;
/// a building block for test reports and change records.
/// </summary>
public sealed class BuildManifest
{
    public int ManifestVersion { get; set; } = 1;
    public string Generator { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? Commit { get; set; }
    public ManifestProject Project { get; set; } = new();
    public string Outcome { get; set; } = "";
    public ToolInfo? Tool { get; set; }
    public List<ManifestInput> Inputs { get; set; } = [];
    public List<ManifestStep> Steps { get; set; } = [];
    public List<Artifact> Artifacts { get; set; } = [];
}

public sealed class ManifestProject
{
    public string Id { get; set; } = "";
    public string Adapter { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class ManifestInput
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>"file", "directory" or "missing".</summary>
    public string Kind { get; set; } = "";
    public int? Files { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class ManifestStep
{
    public string Step { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string? Summary { get; set; }
    public long DurationMs { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int? TestsTotal { get; set; }
    public int? TestsFailed { get; set; }
}

public static class ManifestBuilder
{
    public static BuildManifest Build(
        ProjectConfig project, string repositoryRoot, ProjectResults results,
        IReadOnlyDictionary<string, object?> settings, IEnumerable<string> inputKeys, RunContext ctx)
    {
        var manifest = new BuildManifest
        {
            Generator = $"plc-build {ctx.ToolVersion}",
            CreatedAt = TruncateToSeconds(ctx.Clock.UtcNow),
            Commit = ResolveCommit(repositoryRoot, ctx),
            Project = new ManifestProject { Id = project.Id, Adapter = project.Adapter, Path = project.Path.Replace('\\', '/') },
            Outcome = results.OverallOutcome,
            Tool = MergeTool(results),
        };

        foreach (var key in inputKeys.Distinct().Order(StringComparer.Ordinal))
        {
            if (settings.TryGetValue(key, out var value) && value is string relative && relative.Length > 0)
            {
                manifest.Inputs.Add(HashInput(key, relative, project.Root));
            }
        }

        foreach (var (step, r) in results.Steps.Select(s => (s.Step, s.Response)))
        {
            manifest.Steps.Add(new ManifestStep
            {
                Step = step,
                Outcome = r.Outcome,
                Summary = r.Summary,
                DurationMs = r.DurationMs,
                Errors = r.Diagnostics.Count(d => d.Severity == Severities.Error),
                Warnings = r.Diagnostics.Count(d => d.Severity == Severities.Warning),
                TestsTotal = step == StepNames.Test ? r.Tests.Count : null,
                TestsFailed = step == StepNames.Test ? r.Tests.Count(t => t.Outcome is TestOutcomes.Failed or TestOutcomes.Error) : null,
            });
            foreach (var artifact in r.Artifacts)
            {
                var full = Path.GetFullPath(artifact.Path, project.Root);
                manifest.Artifacts.Add(new Artifact
                {
                    Kind = artifact.Kind,
                    Path = Files.RelativeIfBelow(repositoryRoot, full),
                    Sha256 = artifact.Sha256 ?? (File.Exists(full) ? Hashing.FileSha256(full) : null),
                });
            }
        }
        manifest.Artifacts = manifest.Artifacts
            .OrderBy(a => a.Kind, StringComparer.Ordinal)
            .ThenBy(a => a.Path, StringComparer.Ordinal)
            .ToList();
        return manifest;
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset t) =>
        new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Offset);

    /// <summary>First tool info in step order; component lists merged and sorted.</summary>
    private static ToolInfo? MergeTool(ProjectResults results)
    {
        var tools = results.Steps.Select(s => s.Response.Tool).OfType<ToolInfo>().ToList();
        if (tools.Count == 0)
        {
            return null;
        }
        var components = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in tools.SelectMany(t => t.Components ?? []))
        {
            components.TryAdd(key, value);
        }
        return new ToolInfo { Name = tools[0].Name, Version = tools[0].Version, Components = new Dictionary<string, string>(components) };
    }

    private static ManifestInput HashInput(string name, string relative, string root)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var input = new ManifestInput { Name = name, Path = relative.Replace('\\', '/') };
        if (File.Exists(full))
        {
            input.Kind = "file";
            input.Sha256 = Hashing.FileSha256(full);
        }
        else if (Directory.Exists(full))
        {
            var (hash, count) = Hashing.DirectorySha256(full);
            input.Kind = "directory";
            input.Files = count;
            input.Sha256 = hash;
        }
        else
        {
            input.Kind = "missing";
        }
        return input;
    }

    /// <summary>GIT_COMMIT (set by Jenkins) or <c>git rev-parse HEAD</c>; null outside a repository.</summary>
    public static string? ResolveCommit(string repositoryRoot, RunContext ctx)
    {
        if (ctx.GetEnv("GIT_COMMIT") is { } commit)
        {
            return commit;
        }
        try
        {
            var psi = new ProcessStartInfo("git", ["rev-parse", "HEAD"])
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var git = Process.Start(psi);
            if (git is null)
            {
                return null;
            }
            var output = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit(10_000);
            return git.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}

public static class Hashing
{
    public static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Hash over path and content of all files (ADR 0005): files sorted ordinally by relative path with '/',
    /// one line "&lt;path&gt;\0&lt;sha256 of content&gt;\n" per file, SHA-256 over the UTF-8 bytes of all lines.
    /// </summary>
    public static (string Sha256, int Files) DirectorySha256(string dir)
    {
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(dir, f).Replace('\\', '/')))
            .OrderBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();
        var sb = new StringBuilder();
        foreach (var (full, rel) in files)
        {
            sb.Append(rel).Append('\0').Append(FileSha256(full)).Append('\n');
        }
        return (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))), files.Count);
    }
}
