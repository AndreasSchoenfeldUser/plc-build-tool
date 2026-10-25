using PlcBuild.Core.Steps;
using PlcBuild.Protocol;

namespace PlcBuild.Core.Reporting;

public sealed record StepResult(string Step, AdapterResponse Response);

/// <summary>All step responses of one project, in the fixed report order.</summary>
public sealed class ProjectResults
{
    public required string ProjectId { get; init; }
    public required IReadOnlyList<StepResult> Steps { get; init; }
    /// <summary>Unreadable response files, reported but not fatal: report runs in "post { always }".</summary>
    public required IReadOnlyList<string> Problems { get; init; }

    public static ProjectResults Load(WorkLayout layout)
    {
        var steps = new List<StepResult>();
        var problems = new List<string>();
        foreach (var step in StepNames.ReportOrder)
        {
            var path = layout.ResponsePath(step);
            if (!File.Exists(path))
            {
                continue;
            }
            var (response, problem) = ResponseReader.Read(path, step);
            if (response is not null)
            {
                steps.Add(new StepResult(step, response));
            }
            else
            {
                problems.Add($"[{problem!.Code}] {Path.GetFileName(path)}: {problem.Message}");
                steps.Add(new StepResult(step, ResponseReader.Synthetic(step, problem.Code, problem.Message, 0)));
            }
        }
        return new ProjectResults { ProjectId = layout.ProjectId, Steps = steps, Problems = problems };
    }

    /// <summary>Overall outcome: the worst step outcome, "ok" if no step ran.</summary>
    public string OverallOutcome => Steps
        .Select(s => s.Response.Outcome)
        .OrderByDescending(ExitCodes.FromOutcome)
        .FirstOrDefault() ?? Outcomes.Ok;
}
