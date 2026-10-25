using System.Reflection;
using System.Runtime.CompilerServices;
using PlcBuild.Cli;
using PlcBuild.Core;

namespace PlcBuild.Core.Tests;

/// <summary>A temporary customer repository with plc.yml, base archive and sources, driven through the real CLI.</summary>
public sealed class TestRepo : IDisposable
{
    public const string ProjectId = "linie-1/plc";
    public const string TestCommit = "0123456789abcdef0123456789abcdef01234567";
    public static readonly DateTimeOffset Now = new(2026, 10, 25, 8, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string> _env;

    public TestRepo(string scenario = "ok", string? extraFakeSettings = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "plc-build-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        WriteConfig($"""
            version: 1
            unknownTopLevel: wird toleriert
            projects:
              - id: {ProjectId}
                adapter: fake
                path: linie-1/plc
                gitBridge: {"{ branch: main }"}
                fake:
                  baseArchive: base/Linie1.zapfake
                  sources: src/plc
                  scenario: {scenario}
            {Indent(extraFakeSettings)}
            """);
        WriteFile("linie-1/plc/base/Linie1.zapfake", "FAKE-BASE-PROJECT\n");
        WriteFile("linie-1/plc/src/plc/FB_Foerderband.scl", "FUNCTION_BLOCK \"FB_Foerderband\"\nEND_FUNCTION_BLOCK\n");
        WriteFile("linie-1/plc/src/plc/Greifer/FB_Greifer.scl", "FUNCTION_BLOCK \"FB_Greifer\"\nEND_FUNCTION_BLOCK\n");

        _env = BaseEnvironment();
        _env["PLC_BUILD_ADAPTER_FAKE"] = FakeAdapterDir;
        _env["GIT_COMMIT"] = TestCommit;
    }

    public string Root { get; }
    public string OutDir => Path.Combine(Root, "out");
    public string StateDir => Path.Combine(OutDir, ".plc-build", "linie-1_plc");
    public StringWriter Out { get; private set; } = new();
    public StringWriter Err { get; private set; } = new();

    public void SetEnv(string name, string? value)
    {
        if (value is null) _env.Remove(name); else _env[name] = value;
    }

    public void WriteConfig(string yaml) => WriteFile("plc.yml", yaml);

    public void WriteFile(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n"), Files.Utf8NoBom);
    }

    public string ReadFile(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    /// <summary>Runs plc-build in-process with this repository as working directory.</summary>
    public int Run(params string[] args)
    {
        Out = new StringWriter();
        Err = new StringWriter();
        var ctx = new RunContext
        {
            WorkingDirectory = Root,
            Environment = new Dictionary<string, string>(_env),
            Out = Out,
            Error = Err,
            Clock = new FixedClock(Now),
            ToolVersion = "0.0.0-test",
            ToolDirectory = Path.Combine(Root, "no-tool-dir"),
        };
        return CliApp.Run(args, ctx);
    }

    public string Output => Out.ToString() + Err.ToString();

    public void Dispose()
    {
        try
        {
            Run("cleanup");
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // best effort; temp directory
        }
    }

    public static string FakeAdapterDir { get; } = Path.GetFullPath(
        typeof(TestRepo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "FakeAdapterDir").Value!);

    private static string Indent(string? block) =>
        block is null ? "" : string.Join("\n", block.Split('\n').Select(l => "      " + l));

    /// <summary>The real environment without variables that would change test results.</summary>
    private static Dictionary<string, string> BaseEnvironment()
    {
        var env = RunContext.FromProcess().Environment
            .Where(kv => !kv.Key.StartsWith("PLC_BUILD_", StringComparison.Ordinal) && kv.Key != "GIT_COMMIT")
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        // The fake adapter is an apphost; it must find the same runtime the tests run on.
        if (!env.ContainsKey("DOTNET_ROOT"))
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            env["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
        }
        return env;
    }
}

public static class Golden
{
    /// <summary>
    /// Compares with tests/PlcBuild.Core.Tests/golden/&lt;name&gt;. Set PLC_BUILD_UPDATE_GOLDEN=1 to rewrite;
    /// a changed golden file needs a reason in the commit message (CLAUDE.md section 12).
    /// </summary>
    public static void Verify(string name, string actual, [CallerFilePath] string callerFile = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(callerFile)!, "golden", name);
        actual = actual.Replace("\r\n", "\n");
        if (Environment.GetEnvironmentVariable("PLC_BUILD_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, Files.Utf8NoBom);
            return;
        }
        Assert.True(File.Exists(path), $"Golden file {path} fehlt. Mit PLC_BUILD_UPDATE_GOLDEN=1 erzeugen.");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
    }
}
