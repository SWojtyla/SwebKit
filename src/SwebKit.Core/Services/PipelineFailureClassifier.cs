using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public sealed class PipelineFailureClassifier
{
    // Keywords mapped to categories — checked in order against failed-stage names
    private static readonly (string[] Keywords, PipelineFailureCategory Category)[] StageKeywords =
    [
        (["queue", "agent", "initialize"], PipelineFailureCategory.QueuedOrAgent),
        (["build", "compile", "test", "lint", "unit"], PipelineFailureCategory.BuildOrTest),
        (["health", "smoke", "validate", "verify", "post"], PipelineFailureCategory.PostDeployHealth),
        (["approval", "gate", "check", "wait", "review"], PipelineFailureCategory.ApprovalGate),
        (["deploy", "release", "helm", "rollout", "publish"], PipelineFailureCategory.Deploy),
        (["auth", "permission", "infra", "credential", "secret", "config"], PipelineFailureCategory.InfraOrAuth),
    ];

    public PipelineFailureResult Classify(AdoPipelineRun run, IEnumerable<WaitingStage>? waitingStages = null)
    {
        if (run.State != "completed" || run.Result == "succeeded")
            return new PipelineFailureResult(run.Id, PipelineFailureCategory.Unknown, null,
                "Run did not fail or is not yet complete.");

        // Check if any stage was waiting for approval at failure time
        var waiting = waitingStages?.ToList() ?? [];
        if (waiting.Count > 0)
        {
            var ws = waiting[0];
            return new PipelineFailureResult(run.Id, PipelineFailureCategory.ApprovalGate,
                ws.StageName, $"Run halted at approval gate in stage '{ws.StageName}'.");
        }

        // Find failed stages and classify by name
        var failedStage = run.Stages
            .Where(s => s.Result is "failed" or "canceled")
            .OrderBy(s => s.Order)
            .FirstOrDefault();

        if (failedStage is not null)
        {
            var category = ClassifyByName(failedStage.Name);
            var explanation = BuildExplanation(category, failedStage.Name, run.Result);
            return new PipelineFailureResult(run.Id, category, failedStage.Name, explanation);
        }

        // Run-level result without stage evidence
        return new PipelineFailureResult(run.Id, ClassifyByRunResult(run.Result), null,
            $"Run ended with result '{run.Result}'; no failed stage identified.");
    }

    private static PipelineFailureCategory ClassifyByName(string stageName)
    {
        foreach (var (keywords, category) in StageKeywords)
        {
            if (keywords.Any(k => stageName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return category;
        }
        return PipelineFailureCategory.Unknown;
    }

    private static PipelineFailureCategory ClassifyByRunResult(string result) =>
        result switch
        {
            "canceled" => PipelineFailureCategory.QueuedOrAgent,
            _ => PipelineFailureCategory.Unknown
        };

    private static string BuildExplanation(PipelineFailureCategory category, string stageName, string runResult) =>
        category switch
        {
            PipelineFailureCategory.QueuedOrAgent => $"Stage '{stageName}' failed during agent acquisition or initialization.",
            PipelineFailureCategory.BuildOrTest => $"Stage '{stageName}' failed during build or test execution.",
            PipelineFailureCategory.ApprovalGate => $"Stage '{stageName}' failed at an approval gate or pre-deployment check.",
            PipelineFailureCategory.Deploy => $"Stage '{stageName}' failed during deployment or release rollout.",
            PipelineFailureCategory.PostDeployHealth => $"Stage '{stageName}' failed during post-deployment validation or health check.",
            PipelineFailureCategory.InfraOrAuth => $"Stage '{stageName}' failed due to infrastructure, credential, or configuration issues.",
            _ => $"Stage '{stageName}' failed with result '{runResult}'; category could not be determined from stage name alone."
        };
}
