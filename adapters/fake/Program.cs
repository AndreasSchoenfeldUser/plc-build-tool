// Fake adapter for plc-build tests and demos. Speaks protocol v1 without any engineering software.
//
// Scenario: env PLC_BUILD_FAKE_SCENARIO, else setting "scenario" in the "fake" section of plc.yml, else "ok".
//   ok                everything green (compile and analyze report one warning each)
//   compile-error     compile → failed with errors on two objects
//   rule-violation    analyze → failed with violated rules
//   test-failure      test → failed, one red test case with expected and actual value
//   license-missing   every step incl. doctor → environment (LICENSE_MISSING)
//   hang              never answers (core must kill it after the time limit)
//   crash             exits with code 3 without a response
//   no-response       exits with code 0 without a response
//   invalid-response  writes a response that is not JSON
//   bad-protocol      answers with protocol version 99
// hang, crash, no-response, invalid-response and bad-protocol affect every step except doctor.
// Env PLC_BUILD_FAKE_STEP or setting "scenarioStep" limits the scenario to one step.
// Env PLC_BUILD_FAKE_SPAWN_HELPER=1 or setting "spawnHelper: true" starts a helper process that keeps
// running after the adapter exits and reports it in "processes", like FileStorage.Server.exe after TIA.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PlcBuild.Protocol;

var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

if (args.Length == 2 && args[0] == "--linger")
{
    Thread.Sleep(TimeSpan.FromSeconds(int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture)));
    return 0;
}
if (args.Length != 1)
{
    Console.Error.WriteLine("Aufruf: PlcBuild.Adapters.Fake <request.json>");
    return 2;
}

var request = JsonSerializer.Deserialize<AdapterRequest>(File.ReadAllText(args[0]), json)!;
var settings = request.Project.Settings;
var scenario = Env("PLC_BUILD_FAKE_SCENARIO") ?? Setting("scenario") ?? "ok";
var scenarioStep = Env("PLC_BUILD_FAKE_STEP") ?? Setting("scenarioStep");
var spawnHelper = Env("PLC_BUILD_FAKE_SPAWN_HELPER") == "1" || Setting("spawnHelper") == "true";

Console.WriteLine($"Fake-Adapter: Schritt {request.Step}, Szenario {scenario}, Projekt {request.Project.Id}");

foreach (var step in request.Steps)
{
    if (Affects(step))
    {
        switch (scenario)
        {
            case "hang":
                Console.WriteLine($"{step}: hänge absichtlich");
                Thread.Sleep(Timeout.Infinite);
                break;
            case "crash":
                Console.Error.WriteLine($"{step}: simulierter Absturz (System.AccessViolationException)");
                return 3;
            case "no-response":
                Console.WriteLine($"{step}: beende ohne Antwort");
                return 0;
            case "invalid-response":
                File.WriteAllText(ResponsePath(step), "<html>kein JSON</html>");
                return 0;
            case "bad-protocol":
                File.WriteAllText(ResponsePath(step), "{ \"protocol\": 99, \"step\": \"" + step + "\", \"outcome\": \"ok\" }");
                return 0;
        }
    }

    var response = Execute(step);
    if (spawnHelper)
    {
        response.Processes.Add(StartHelper());
    }
    File.WriteAllText(ResponsePath(step), JsonSerializer.Serialize(response, json));
    Console.WriteLine($"{step}: {response.Outcome}");
    if (response.Outcome != Outcomes.Ok)
    {
        break; // later steps of a run session are skipped
    }
}
return 0;

