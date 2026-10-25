using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlcBuild.Core;

/// <summary>Exit codes, CLAUDE.md section 4. The split between 1 and 3/4 is a core promise.</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    /// <summary>Finding in the project: only when the adapter answered with outcome "failed".</summary>
    public const int Finding = 1;
    public const int Usage = 2;
    public const int Environment = 3;
    public const int Aborted = 4;

    public static int FromOutcome(string? outcome) => outcome switch
    {
        PlcBuild.Protocol.Outcomes.Ok => Ok,
        PlcBuild.Protocol.Outcomes.Failed => Finding,
        PlcBuild.Protocol.Outcomes.Environment => Environment,
        _ => Aborted,
    };

    /// <summary>Combines results of several projects or steps: the higher code wins.</summary>
    public static int Worst(int a, int b) => Math.Max(a, b);
}

/// <summary>An error with a stable code that ends the command with a defined exit code.</summary>
public sealed class PlcBuildException(string code, int exitCode, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;

    public static PlcBuildException Usage(string code, string message) => new(code, ExitCodes.Usage, message);
    public static PlcBuildException Env(string code, string message) => new(code, ExitCodes.Environment, message);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>
/// Everything a command needs from the outside world. Tests run commands in-process and in parallel,
/// so nothing below reads Environment.CurrentDirectory or process environment variables directly.
/// </summary>
public sealed class RunContext
{
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyDictionary<string, string> Environment { get; init; }
    public required TextWriter Out { get; init; }
    public required TextWriter Error { get; init; }
    public IClock Clock { get; init; } = new SystemClock();
    /// <summary>Directory of the plc-build executable; default location of <c>adapters/</c>.</summary>
    public string ToolDirectory { get; init; } = AppContext.BaseDirectory;
    public string ToolVersion { get; init; } = DefaultToolVersion;
    public bool Verbose { get; set; }

    public string? GetEnv(string name) =>
        Environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public string ResolvePath(string path) => Path.GetFullPath(path, WorkingDirectory);

    public static string DefaultToolVersion =>
        typeof(RunContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static RunContext FromProcess()
    {
        var env = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry e in System.Environment.GetEnvironmentVariables())
        {
            env[(string)e.Key] = (string?)e.Value ?? "";
        }
        return new RunContext
        {
            WorkingDirectory = System.Environment.CurrentDirectory,
            Environment = env,
            Out = Console.Out,
            Error = Console.Error,
        };
    }
}

public static class Json
{
    /// <summary>Deterministic output: camelCase, indented, LF line endings, readable umlauts.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    public static void WriteFile<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Files.WriteText(path, Serialize(value));
    }
}

public static class Files
{
    public static readonly System.Text.Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

    public static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Utf8NoBom);
    }

    /// <summary>Project ids contain '/', file names do not.</summary>
    public static string SafeName(string projectId) => projectId.Replace('/', '_').Replace('\\', '_');

    /// <summary>Relative path with forward slashes if <paramref name="path"/> lies below <paramref name="baseDir"/>.</summary>
    public static string RelativeIfBelow(string baseDir, string path)
    {
        var full = Path.GetFullPath(path);
        var rel = Path.GetRelativePath(baseDir, full);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
        {
            return full.Replace('\\', '/');
        }
        return rel.Replace('\\', '/');
    }
}
