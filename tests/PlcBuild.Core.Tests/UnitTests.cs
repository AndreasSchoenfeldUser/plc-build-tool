using PlcBuild.Core.Adapters;
using PlcBuild.Core.Config;
using PlcBuild.Core.Reporting;
using PlcBuild.Core.Steps;
using PlcBuild.Protocol;

namespace PlcBuild.Core.Tests;

public class ExitCodeTests
{
    [Theory]
    [InlineData("ok", 0)]
    [InlineData("failed", 1)]
    [InlineData("environment", 3)]
    [InlineData("error", 4)]
    [InlineData("unbekannt", 4)]
    [InlineData(null, 4)]
    public void Outcome_MapsToExitCode(string? outcome, int expected) =>
        Assert.Equal(expected, ExitCodes.FromOutcome(outcome));
}

public class ResponseReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("plc-build-resp").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (AdapterResponse?, ResponseProblem?) Read(string? content, string step = "compile")
    {
        var path = Path.Combine(_dir, "response.json");
        if (content is not null)
        {
            File.WriteAllText(path, content);
        }
        return ResponseReader.Read(path, step);
    }

    [Theory]
    [InlineData(null, "ADAPTER_NO_RESPONSE")]
    [InlineData("nicht json", "ADAPTER_INVALID_RESPONSE")]
    [InlineData("{}", "PROTOCOL_UNSUPPORTED")]
    [InlineData("""{"protocol": 2, "step": "compile", "outcome": "ok"}""", "PROTOCOL_UNSUPPORTED")]
    [InlineData("""{"protocol": 1, "step": "test", "outcome": "ok"}""", "ADAPTER_INVALID_RESPONSE")]
    [InlineData("""{"protocol": 1, "step": "compile", "outcome": "gruen"}""", "ADAPTER_INVALID_RESPONSE")]
    [InlineData("""{"protocol": 1, "step": "compile", "outcome": "ok", "durationMs": "schnell"}""", "ADAPTER_INVALID_RESPONSE")]
    public void Unusable_Response_HasProblemCode(string? content, string code)
    {
        var (response, problem) = Read(content);
        Assert.Null(response);
        Assert.Equal(code, problem!.Code);
    }

    [Fact]
    public void Minimal_Response_IsAccepted_WithUnknownFieldsAndNullLists()
    {
        var (response, problem) = Read("""{"protocol": 1, "step": "compile", "outcome": "failed", "diagnostics": null, "extra": 1}""");
        Assert.Null(problem);
        Assert.Equal(Outcomes.Failed, response!.Outcome);
        Assert.Empty(response.Diagnostics);
    }
}

public class AdapterLocatorTests
{
    [Theory]
    [InlineData("fake", "PLC_BUILD_ADAPTER_FAKE")]
    [InlineData("tia-v21", "PLC_BUILD_ADAPTER_TIA_V21")]
    [InlineData("codesys", "PLC_BUILD_ADAPTER_CODESYS")]
    public void OverrideVariable(string adapter, string variable) =>
        Assert.Equal(variable, AdapterLocator.OverrideVariable(adapter));

    [Fact]
    public void CommandTemplate_ExpandsPlaceholders_AndRequiresSettings()
    {
        var adapter = new LocatedAdapter(new AdapterManifest
        {
            Name = "codesys",
            Protocol = 1,
            Command = "{settings.exe}",
            Args = ["--profile={settings.profile}", "--noUI", "--runscript={adapterDir}/adapter.py", "--scriptargs={request}"],
            SettingsKey = "codesys",
        }, "/agent/adapters/codesys");
        var project = new ProjectConfig
        {
            Id = "p", Adapter = "codesys", Path = "p", Root = "/repo/p",
            Sections = new Dictionary<string, object?>(),
        };
        var settings = new Dictionary<string, object?> { ["exe"] = "C:/CODESYS.exe", ["profile"] = "CODESYS V3.5 SP22 Patch 1" };

        var (file, args) = CommandTemplate.Expand(adapter, project, settings, "/out/request-compile.json");
        Assert.Equal("C:/CODESYS.exe", file);
        Assert.Equal(["--profile=CODESYS V3.5 SP22 Patch 1", "--noUI", "--runscript=/agent/adapters/codesys/adapter.py", "--scriptargs=/out/request-compile.json"], args);

        settings.Remove("profile");
        var ex = Assert.Throws<PlcBuildException>(() => CommandTemplate.Expand(adapter, project, settings, "r"));
        Assert.Equal(ExitCodes.Usage, ex.ExitCode);
        Assert.Contains("codesys.profile", ex.Message);
    }
}

