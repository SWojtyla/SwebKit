using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers the pure parts of <see cref="AcpAgentHost"/> that don't need a live agent
/// process — the auto-approve option selection (the process/session lifecycle is exercised
/// end-to-end by hand against a real agent, per the feature's test plan).</summary>
public class AcpAgentHostTests
{
    [Fact]
    public void PickAutoApproveOption_prefers_allow_once_over_other_options()
    {
        var options = new[]
        {
            new AcpPermissionOption("a1", "Allow always", "allow_always"),
            new AcpPermissionOption("a2", "Allow once", "allow_once"),
            new AcpPermissionOption("r1", "Reject once", "reject_once"),
        };

        Assert.Equal("a2", AcpAgentHost.PickAutoApproveOption(options));
    }

    [Fact]
    public void PickAutoApproveOption_falls_back_to_any_allow_option()
    {
        var options = new[]
        {
            new AcpPermissionOption("r1", "Reject once", "reject_once"),
            new AcpPermissionOption("a1", "Allow always", "allow_always"),
        };

        Assert.Equal("a1", AcpAgentHost.PickAutoApproveOption(options));
    }

    [Fact]
    public void PickAutoApproveOption_uses_the_first_option_when_nothing_allows()
    {
        var options = new[]
        {
            new AcpPermissionOption("r1", "Reject once", "reject_once"),
            new AcpPermissionOption("r2", "Reject always", "reject_always"),
        };

        Assert.Equal("r1", AcpAgentHost.PickAutoApproveOption(options));
    }

    [Fact]
    public void PickAutoApproveOption_returns_null_for_an_empty_option_list()
    {
        Assert.Null(AcpAgentHost.PickAutoApproveOption([]));
    }
}