AdapterResponse Execute(string step)
{
    var r = new AdapterResponse
    {
        Step = step,
        Outcome = Outcomes.Ok,
        DurationMs = step switch
        {
            StepNames.Doctor => 50,
            StepNames.Restore => 4200,
            StepNames.Import => 1500,
            StepNames.Compile => 8100,
            StepNames.Analyze => 2300,
            StepNames.Test => 12000,
            StepNames.Archive => 3000,
            _ => 0,
        },
        Tool = new ToolInfo
        {
            Name = "Fake Engineering",
            Version = "1.0",
            Components = new Dictionary<string, string> { ["Fake Compiler"] = "1.0", ["Fake Test"] = "1.0" },
        },
    };

    if (scenario == "license-missing" && Affects(step))
    {
        r.Outcome = Outcomes.Environment;
        r.Summary = "Lizenz fehlt";
        r.EnvironmentIssues.Add(new EnvironmentIssue
        {
            Code = "LICENSE_MISSING",
            Message = "Keine gültige Lizenz für Fake Engineering Professional gefunden.",
            Remedy = "Lizenz im Automation License Manager auf diesem Agenten bereitstellen.",
        });
        return r;
    }

    switch (step)
    {
        case StepNames.Doctor:
            r.Summary = "Umgebung in Ordnung";
            break;

        case StepNames.Restore:
            Directory.CreateDirectory(request.Project.WorkDir);
            File.WriteAllText(Path.Combine(request.Project.WorkDir, "project.fake"), "restored\n");
            r.Summary = "Basisprojekt wiederhergestellt";
            break;

        case StepNames.Import:
            var sources = Path.Combine(request.Project.Root, Setting("sources") ?? "src");
            var count = Directory.Exists(sources) ? Directory.GetFiles(sources, "*", SearchOption.AllDirectories).Length : 0;
            r.Summary = $"{count} Quelldatei(en) importiert";
            break;

        case StepNames.Compile when scenario == "compile-error" && Affects(step):
            r.Outcome = Outcomes.Failed;
            r.Diagnostics.Add(Diag(Severities.Error, "compile", "PLC_1/Programmbausteine/Foerderband/FB_Foerderband", "Operand „#Freigabe“ ist nicht definiert."));
            r.Diagnostics.Add(Diag(Severities.Error, "compile", "PLC_1/Programmbausteine/Foerderband/FB_Foerderband", "Typkonflikt: INT kann nicht nach BOOL konvertiert werden."));
            r.Diagnostics.Add(Diag(Severities.Error, "compile", "PLC_1/Programmbausteine/Greifer/FB_Greifer", "Ungültiger Aufruf von „TON“: Parameter „PT“ fehlt."));
            r.Diagnostics.Add(Diag(Severities.Warning, "compile", "PLC_1/Programmbausteine/Main", "Variable „#tmpZaehler“ wird nicht verwendet."));
            r.Summary = "3 Fehler, 1 Warnung";
            break;

        case StepNames.Compile:
            r.Diagnostics.Add(Diag(Severities.Warning, "compile", "PLC_1/Programmbausteine/Main", "Variable „#tmpZaehler“ wird nicht verwendet."));
            r.Summary = "0 Fehler, 1 Warnung";
            break;

        case StepNames.Analyze when scenario == "rule-violation" && Affects(step):
            r.Outcome = Outcomes.Failed;
            r.Diagnostics.Add(Diag(Severities.Error, "analyze", "PLC_1/Programmbausteine/Foerderband/FB_Foerderband", "Bausteinname muss mit Präfix „FB_“ und Großbuchstaben beginnen.", "SG-NAMING-01"));
            r.Diagnostics.Add(Diag(Severities.Error, "analyze", "PLC_1/Programmbausteine/Greifer/FB_Greifer", "Bausteinname muss mit Präfix „FB_“ und Großbuchstaben beginnen.", "SG-NAMING-01"));
            r.Diagnostics.Add(Diag(Severities.Error, "analyze", "PLC_1/Programmbausteine/Greifer/FB_Greifer", "Baustein hat keinen Kommentar.", "SG-DOC-02"));
            r.Diagnostics.Add(Diag(Severities.Warning, "analyze", "PLC_1/Programmbausteine/Main", "Magische Zahl 42 im Code.", "SG-LEGACY-07"));
            r.Summary = "3 Regelverletzungen, 1 Warnung";
            break;

        case StepNames.Analyze:
            r.Diagnostics.Add(Diag(Severities.Warning, "analyze", "PLC_1/Programmbausteine/Main", "Magische Zahl 42 im Code.", "SG-LEGACY-07"));
            r.Summary = "0 Regelverletzungen, 1 Warnung";
            break;

        case StepNames.Test:
            var red = scenario == "test-failure" && Affects(step);
            r.Tests.Add(new TestResult { Suite = "FB_Foerderband", Name = "Test_Start_Stop", Outcome = TestOutcomes.Passed, DurationMs = 800 });
            r.Tests.Add(red
                ? new TestResult
                {
                    Suite = "FB_Greifer", Name = "Test_Schliessen_Timeout", Outcome = TestOutcomes.Failed, DurationMs = 1200,
                    Message = "Erwartet TRUE, ist FALSE", Expected = "TRUE", Actual = "FALSE",
                }
                : new TestResult { Suite = "FB_Greifer", Name = "Test_Schliessen_Timeout", Outcome = TestOutcomes.Passed, DurationMs = 1200 });
            r.Outcome = red ? Outcomes.Failed : Outcomes.Ok;
            r.Summary = red ? "2 Tests, 1 fehlgeschlagen" : "2 Tests, alle bestanden";
            break;

        case StepNames.Archive:
            if (!request.Options.TryGetValue("archiveFile", out var archiveFile))
            {
                r.Outcome = Outcomes.Error;
                r.Summary = "Option archiveFile fehlt im Request.";
                break;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(archiveFile)!);
            File.WriteAllText(archiveFile, $"FAKE-ARCHIVE\nproject={request.Project.Id}\n", new UTF8Encoding(false));
            r.Artifacts.Add(new Artifact { Kind = "archive", Path = archiveFile, Sha256 = Sha256(archiveFile) });
            r.Summary = $"Archiv {Path.GetFileName(archiveFile)} geschrieben";
            break;

        default:
            r.Outcome = Outcomes.Error;
            r.Summary = $"Schritt „{step}“ kennt der Fake-Adapter nicht.";
            break;
    }
    return r;
}

bool Affects(string step)
{
    if (scenarioStep is not null)
    {
        return step == scenarioStep;
    }
    return scenario switch
    {
        "compile-error" => step == StepNames.Compile,
        "rule-violation" => step == StepNames.Analyze,
        "test-failure" => step == StepNames.Test,
        "license-missing" => true,
        _ => step != StepNames.Doctor,
    };
}

ProcessInfo StartHelper()
{
    var self = Environment.ProcessPath!;
    var psi = new ProcessStartInfo(self, ["--linger", "600"])
    {
        UseShellExecute = false,
        // Own pipes so the helper does not keep the core's stdout/stderr pipes open.
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    var helper = Process.Start(psi)!;
    return new ProcessInfo
    {
        Pid = helper.Id,
        Name = "PlcBuild.Adapters.Fake.Helper",
        StartTime = new DateTimeOffset(helper.StartTime.ToUniversalTime(), TimeSpan.Zero),
    };
}

static Diagnostic Diag(string severity, string source, string obj, string message, string? code = null) =>
    new() { Severity = severity, Source = source, Object = obj, Message = message, Code = code };

static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

string ResponsePath(string step) => Path.Combine(request.OutDir, $"response-{step}.json");

string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

string? Setting(string key) => settings.TryGetValue(key, out var value) && value is JsonElement e
    ? e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => e.GetRawText(),
    }
    : null;
