using System.Diagnostics;
using PlcBuild.Core.Processes;
using PlcBuild.Core.Reporting;

namespace PlcBuild.Core.Tests;

/// <summary>M0 acceptance: every fake scenario with expected exit code, golden files for the reports.</summary>
public class ScenarioTests
{
    private const string P = TestRepo.ProjectId;

    private static void Report(TestRepo repo, string goldenPrefix)
    {
        Assert.Equal(0, repo.Run("report", "--project", P, "--junit", "out/junit", "--sarif", "out/sarif"));
        Golden.Verify($"{goldenPrefix}.junit.xml", repo.ReadFile("out/junit/linie-1_plc.xml"));
        Golden.Verify($"{goldenPrefix}.sarif", repo.ReadFile("out/sarif/linie-1_plc.sarif"));
        Golden.Verify($"{goldenPrefix}.manifest.json", repo.ReadFile("out/manifest/linie-1_plc.json"));
    }

    [Fact]
    public void Ok_FullPipeline_IsGreen()
    {
        using var repo = new TestRepo("ok");
        Assert.Equal(0, repo.Run("doctor"));
        Assert.Equal(0, repo.Run("restore", "--project", P));
        Assert.Equal(0, repo.Run("import", "--project", P));
        Assert.Equal(0, repo.Run("compile", "--project", P));
        Assert.Contains("Warnung PLC_1/Programmbausteine/Main", repo.Output); // warnings do not change the exit code
        Assert.Equal(0, repo.Run("analyze", "--project", P));
        Assert.Equal(0, repo.Run("test", "--project", P));
        Assert.Equal(0, repo.Run("archive", "--project", P, "--out", "out/Linie1_1.zapfake"));
        Report(repo, "ok");
    }

    [Fact]
    public void CompileError_IsFinding_Exit1()
    {
        using var repo = new TestRepo("compile-error");
        Assert.Equal(0, repo.Run("restore", "--project", P));
        Assert.Equal(1, repo.Run("compile", "--project", P));
        Assert.Contains("Fehler  PLC_1/Programmbausteine/Foerderband/FB_Foerderband: Operand „#Freigabe“ ist nicht definiert.", repo.Output);
        Report(repo, "compile-error");
    }

    [Fact]
    public void RuleViolation_IsFinding_Exit1()
    {
        using var repo = new TestRepo("rule-violation");
        Assert.Equal(0, repo.Run("compile", "--project", P));
        Assert.Equal(1, repo.Run("analyze", "--project", P));
        Report(repo, "rule-violation");
    }

    [Fact]
    public void TestFailure_IsFinding_Exit1_WithExpectedAndActual()
    {
        using var repo = new TestRepo("test-failure");
        Assert.Equal(1, repo.Run("test", "--project", P));
        Assert.Contains("erwartet TRUE, ist FALSE", repo.Output);
        Report(repo, "test-failure");
    }

    [Fact]
    public void LicenseMissing_IsEnvironment_Exit3()
    {
        using var repo = new TestRepo("license-missing");
        Assert.Equal(3, repo.Run("doctor", "--project", P));
        Assert.Contains("[LICENSE_MISSING]", repo.Output);
        Assert.Contains("Behebung:", repo.Output);
        Assert.Equal(3, repo.Run("compile", "--project", P));
        Report(repo, "license-missing");
    }

