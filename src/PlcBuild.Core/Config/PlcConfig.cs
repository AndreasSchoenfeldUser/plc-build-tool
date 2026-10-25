using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PlcBuild.Core.Config;

public sealed class PlcConfig
{
    public required string ConfigPath { get; init; }
    /// <summary>Directory containing plc.yml. Project paths are relative to it.</summary>
    public required string RepositoryRoot { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<ProjectConfig> Projects { get; init; }

    public ProjectConfig GetProject(string id)
    {
        var project = Projects.FirstOrDefault(p => p.Id == id);
        if (project is null)
        {
            var known = string.Join(", ", Projects.Select(p => p.Id));
            throw PlcBuildException.Usage("PROJECT_UNKNOWN",
                $"Projekt „{id}“ ist in {ConfigPath} nicht definiert. Bekannte Projekte: {known}.");
        }
        return project;
    }
}

public sealed class ProjectConfig
{
    public required string Id { get; init; }
    public required string Adapter { get; init; }
    public required string Path { get; init; }
    public required string Root { get; init; }
    /// <summary>All further keys of the project entry, e.g. "tia" or "codesys", converted to plain values.</summary>
    public required IReadOnlyDictionary<string, object?> Sections { get; init; }

    public Dictionary<string, object?> GetSection(string? key)
    {
        if (key is not null && Sections.TryGetValue(key, out var value) && value is Dictionary<string, object?> map)
        {
            return map;
        }
        return new Dictionary<string, object?>();
    }
}

/// <summary>
/// Loads plc.yml. Unknown fields are tolerated on purpose: the PLC Git Bridge reads the same file
/// (CLAUDE.md section 10). Adapter-specific sections are passed through without interpretation.
/// </summary>
public static partial class PlcConfigLoader
{
    public const string DefaultFileName = "plc.yml";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex IdPattern();

    public static PlcConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw PlcBuildException.Usage("CONFIG_NOT_FOUND",
                $"Konfigurationsdatei {path} nicht gefunden. Mit --config den Pfad angeben oder plc-build im Repository-Wurzelverzeichnis aufrufen.");
        }
        return Parse(File.ReadAllText(path), Path.GetFullPath(path));
    }

    public static PlcConfig Parse(string yaml, string configPath)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw Invalid(configPath, $"YAML-Fehler in Zeile {ex.Start.Line}, Spalte {ex.Start.Column}: {ex.Message}");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw Invalid(configPath, "Die Datei ist leer oder kein YAML-Objekt.");
        }

        var versionNode = Get(root, "version");
        if (versionNode is not YamlScalarNode { Value: { } versionText }
            || !int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            throw Invalid(configPath, "Feld „version“ fehlt oder ist keine Zahl. Erwartet: version: 1");
        }
        if (version != 1)
        {
            throw Invalid(configPath, $"version: {version} wird nicht unterstützt. Diese plc-build-Version kennt version: 1.");
        }

        if (Get(root, "projects") is not YamlSequenceNode projectNodes || projectNodes.Children.Count == 0)
        {
            throw Invalid(configPath, "Liste „projects“ fehlt oder ist leer.");
        }

        var repoRoot = Path.GetDirectoryName(configPath)!;
        var projects = new List<ProjectConfig>();
        var index = 0;
        foreach (var node in projectNodes.Children)
        {
            index++;
            if (node is not YamlMappingNode map)
            {
                throw Invalid(configPath, $"projects[{index}] ist kein Objekt.");
            }
            var id = RequireString(map, "id", configPath, $"projects[{index}]");
            var where = $"Projekt „{id}“";
            if (!IdPattern().IsMatch(id) || id.Contains("..", StringComparison.Ordinal))
            {
                throw Invalid(configPath, $"{where}: id darf nur Buchstaben, Ziffern und . _ / - enthalten.");
            }
            if (projects.Any(p => p.Id == id))
            {
                throw Invalid(configPath, $"{where} ist mehrfach definiert.");
            }
            var adapter = RequireString(map, "adapter", configPath, where);
            var relPath = RequireString(map, "path", configPath, where);
            if (Path.IsPathRooted(relPath))
            {
                throw Invalid(configPath, $"{where}: path muss relativ zum Verzeichnis von plc.yml sein, ist aber „{relPath}“.");
            }

            var sections = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (keyNode, valueNode) in map.Children)
            {
                var key = ((YamlScalarNode)keyNode).Value ?? "";
                if (key is "id" or "adapter" or "path")
                {
                    continue;
                }
                sections[key] = ToValue(valueNode);
            }

            projects.Add(new ProjectConfig
            {
                Id = id,
                Adapter = adapter,
                Path = relPath,
                Root = Path.GetFullPath(Path.Combine(repoRoot, relPath)),
                Sections = sections,
            });
        }

        return new PlcConfig { ConfigPath = configPath, RepositoryRoot = repoRoot, Version = version, Projects = projects };
    }

    private static PlcBuildException Invalid(string configPath, string detail) =>
        PlcBuildException.Usage("CONFIG_INVALID", $"{configPath}: {detail}");

    private static YamlNode? Get(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static string RequireString(YamlMappingNode map, string key, string configPath, string where)
    {
        if (Get(map, key) is YamlScalarNode { Value: { Length: > 0 } value })
        {
            return value;
        }
        throw Invalid(configPath, $"{where}: Pflichtfeld „{key}“ fehlt.");
    }

    /// <summary>Converts YAML to plain values. Plain scalars become bool, long, double or null; quoted ones stay strings.</summary>
    internal static object? ToValue(YamlNode node) => node switch
    {
        YamlMappingNode map => map.Children.ToDictionary(
            kv => ((YamlScalarNode)kv.Key).Value ?? "",
            kv => ToValue(kv.Value),
            StringComparer.Ordinal),
        YamlSequenceNode seq => seq.Children.Select(ToValue).ToList(),
        YamlScalarNode scalar => ScalarValue(scalar),
        _ => null,
    };

    private static object? ScalarValue(YamlScalarNode scalar)
    {
        var text = scalar.Value;
        if (scalar.Style != ScalarStyle.Plain || text is null)
        {
            return text;
        }
        switch (text)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return true;
            case "false" or "False" or "FALSE":
                return false;
        }
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return text;
    }
}