public class HashingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("plc-build-hash").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string rel, string content)
    {
        var path = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void DirectoryHash_DependsOnPathAndContent_NotOnCreationOrder()
    {
        Write("b/x.scl", "B");
        Write("a.scl", "A");
        var (first, count) = Hashing.DirectorySha256(_dir);
        Assert.Equal(2, count);
        // Known value: guards the hash definition of ADR 0005 against accidental change.
        Assert.Equal("d50646bee5fbbcbba7b707b7dfb82cefd96b8b07851984c6317ca3b4e259df98", first);

        File.Delete(Path.Combine(_dir, "a.scl"));
        Write("a.scl", "A");
        Assert.Equal(first, Hashing.DirectorySha256(_dir).Sha256);

        Write("a.scl", "A2");
        var changedContent = Hashing.DirectorySha256(_dir).Sha256;
        Assert.NotEqual(first, changedContent);

        Write("a.scl", "A");
        File.Move(Path.Combine(_dir, "a.scl"), Path.Combine(_dir, "c.scl"));
        Assert.NotEqual(first, Hashing.DirectorySha256(_dir).Sha256);
    }
}

public class SarifTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("plc-build-sarif").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ExternalSarif_IsTakenOver_WithPlcBuildMetadata()
    {
        File.WriteAllText(Path.Combine(_dir, "analysis.sarif"), """
            {"version":"2.1.0","runs":[{"tool":{"driver":{"name":"CODESYS Static Analysis"}},"results":[{"ruleId":"SA0033","message":{"text":"Unbenutzte Variable"}}]}]}
            """);
        var results = new ProjectResults
        {
            ProjectId = "verpackung/plc",
            Problems = [],
            Steps =
            [
                new StepResult("analyze", new AdapterResponse
                {
                    Step = "analyze",
                    Outcome = Outcomes.Failed,
                    Diagnostics = [new Diagnostic { Message = "dupliziert", Code = "SA0033" }],
                    Artifacts = [new Artifact { Kind = "sarif", Path = "analysis.sarif" }],
                }),
            ],
        };

        var problems = new List<string>();
        var sarif = SarifWriter.Write(results, "codesys", _dir, "0.0.0-test", problems);

        Assert.Empty(problems);
        Assert.Contains("CODESYS Static Analysis", sarif);
        Assert.Contains("\"plcBuild\"", sarif);
        Assert.Contains("\"adapter\": \"codesys\"", sarif);
        Assert.DoesNotContain("dupliziert", sarif);
    }

    [Fact]
    public void BrokenExternalSarif_FallsBackToDiagnostics()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.sarif"), "kaputt");
        var results = new ProjectResults
        {
            ProjectId = "p",
            Problems = [],
            Steps =
            [
                new StepResult("analyze", new AdapterResponse
                {
                    Step = "analyze",
                    Outcome = Outcomes.Failed,
                    Diagnostics = [new Diagnostic { Message = "aus der Antwort", Code = "R1" }],
                    Artifacts = [new Artifact { Kind = "sarif", Path = "broken.sarif" }],
                }),
            ],
        };
        var problems = new List<string>();
        var sarif = SarifWriter.Write(results, "codesys", _dir, "0.0.0-test", problems);
        Assert.Contains("aus der Antwort", sarif);
        Assert.Single(problems);
    }
}
