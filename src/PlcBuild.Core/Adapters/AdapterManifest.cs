using System.Text.Json;
using System.Text.RegularExpressions;
using PlcBuild.Core.Config;

namespace PlcBuild.Core.Adapters;

/// <summary>
/// Contents of <c>adapter.json</c> next to each adapter (ADR 0002). Everything vendor-specific the core
/// needs to know lives here, so the core itself has no branches per engineering tool (H3).
/// </summary>
public sealed class AdapterManifest
{
    public string Name { get; set; } = "";
    public int Protocol { get; set; }
    /// <summary>Executable, may contain placeholders such as {adapterDir} or {settings.exe}.</summary>
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = [];
    /// <summary>Key of the project section in plc.yml that is passed as settings, e.g. "tia".</summary>
    public string? SettingsKey { get; set; }
    /// <summary>Settings keys that name input files or directories; hashed into the build manifest.</summary>
    public List<string> ManifestInputs { get; set; } = [];
    /// <summary>Names of environment variables with secrets, passed to the adapter by name only.</summary>
    public Dictionary<string, string> Secrets { get; set; } = [];
}

public sealed record LocatedAdapter(AdapterManifest Manifest, string Directory);

public static partial class AdapterLocator
{
    public const string ManifestFileName = "adapter.json";

    /// <summary>PLC_BUILD_ADAPTER_TIA_V21 for adapter "tia-v21".</summary>
    public static string OverrideVariable(string adapter) =>
        "PLC_BUILD_ADAPTER_" + NonAlnum().Replace(adapter.ToUpperInvariant(), "_");

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlnum();

    public static LocatedAdapter Locate(string adapter, RunContext ctx)
    {
        var variable = OverrideVariable(adapter);
        var dir = ctx.GetEnv(variable) is { } overridden
            ? ctx.ResolvePath(overridden)
            : Path.Combine(ctx.ToolDirectory, "adapters", adapter);

        var manifestPath = Path.Combine(dir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw PlcBuildException.Env("ADAPTER_NOT_FOUND",
                $"Adapter „{adapter}“ ist auf diesem Agenten nicht installiert: {manifestPath} fehlt. " +
                $"Adapter nach {Path.Combine(ctx.ToolDirectory, "adapters", adapter)} kopieren oder {variable} auf das Adapter-Verzeichnis setzen.");
        }

        AdapterManifest? manifest;
        try
        {
            manifest = Json.Deserialize<AdapterManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException ex)
        {
            throw PlcBuildException.Env("ADAPTER_MANIFEST_INVALID", $"{manifestPath} ist kein gültiges JSON: {ex.Message}");
        }
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Command))
        {
            throw PlcBuildException.Env("ADAPTER_MANIFEST_INVALID", $"{manifestPath}: Feld „command“ fehlt.");
        }
        if (manifest.Protocol != PlcBuild.Protocol.ProtocolVersion.Current)
        {
            throw PlcBuildException.Env("ADAPTER_PROTOCOL_MISMATCH",
                $"Adapter „{adapter}“ spricht Protokoll {manifest.Protocol}, plc-build erwartet {PlcBuild.Protocol.ProtocolVersion.Current}. Passende Versionen von plc-build und Adapter installieren.");
        }
        return new LocatedAdapter(manifest, Path.GetFullPath(dir));
    }
}

/// <summary>Expands placeholders in the adapter command line.</summary>
public static partial class CommandTemplate
{
    [GeneratedRegex(@"\{([A-Za-z0-9_.]+)\}")]
    private static partial Regex Placeholder();

    public static (string FileName, IReadOnlyList<string> Arguments) Expand(
        LocatedAdapter adapter, ProjectConfig project, IReadOnlyDictionary<string, object?> settings, string requestPath)
    {
        string Replace(string template) => Placeholder().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            switch (name)
            {
                case "adapterDir": return adapter.Directory;
                case "request": return requestPath;
                case "projectRoot": return project.Root;
            }
            if (name.StartsWith("settings.", StringComparison.Ordinal))
            {
                var key = name["settings.".Length..];
                if (settings.TryGetValue(key, out var value) && value is not null and not IDictionary<string, object?>)
                {
                    return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
                }
                throw PlcBuildException.Usage("CONFIG_INVALID",
                    $"Projekt „{project.Id}“: Adapter „{adapter.Manifest.Name}“ braucht das Feld „{adapter.Manifest.SettingsKey}.{key}“ in plc.yml.");
            }
            throw PlcBuildException.Env("ADAPTER_MANIFEST_INVALID",
                $"{Path.Combine(adapter.Directory, AdapterLocator.ManifestFileName)}: unbekannter Platzhalter {{{name}}}.");
        });

        return (Replace(adapter.Manifest.Command), adapter.Manifest.Args.Select(Replace).ToList());
    }
}
