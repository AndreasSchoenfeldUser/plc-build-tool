using PlcBuild.Core.Config;

namespace PlcBuild.Core.Tests;

public class ConfigTests
{
    private const string Path = "/repo/plc.yml";

    private static PlcBuildException Invalid(string yaml) =>
        Assert.Throws<PlcBuildException>(() => PlcConfigLoader.Parse(yaml, Path));

    [Fact]
    public void Parses_ExampleFromClaudeMd_AndToleratesUnknownFields()
    {
        var config = PlcConfigLoader.Parse("""
            version: 1
            bridge: { remote: origin }
            projects:
              - id: linie-1/plc
                adapter: tia-v21
                path: linie-1/plc
                owner: team-a
                tia:
                  baseArchive: base/Linie1.zap21      # Git LFS
                  sources: src/plc
                  plc: PLC_1
                  umac: false
                  retries: 3
                  quoted: "false"
              - id: verpackung/plc
                adapter: codesys
                path: verpackung/plc
                codesys:
                  exe: "C:\\Program Files\\CODESYS 3.5.22.1\\CODESYS\\Common\\CODESYS.exe"
                  profile: "CODESYS V3.5 SP22 Patch 1"
            """, Path);

        Assert.Equal(2, config.Projects.Count);
        var tia = config.GetProject("linie-1/plc");
        Assert.Equal("tia-v21", tia.Adapter);
        var section = tia.GetSection("tia");
        Assert.Equal("base/Linie1.zap21", section["baseArchive"]);
        Assert.Equal(false, section["umac"]);
        Assert.Equal(3L, section["retries"]);
        Assert.Equal("false", section["quoted"]);
        Assert.Equal(@"C:\Program Files\CODESYS 3.5.22.1\CODESYS\Common\CODESYS.exe",
            config.GetProject("verpackung/plc").GetSection("codesys")["exe"]);
        Assert.Empty(tia.GetSection("codesys"));
    }

    [Theory]
    [InlineData("projects: []", "version")]
    [InlineData("version: 2\nprojects: [{id: a, adapter: fake, path: a}]", "version: 2")]
    [InlineData("version: 1", "projects")]
    [InlineData("version: 1\nprojects: [{adapter: fake, path: a}]", "„id“")]
    [InlineData("version: 1\nprojects: [{id: a, path: a}]", "„adapter“")]
    [InlineData("version: 1\nprojects: [{id: a, adapter: fake}]", "„path“")]
    [InlineData("version: 1\nprojects: [{id: a, adapter: fake, path: /abs}]", "relativ")]
    [InlineData("version: 1\nprojects: [{id: a, adapter: fake, path: a}, {id: a, adapter: fake, path: b}]", "mehrfach")]
    [InlineData("version: 1\nprojects: [{id: '../x', adapter: fake, path: a}]", "id darf")]
    [InlineData("version: 1\nprojects: [ {id: a", "YAML-Fehler")]
    [InlineData("", "leer")]
    public void Invalid_Config_IsUsageError(string yaml, string expected)
    {
        var ex = Invalid(yaml);
        Assert.Equal("CONFIG_INVALID", ex.Code);
        Assert.Equal(ExitCodes.Usage, ex.ExitCode);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void UnknownProject_IsUsageError()
    {
        var config = PlcConfigLoader.Parse("version: 1\nprojects: [{id: a, adapter: fake, path: a}]", Path);
        var ex = Assert.Throws<PlcBuildException>(() => config.GetProject("b"));
        Assert.Equal("PROJECT_UNKNOWN", ex.Code);
        Assert.Equal(ExitCodes.Usage, ex.ExitCode);
    }
}

public class CliTests
{
    [Theory]
    [InlineData]
    [InlineData("frobnicate")]
    [InlineData("compile")]
    [InlineData("compile", "--project")]
    [InlineData("compile", "--project", "x", "--timeout", "0")]
    [InlineData("archive", "--project", "linie-1/plc")]
    [InlineData("report", "--project", "linie-1/plc")]
    public void InvalidInvocation_Exit2(params string[] args)
    {
        using var repo = new TestRepo();
        Assert.Equal(2, repo.Run(args));
        Assert.Contains("[USAGE]", repo.Output);
    }

