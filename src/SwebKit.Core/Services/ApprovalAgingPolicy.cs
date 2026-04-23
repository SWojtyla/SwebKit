using SwebKit.Core.Models;

namespace SwebKit.Core.Services;

public sealed class ApprovalAgingPolicy
{
    // Production-like env names — case-insensitive partial match
    private static readonly string[] ProductionNames = ["prod", "production", "prd"];

    // Thresholds
    private static readonly TimeSpan ProdWarning = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ProdBreached = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan NonProdWarning = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan NonProdBreached = TimeSpan.FromHours(4);

    public ApprovalAgeResult Evaluate(AdoApproval approval, DateTimeOffset now)
    {
        var age = now - approval.CreatedOn;
        var isProd = IsProductionLike(approval.EnvironmentName);
        var state = ClassifyAge(age, isProd);
        return new ApprovalAgeResult(approval.Id, age, state, approval.EnvironmentName);
    }

    public IReadOnlyList<ApprovalAgeResult> EvaluateAll(
        IEnumerable<AdoApproval> approvals, DateTimeOffset now)
        => approvals.Select(a => Evaluate(a, now)).ToList();

    private static bool IsProductionLike(string? envName) =>
        envName is not null &&
        ProductionNames.Any(p => envName.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static ApprovalAgeState ClassifyAge(TimeSpan age, bool isProd)
    {
        var (warn, breach) = isProd
            ? (ProdWarning, ProdBreached)
            : (NonProdWarning, NonProdBreached);

        return age >= breach ? ApprovalAgeState.Breached
             : age >= warn ? ApprovalAgeState.Warning
                            : ApprovalAgeState.OnTime;
    }
}
