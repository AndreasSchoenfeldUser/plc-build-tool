using System.Globalization;
using System.Text;
using System.Xml;
using PlcBuild.Core.Steps;
using PlcBuild.Protocol;

namespace PlcBuild.Core.Reporting;

/// <summary>
/// JUnit XML as read by the Jenkins JUnit plugin, GitLab and Azure DevOps:
/// testsuites → one testsuite per step → testcase (CLAUDE.md section 6).
/// </summary>
public static class JUnitWriter
{
    private sealed record Case(string Name, string ClassName, long DurationMs, string? Kind, string? Message, string? Body);

    public static string Write(ProjectResults results)
    {
        var suites = results.Steps.Select(s => (s.Step, s.Response, Cases: CasesFor(results.ProjectId, s))).ToList();

        var sb = new StringBuilder();
        using (var xml = XmlWriter.Create(new StringWriter(sb), new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            Encoding = Files.Utf8NoBom,
            OmitXmlDeclaration = true,
        }))
        {
            xml.WriteStartElement("testsuites");
            xml.WriteAttributeString("name", $"plc-build {results.ProjectId}");
            WriteCounts(xml, suites.SelectMany(s => s.Cases).ToList(), suites.Sum(s => s.Response.DurationMs));

            foreach (var (step, response, cases) in suites)
            {
                xml.WriteStartElement("testsuite");
                xml.WriteAttributeString("name", $"{results.ProjectId}.{step}");
                WriteCounts(xml, cases, response.DurationMs);
                foreach (var c in cases)
                {
                    xml.WriteStartElement("testcase");
                    xml.WriteAttributeString("classname", c.ClassName);
                    xml.WriteAttributeString("name", c.Name);
                    xml.WriteAttributeString("time", Seconds(c.DurationMs));
                    if (c.Kind is not null)
                    {
                        xml.WriteStartElement(c.Kind);
                        if (c.Message is not null)
                        {
                            xml.WriteAttributeString("message", c.Message);
                        }
                        if (c.Body is not null)
                        {
                            xml.WriteString(c.Body);
                        }
                        xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                }
                var warnings = response.Diagnostics.Where(d => d.Severity == Severities.Warning).ToList();
                if (warnings.Count > 0)
                {
                    xml.WriteElementString("system-out", string.Join("\n", Ordering.Diagnostics(warnings).Select(Line)));
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + sb + "\n";
    }

    private static void WriteCounts(XmlWriter xml, IReadOnlyList<Case> cases, long durationMs)
    {
        xml.WriteAttributeString("tests", Count(cases.Count));
        xml.WriteAttributeString("failures", Count(cases.Count(c => c.Kind == "failure")));
        xml.WriteAttributeString("errors", Count(cases.Count(c => c.Kind == "error")));
        xml.WriteAttributeString("skipped", Count(cases.Count(c => c.Kind == "skipped")));
        xml.WriteAttributeString("time", Seconds(durationMs));
    }

    private static IReadOnlyList<Case> CasesFor(string projectId, StepResult result)
    {
        var (step, r) = (result.Step, result.Response);
        var className = $"{projectId}.{step}";

        // Environment problems and adapter errors are not findings: one <error> case for the whole step.
        if (r.Outcome is Outcomes.Environment or Outcomes.Error)
        {
            var body = new List<string>();
            body.AddRange(r.EnvironmentIssues.Select(i => $"[{i.Code}] {i.Message}" + (i.Remedy is null ? "" : $"\nBehebung: {i.Remedy}")));
            body.AddRange(Ordering.Diagnostics(r.Diagnostics).Where(d => d.Severity == Severities.Error).Select(Line));
            return [new Case($"{step} {projectId}", className, r.DurationMs, "error", r.Summary ?? r.Outcome, string.Join("\n", body))];
        }

        return step switch
        {
            StepNames.Compile => DiagnosticCases(projectId, step, className, r, groupByCode: false),
            StepNames.Analyze => DiagnosticCases(projectId, step, className, r, groupByCode: true),
            StepNames.Test => TestCases(projectId, className, r),
            _ => [StepCase(projectId, step, className, r)],
        };
    }

    /// <summary>Compile: one case per object with errors. Analyze: one case per violated rule and object.</summary>
    private static IReadOnlyList<Case> DiagnosticCases(string projectId, string step, string className, AdapterResponse r, bool groupByCode)
    {
        var errors = r.Diagnostics.Where(d => d.Severity == Severities.Error).ToList();
        if (errors.Count == 0)
        {
            return [StepCase(projectId, step, className, r)];
        }
        return errors
            .GroupBy(d => (Object: d.Object ?? projectId, Code: groupByCode ? d.Code ?? "" : ""))
            .OrderBy(g => g.Key.Code, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Object, StringComparer.Ordinal)
            .Select(g =>
            {
                var ordered = Ordering.Diagnostics(g).ToList();
                var name = groupByCode && g.Key.Code.Length > 0 ? $"{g.Key.Code} {g.Key.Object}" : g.Key.Object;
                return new Case(name, className, 0, "failure", ordered[0].Message, string.Join("\n", ordered.Select(Line)));
            })
            .ToList();
    }

    private static IReadOnlyList<Case> TestCases(string projectId, string className, AdapterResponse r)
    {
        if (r.Tests.Count == 0)
        {
            return [StepCase(projectId, StepNames.Test, className, r)];
        }
        return Ordering.Tests(r.Tests).Select(t =>
        {
            var testClass = string.IsNullOrEmpty(t.Suite) ? className : $"{className}.{t.Suite}";
            return t.Outcome switch
            {
                TestOutcomes.Passed => new Case(t.Name, testClass, t.DurationMs, null, null, null),
                TestOutcomes.Skipped => new Case(t.Name, testClass, t.DurationMs, "skipped", t.Message, null),
                TestOutcomes.Failed => new Case(t.Name, testClass, t.DurationMs, "failure", t.Message ?? "Test fehlgeschlagen", TestBody(t)),
                _ => new Case(t.Name, testClass, t.DurationMs, "error", t.Message ?? "Testfehler", TestBody(t)),
            };
        }).ToList();
    }

    private static string? TestBody(TestResult t)
    {
        var lines = new List<string>();
        if (t.Message is not null) lines.Add(t.Message);
        if (t.Expected is not null) lines.Add($"Erwartet: {t.Expected}");
        if (t.Actual is not null) lines.Add($"Ist: {t.Actual}");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>A single case for the whole step: green when ok, failure otherwise.</summary>
    private static Case StepCase(string projectId, string step, string className, AdapterResponse r) =>
        r.Outcome == Outcomes.Ok
            ? new Case($"{step} {projectId}", className, r.DurationMs, null, null, null)
            : new Case($"{step} {projectId}", className, r.DurationMs, "failure", r.Summary ?? r.Outcome,
                string.Join("\n", Ordering.Diagnostics(r.Diagnostics).Select(Line)));

    private static string Line(Diagnostic d)
    {
        var location = d.Location is null ? "" : FormatLocation(d.Location);
        var code = d.Code is null ? "" : $"[{d.Code}] ";
        var obj = d.Object is null ? "" : $"{d.Object}{location}: ";
        return $"{d.Severity}: {code}{obj}{d.Message}";
    }

    private static string FormatLocation(DiagnosticLocation l)
    {
        var parts = new List<string>();
        if (l.File is not null) parts.Add(l.File);
        if (l.Network is not null) parts.Add($"Netzwerk {l.Network}");
        if (l.Line is not null) parts.Add($"Zeile {l.Line}");
        if (l.Column is not null) parts.Add($"Spalte {l.Column}");
        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);
    private static string Seconds(long ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
}