    [Fact]
    public void Help_Exit0()
    {
        using var repo = new TestRepo();
        Assert.Equal(0, repo.Run("--help"));
        Assert.Contains("compile", repo.Output);
    }

    [Fact]
    public void MissingConfig_Exit2()
    {
        using var repo = new TestRepo();
        Assert.Equal(2, repo.Run("compile", "--project", "linie-1/plc", "--config", "gibtsnicht.yml"));
        Assert.Contains("[CONFIG_NOT_FOUND]", repo.Output);
    }

    [Fact]
    public void UnknownProject_Exit2()
    {
        using var repo = new TestRepo();
        Assert.Equal(2, repo.Run("compile", "--project", "linie-2/plc"));
        Assert.Contains("[PROJECT_UNKNOWN]", repo.Output);
    }

    [Fact]
    public void MissingInputFile_Exit2()
    {
        using var repo = new TestRepo();
        File.Delete(System.IO.Path.Combine(repo.Root, "linie-1/plc/base/Linie1.zapfake"));
        Assert.Equal(2, repo.Run("restore", "--project", "linie-1/plc"));
        Assert.Contains("[INPUT_NOT_FOUND]", repo.Output);
    }

    [Fact]
    public void MissingProjectDirectory_Exit2()
    {
        using var repo = new TestRepo();
        Directory.Delete(System.IO.Path.Combine(repo.Root, "linie-1"), recursive: true);
        Assert.Equal(2, repo.Run("compile", "--project", "linie-1/plc"));
        Assert.Contains("[PROJECT_PATH_NOT_FOUND]", repo.Output);
    }

    [Fact]
    public void AdapterNotInstalled_IsEnvironment_Exit3()
    {
        using var repo = new TestRepo();
        repo.SetEnv("PLC_BUILD_ADAPTER_FAKE", null);
        Assert.Equal(3, repo.Run("compile", "--project", "linie-1/plc"));
        Assert.Contains("[ADAPTER_NOT_FOUND]", repo.Output);
        Assert.Contains("PLC_BUILD_ADAPTER_FAKE", repo.Output);
    }

    [Fact]
    public void Doctor_ReportsEveryProject_WorstExitCodeWins()
    {
        using var repo = new TestRepo();
        repo.WriteConfig("""
            version: 1
            projects:
              - id: gut
                adapter: fake
                path: linie-1/plc
              - id: ohne-adapter
                adapter: gibtsnicht
                path: linie-1/plc
            """);
        Assert.Equal(3, repo.Run("doctor"));
        Assert.Contains("doctor gut: ok", repo.Output);
        Assert.Contains("[ADAPTER_NOT_FOUND]", repo.Output);
    }

    [Fact]
    public void Report_WithoutResults_StillWritesFiles()
    {
        using var repo = new TestRepo();
        Assert.Equal(0, repo.Run("report", "--project", "linie-1/plc", "--junit", "out/junit", "--sarif", "out/sarif"));
        Assert.True(File.Exists(System.IO.Path.Combine(repo.Root, "out/junit/linie-1_plc.xml")));
        Assert.True(File.Exists(System.IO.Path.Combine(repo.Root, "out/manifest/linie-1_plc.json")));
        Assert.Contains("[NO_RESULTS]", repo.Output);
    }

    [Fact]
    public void Request_ContainsNoSecretValues()
    {
        using var repo = new TestRepo();
        repo.SetEnv("PLC_BUILD_UMAC_PASSWORD", "geheim-123");
        Assert.Equal(0, repo.Run("compile", "--project", "linie-1/plc"));
        foreach (var file in Directory.GetFiles(repo.StateDir))
        {
            Assert.DoesNotContain("geheim-123", File.ReadAllText(file));
        }
    }
}