    [Fact]
    public void Hang_IsKilledAfterTimeout_Exit4()
    {
        using var repo = new TestRepo("hang");
        var watch = Stopwatch.StartNew();
        Assert.Equal(4, repo.Run("compile", "--project", P, "--timeout", "0.02"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
        Assert.Contains("[ADAPTER_TIMEOUT]", repo.Output);

        var pids = PidLedger.Load(Path.Combine(repo.StateDir, "pids.json"));
        Assert.DoesNotContain(pids.Processes, p => ProcessProbe.IsSameProcessRunning(p.Pid, p.StartTime));
        AssertSyntheticResponse(repo, "compile", "ADAPTER_TIMEOUT");
    }

    [Theory]
    [InlineData("crash", "ADAPTER_CRASHED")]
    [InlineData("no-response", "ADAPTER_NO_RESPONSE")]
    [InlineData("invalid-response", "ADAPTER_INVALID_RESPONSE")]
    [InlineData("bad-protocol", "PROTOCOL_UNSUPPORTED")]
    public void BrokenAdapter_IsAbort_Exit4(string scenario, string code)
    {
        using var repo = new TestRepo(scenario);
        Assert.Equal(4, repo.Run("compile", "--project", P));
        Assert.Contains($"[{code}]", repo.Output);
        AssertSyntheticResponse(repo, "compile", code);

        // report still works and shows the abort as <error>, not as a finding
        Assert.Equal(0, repo.Run("report", "--project", P, "--junit", "out/junit"));
        var junit = repo.ReadFile("out/junit/linie-1_plc.xml");
        Assert.Contains("errors=\"1\"", junit);
        Assert.Contains("failures=\"0\"", junit);
    }

    [Fact]
    public void Doctor_IgnoresRuntimeScenarios()
    {
        using var repo = new TestRepo("hang");
        Assert.Equal(0, repo.Run("doctor", "--timeout", "0.5"));
    }

    [Fact]
    public void StaleResponse_IsNeverReused()
    {
        using var repo = new TestRepo("ok");
        Assert.Equal(0, repo.Run("compile", "--project", P));
        repo.SetEnv("PLC_BUILD_FAKE_SCENARIO", "no-response");
        Assert.Equal(4, repo.Run("compile", "--project", P));
    }

    [Fact]
    public void ScenarioStep_LimitsScenarioToOneStep()
    {
        using var repo = new TestRepo("crash");
        repo.SetEnv("PLC_BUILD_FAKE_STEP", "test");
        Assert.Equal(0, repo.Run("compile", "--project", P));
        Assert.Equal(4, repo.Run("test", "--project", P));
    }

    [Fact]
    public void Run_ExecutesAllStepsInOneSession()
    {
        using var repo = new TestRepo("ok");
        Assert.Equal(0, repo.Run("run", "--project", P, "--steps", "restore,import,compile,analyze,test"));
        foreach (var step in new[] { "restore", "import", "compile", "analyze", "test" })
        {
            Assert.True(File.Exists(Path.Combine(repo.StateDir, $"response-{step}.json")), step);
        }
        Assert.Single(Directory.GetFiles(repo.StateDir, "request-*.json"));
    }

    [Fact]
    public void Run_StopsAfterFirstFinding()
    {
        using var repo = new TestRepo("compile-error");
        Assert.Equal(1, repo.Run("run", "--project", P, "--steps", "restore,import,compile,analyze,test"));
        Assert.True(File.Exists(Path.Combine(repo.StateDir, "response-compile.json")));
        Assert.False(File.Exists(Path.Combine(repo.StateDir, "response-analyze.json")));
        Assert.Contains("analyze linie-1/plc: übersprungen", repo.Output);
    }

    [Fact]
    public void Run_AdapterDiesMidSession_Exit4_KeepsEarlierResults()
    {
        using var repo = new TestRepo("crash");
        repo.SetEnv("PLC_BUILD_FAKE_STEP", "compile");
        Assert.Equal(4, repo.Run("run", "--project", P, "--steps", "restore,compile,test"));
        Assert.Contains("restore linie-1/plc: ok", repo.Output);
        Assert.Contains("[ADAPTER_CRASHED]", repo.Output);
        Assert.Contains("test linie-1/plc: übersprungen", repo.Output);
    }

    [Theory]
    [InlineData("doctor,compile")]
    [InlineData("compile,compile")]
    [InlineData("archive")]
    public void Run_RejectsInvalidSteps_Exit2(string steps)
    {
        using var repo = new TestRepo("ok");
        Assert.Equal(2, repo.Run("run", "--project", P, "--steps", steps));
        Assert.Contains("[STEP_UNKNOWN]", repo.Output);
    }

    [Fact]
    public void Archive_ArtifactWithHashInManifest()
    {
        using var repo = new TestRepo("ok");
        Assert.Equal(0, repo.Run("archive", "--project", P, "--out", "out/Linie1_7.zapfake"));
        Assert.Equal(0, repo.Run("report", "--project", P, "--junit", "out/junit"));
        var manifest = repo.ReadFile("out/manifest/linie-1_plc.json");
        Assert.Contains("\"path\": \"out/Linie1_7.zapfake\"", manifest);
        Assert.Contains(Hashing.FileSha256(Path.Combine(repo.Root, "out/Linie1_7.zapfake")), manifest);
    }

    [Fact]
    public void Cleanup_KillsReportedHelperProcess()
    {
        using var repo = new TestRepo("ok", "spawnHelper: true");
        Assert.Equal(0, repo.Run("restore", "--project", P));
        var helper = PidLedger.Load(Path.Combine(repo.StateDir, "pids.json")).Processes.Single(p => p.Origin == "reported");
        Assert.True(ProcessProbe.IsSameProcessRunning(helper.Pid, helper.StartTime));

        Assert.Equal(0, repo.Run("cleanup", "--project", P));
        Assert.Contains("1 Prozess(e) beendet", repo.Output);
        Assert.False(ProcessProbe.IsSameProcessRunning(helper.Pid, helper.StartTime));
    }

    [Fact]
    public void Cleanup_LeavesProcessWithOtherStartTimeAlone()
    {
        using var repo = new TestRepo("ok");
        using var other = Process.Start(new ProcessStartInfo(Path.Combine(TestRepo.FakeAdapterDir, "PlcBuild.Adapters.Fake"), ["--linger", "60"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            Environment = { ["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..")) },
        })!;
        try
        {
            var pidsPath = Path.Combine(repo.StateDir, "pids.json");
            Json.WriteFile(pidsPath, new PidFile
            {
                Processes = [new PidEntry { Pid = other.Id, Name = "reused", StartTime = TestRepo.Now, Step = "compile", Origin = "reported" }],
            });
            Assert.Equal(0, repo.Run("cleanup"));
            Assert.False(other.HasExited);
        }
        finally
        {
            other.Kill();
        }
    }

    private static void AssertSyntheticResponse(TestRepo repo, string step, string code)
    {
        var response = File.ReadAllText(Path.Combine(repo.StateDir, $"response-{step}.json"));
        Assert.Contains("\"generatedBy\": \"plc-build\"", response);
        Assert.Contains($"\"code\": \"{code}\"", response);
        Assert.Contains("\"outcome\": \"error\"", response);
    }
}
