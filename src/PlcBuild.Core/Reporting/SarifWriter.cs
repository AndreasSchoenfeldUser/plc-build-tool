using System.Text.Json;
using System.Text.Json.Nodes;
using PlcBuild.Core.Steps;
using PlcBuild.Protocol;

namespace PlcBuild.Core.Reporting;

/// <summary>
/// SARIF 2.1.0 for compile and analyze findings. If the adapter delivered its own SARIF file
/// (artifact kind "sarif", e.g. CODESYS Static Analysis), its runs are taken over and only get
/// plc-build metadata in <c>properties.plcBuild</c>.
/// </summary>
public static class SarifWriter
{
    public const string ArtifactKind = "sarif";
    private static readonly string[] SarifSteps = [StepNames.Compile, StepNames.Analyze];

    public static string Write(ProjectResults results, string adapter, string projectRoot, string toolVersion, ICollection<string> problems)
    {
        var runs = new JsonArray();
        foreach (var result in results.Steps.Where(s => SarifSteps.Contains(s.Step)))
        {
            var r = result.Response;
            if (r.Outcome is not (Outcomes.Ok or Outcomes.Failed))
            {
                continue; // no findings when the tool did not run properly
            }
            var metadata = Metadata(results.ProjectId, result.Step, adapter, r.Outcome, toolVersion);

            var external = r.Artifacts.Where(a => a.Kind == ArtifactKind).ToList();
            if (external.Count > 0 && TryTakeOver(external, projectRoot, metadata, runs, problems))
            {
                continue;
            }
            runs.Add(BuildRun(results.ProjectId, result.Step, r, metadata));
        }

        var log = new JsonObject
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = runs,
        };
        return log.ToJsonString(Json.Options) + "\n";
    }

    private static JsonObject Metadata(string projectId, string step, string adapter, string outcome, string toolVersion) => new()
    {
        ["project"] = projectId,
        ["step"] = step,
        ["adapter"] = adapter,
        ["outcome"] = outcome,
        ["generator"] = $"plc-build {toolVersion}",
    };

    private static bool TryTakeOver(List<Artifact> files, string projectRoot, JsonObject metadata, JsonArray runs, ICollection<string> problems)
    {
        var taken = new List<JsonNode>();
        foreach (var file in files)
        {
            var path = Path.GetFullPath(file.Path, projectRoot);
            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(path));
                if (doc?["runs"] is not JsonArray fileRuns)
                {
                    problems.Add($"[SARIF_INVALID] {path}: kein Feld „runs“, Befunde werden aus der Antwort erzeugt.");
                    return false;
                }
                foreach (var run in fileRuns.ToList())
                {
                    fileRuns.Remove(run);
                    if (run is JsonObject runObject)
                    {
                        var properties = runObject["properties"] as JsonObject ?? new JsonObject();
                        properties["plcBuild"] = metadata.DeepClone();
                        runObject["properties"] = properties;
                        taken.Add(runObject);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                problems.Add($"[SARIF_INVALID] {path}: {ex.Message}. Befunde werden aus der Antwort erzeugt.");
                return false;
            }
        }
        foreach (var run in taken)
        {
            runs.Add(run);
        }
        return true;
    }

    private static JsonObject BuildRun(string projectId, string step, AdapterResponse r, JsonObject metadata)
    {
        var diagnostics = Ordering.Diagnostics(r.Diagnostics).ToList();
        var rules = new JsonArray();
        foreach (var id in diagnostics.Select(d => RuleId(step, d)).Distinct().Order(StringComparer.Ordinal))
        {
            rules.Add(new JsonObject { ["id"] = id });
        }

        var results = new JsonArray();
        foreach (var d in diagnostics)
        {
            var location = new JsonObject();
            if (d.Location?.File is { } file)
            {
                var physical = new JsonObject { ["artifactLocation"] = new JsonObject { ["uri"] = file.Replace('\\', '/') } };
                if (d.Location.Line is { } line)
                {
                    var region = new JsonObject { ["startLine"] = line };
                    if (d.Location.Column is { } column)
                    {
                        region["startColumn"] = column;
                    }
                    physical["region"] = region;
                }
                location["physicalLocation"] = physical;
            }
            location["logicalLocations"] = new JsonArray(new JsonObject
            {
                ["fullyQualifiedName"] = d.Object ?? projectId,
            });
            if (d.Location?.Network is { } network)
            {
                location["properties"] = new JsonObject { ["network"] = network };
            }

            results.Add(new JsonObject
            {
                ["ruleId"] = RuleId(step, d),
                ["level"] = d.Severity switch
                {
                    Severities.Error => "error",
                    Severities.Warning => "warning",
                    _ => "note",
                },
                ["message"] = new JsonObject { ["text"] = d.Message },
                ["locations"] = new JsonArray(location),
            });
        }

        var driver = new JsonObject { ["name"] = r.Tool?.Name ?? "unbekannt" };
        if (r.Tool?.Version is { } version)
        {
            driver["version"] = version;
        }
        driver["rules"] = rules;

        return new JsonObject
        {
            ["tool"] = new JsonObject { ["driver"] = driver },
            ["automationDetails"] = new JsonObject { ["id"] = $"{projectId}/{step}/" },
            ["results"] = results,
            ["properties"] = new JsonObject { ["plcBuild"] = metadata },
        };
    }

    private static string RuleId(string step, Diagnostic d) => d.Code ?? $"{step}/{d.Severity}";
}
