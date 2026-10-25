using System;
using System.Collections.Generic;

namespace PlcBuild.Protocol;

// Adapter protocol v1 (CLAUDE.md section 5, ADR 0003).
// Plain POCOs without serializer attributes: the core uses System.Text.Json with camelCase,
// adapters may use any JSON library that maps camelCase names.

public static class ProtocolVersion
{
    public const int Current = 1;
}

public static class StepNames
{
    public const string Doctor = "doctor";
    public const string Restore = "restore";
    public const string Import = "import";
    public const string Compile = "compile";
    public const string Analyze = "analyze";
    public const string Test = "test";
    public const string Archive = "archive";
    public const string Run = "run";

    /// <summary>Fixed order used for reports and manifests.</summary>
    public static readonly IReadOnlyList<string> ReportOrder =
        new[] { Doctor, Restore, Import, Compile, Analyze, Test, Archive };

    /// <summary>Steps that may be combined in one adapter session via <c>run</c>.</summary>
    public static readonly IReadOnlyList<string> RunnableInSession =
        new[] { Restore, Import, Compile, Analyze, Test };
}

public static class Outcomes
{
    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string Environment = "environment";
    public const string Error = "error";
}

public static class Severities
{
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Info = "info";
}

public static class TestOutcomes
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Error = "error";
    public const string Skipped = "skipped";
}

public sealed class AdapterRequest
{
    public int Protocol { get; set; } = ProtocolVersion.Current;
    public string Step { get; set; } = "";
    /// <summary>Equals [Step] for single steps; the step list for <c>run</c>.</summary>
    public List<string> Steps { get; set; } = new List<string>();
    public ProjectInfo Project { get; set; } = new ProjectInfo();
    /// <summary>Per-project working directory. Responses go to <c>response-&lt;step&gt;.json</c> here.</summary>
    public string OutDir { get; set; } = "";
    public int TimeoutSeconds { get; set; }
    /// <summary>Step arguments from the command line, e.g. <c>archiveFile</c> for <c>archive</c>.</summary>
    public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
    /// <summary>Names of environment variables holding secrets. Never the values.</summary>
    public Dictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();
}

public sealed class ProjectInfo
{
    public string Id { get; set; } = "";
    public string Adapter { get; set; } = "";
    public string Root { get; set; } = "";
    public string WorkDir { get; set; } = "";
    /// <summary>Adapter section from plc.yml (strings, numbers, booleans, nested maps and lists).</summary>
    public Dictionary<string, object?> Settings { get; set; } = new Dictionary<string, object?>();
}

public sealed class AdapterResponse
{
    public int Protocol { get; set; } = ProtocolVersion.Current;
    public string Step { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string? Summary { get; set; }
    public long DurationMs { get; set; }
    public ToolInfo? Tool { get; set; }
    public List<Diagnostic> Diagnostics { get; set; } = new List<Diagnostic>();
    public List<TestResult> Tests { get; set; } = new List<TestResult>();
    public List<Artifact> Artifacts { get; set; } = new List<Artifact>();
    public List<ProcessInfo> Processes { get; set; } = new List<ProcessInfo>();
    public List<EnvironmentIssue> EnvironmentIssues { get; set; } = new List<EnvironmentIssue>();
    /// <summary>Set to "plc-build" when the core wrote this response because the adapter gave none.</summary>
    public string? GeneratedBy { get; set; }
}

public sealed class ToolInfo
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public Dictionary<string, string> Components { get; set; } = new Dictionary<string, string>();
}

public sealed class Diagnostic
{
    public string Severity { get; set; } = Severities.Error;
    /// <summary>Step or sub-tool that produced the message, e.g. "compile", "analyze".</summary>
    public string? Source { get; set; }
    /// <summary>Object path inside the engineering project.</summary>
    public string? Object { get; set; }
    /// <summary>Only when the engineering tool provides it; never parsed from message text.</summary>
    public DiagnosticLocation? Location { get; set; }
    /// <summary>Message or rule code, if the tool provides one.</summary>
    public string? Code { get; set; }
    public string Message { get; set; } = "";
}

public sealed class DiagnosticLocation
{
    public string? File { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public int? Network { get; set; }
}

public sealed class TestResult
{
    public string Suite { get; set; } = "";
    public string Name { get; set; } = "";
    public string Outcome { get; set; } = TestOutcomes.Passed;
    public long DurationMs { get; set; }
    public string? Message { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
}

public sealed class Artifact
{
    /// <summary>e.g. "archive", "sarif", "log".</summary>
    public string Kind { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Sha256 { get; set; }
}

public sealed class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Required for cleanup: a PID is only killed if its start time matches.</summary>
    public DateTimeOffset? StartTime { get; set; }
}

public sealed class EnvironmentIssue
{
    /// <summary>Stable English code, e.g. NOT_IN_OPENNESS_GROUP.</summary>
    public string Code { get; set; } = "";
    /// <summary>German message for the agent maintainer.</summary>
    public string Message { get; set; } = "";
    /// <summary>German instruction how to fix it.</summary>
    public string? Remedy { get; set; }
}
