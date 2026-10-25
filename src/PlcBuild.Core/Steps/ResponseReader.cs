using System.Text.Json;
using PlcBuild.Protocol;

namespace PlcBuild.Core.Steps;

public sealed record ResponseProblem(string Code, string Message);

public static class ResponseReader
{
    private static readonly HashSet<string> KnownOutcomes =
        [Outcomes.Ok, Outcomes.Failed, Outcomes.Environment, Outcomes.Error];

    /// <summary>Reads and validates a response. Returns either a response or the reason it is unusable.</summary>
    public static (AdapterResponse? Response, ResponseProblem? Problem) Read(string path, string expectedStep)
    {
        if (!File.Exists(path))
        {
            return (null, new ResponseProblem("ADAPTER_NO_RESPONSE", $"Der Adapter hat keine Antwort geschrieben ({Path.GetFileName(path)} fehlt)."));
        }

        int protocol;
        AdapterResponse? response;
        try
        {
            var text = File.ReadAllText(path);
            using (var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
            {
                protocol = doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("protocol", out var p)
                    && p.TryGetInt32(out var v) ? v : -1;
            }
            // Check the version before mapping the rest: a future protocol may change field types.
            if (protocol != ProtocolVersion.Current)
            {
                return (null, new ResponseProblem("PROTOCOL_UNSUPPORTED",
                    $"Antwort mit Protokollversion {(protocol < 0 ? "ohne Angabe" : protocol)}; plc-build versteht Version {ProtocolVersion.Current}. Passende Versionen von plc-build und Adapter installieren."));
            }
            response = Json.Deserialize<AdapterResponse>(text);
        }
        catch (JsonException ex)
        {
            return (null, new ResponseProblem("ADAPTER_INVALID_RESPONSE", $"Antwort ist kein gültiges JSON: {ex.Message}"));
        }

        if (response is null)
        {
            return (null, new ResponseProblem("ADAPTER_INVALID_RESPONSE", "Antwort ist leer."));
        }
        if (response.Step != expectedStep)
        {
            return (null, new ResponseProblem("ADAPTER_INVALID_RESPONSE", $"Antwort gehört zu Schritt „{response.Step}“, erwartet „{expectedStep}“."));
        }
        if (!KnownOutcomes.Contains(response.Outcome))
        {
            return (null, new ResponseProblem("ADAPTER_INVALID_RESPONSE", $"Unbekanntes Ergebnis „{response.Outcome}“."));
        }

        // Tolerate explicit nulls for lists.
        response.Diagnostics ??= [];
        response.Tests ??= [];
        response.Artifacts ??= [];
        response.Processes ??= [];
        response.EnvironmentIssues ??= [];
        return (response, null);
    }

    private static string ShortText(string code) => code switch
    {
        "ADAPTER_TIMEOUT" => "Zeitlimit überschritten",
        "ADAPTER_CRASHED" => "Adapter abgestürzt",
        "ADAPTER_NO_RESPONSE" => "keine Antwort vom Adapter",
        "ADAPTER_INVALID_RESPONSE" => "ungültige Antwort vom Adapter",
        "ADAPTER_START_FAILED" => "Adapter ließ sich nicht starten",
        "PROTOCOL_UNSUPPORTED" => "unbekannte Protokollversion",
        _ => "Adapter-Fehler",
    };

    /// <summary>A response written by the core when the adapter did not deliver one. Always outcome "error" (exit 4).</summary>
    public static AdapterResponse Synthetic(string step, string code, string message, long durationMs) => new()
    {
        Step = step,
        Outcome = Outcomes.Error,
        Summary = $"[{code}] {ShortText(code)}",
        DurationMs = durationMs,
        GeneratedBy = "plc-build",
        Diagnostics =
        [
            new Diagnostic { Severity = Severities.Error, Source = "plc-build", Code = code, Message = message },
        ],
    };
}
