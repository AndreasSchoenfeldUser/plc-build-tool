namespace PlcBuild.Core.Steps;

/// <summary>File layout below <c>out/.plc-build/&lt;project id with / → _&gt;/</c> (CLAUDE.md section 5).</summary>
public sealed class WorkLayout(string outDir, string projectId)
{
    public const string StateDirName = ".plc-build";

    public string OutDir { get; } = Path.GetFullPath(outDir);
    public string ProjectId { get; } = projectId;
    public string StateRoot => Path.Combine(OutDir, StateDirName);
    public string ProjectDir => Path.Combine(StateRoot, Files.SafeName(ProjectId));
    public string WorkDir => Path.Combine(ProjectDir, "work");
    public string PidsPath => Path.Combine(ProjectDir, "pids.json");

    public string RequestPath(string step) => Path.Combine(ProjectDir, $"request-{step}.json");
    public string ResponsePath(string step) => Path.Combine(ProjectDir, $"response-{step}.json");
    public string LogPath(string step) => Path.Combine(ProjectDir, $"adapter-{step}.log");

    /// <summary>All project state directories, for <c>cleanup</c> without <c>--project</c>.</summary>
    public static IEnumerable<string> AllProjectDirs(string outDir)
    {
        var root = Path.Combine(Path.GetFullPath(outDir), StateDirName);
        return Directory.Exists(root) ? Directory.GetDirectories(root).Order(StringComparer.Ordinal) : [];
    }
}
