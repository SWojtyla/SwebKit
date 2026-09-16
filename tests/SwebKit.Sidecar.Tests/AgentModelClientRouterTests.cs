using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services;
using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

/// <summary>Covers <see cref="AgentModelClientRouter"/>'s provider dispatch — the seam that keeps
/// ACP profiles from ever being sent to the OpenAI-compatible HTTP client and vice versa.</summary>
public class AgentModelClientRouterTests
{
    private static (AgentModelClientRouter Router, OpenAiCompatibleAgentClient OpenAi, AcpAgentModelClient Acp) Create()
    {
        var settings = new UserSettingsRepository();
        var credentials = new SidecarCredentialStore(null);
        var openAi = new OpenAiCompatibleAgentClient(new HttpClient(), settings, credentials);
        var host = new AcpAgentHost(settings, credentials, new AcpPermissionStore(), NullLogger<AcpAgentHost>.Instance);
        // IServer is only touched when an actual ACP turn runs (to find the bound port for the
        // MCP URL) — never by Resolve, so a null suffices for this dispatch test.
        var acp = new AcpAgentModelClient(settings, host, null!, new OutOfScopeCallTracker());
        return (new AgentModelClientRouter(settings, openAi, acp), openAi, acp);
    }

    [Fact]
    public void Resolve_returns_the_acp_client_for_acp_profiles()
    {
        var (router, _, acp) = Create();

        Assert.Same(acp, router.Resolve(ProviderKind.Acp));
    }

    [Theory]
    [InlineData(ProviderKind.LmStudio)]
    [InlineData(ProviderKind.OpenAiCompatible)]
    [InlineData(ProviderKind.Mistral)]
    public void Resolve_returns_the_openai_client_for_http_providers(ProviderKind provider)
    {
        var (router, openAi, _) = Create();

        Assert.Same(openAi, router.Resolve(provider));
    }

    [Fact]
    public void Resolve_falls_back_to_openai_when_no_profile_is_active()
    {
        var (router, openAi, _) = Create();

        Assert.Same(openAi, router.Resolve(null));
    }
}
