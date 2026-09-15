using System.Text.Json;
using SwebKit.Agents;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// <see cref="IAgentModelClient"/> dispatcher: routes every call to the provider implementation
/// matching the *currently active* profile (<see cref="ProviderKind.Acp"/> →
/// <see cref="AcpAgentModelClient"/>, everything else → <c>OpenAiCompatibleAgentClient</c>).
/// Resolution happens per call, not per registration, so switching the active profile in
/// Settings takes effect on the next turn without a sidecar restart.
/// </summary>
public sealed class AgentModelClientRouter : IAgentModelClient
{
    private readonly UserSettingsRepository _settings;
    private readonly OpenAiCompatibleAgentClient _openAi;
    private readonly AcpAgentModelClient _acp;

    public AgentModelClientRouter(
        UserSettingsRepository settings,
        OpenAiCompatibleAgentClient openAi,
        AcpAgentModelClient acp)
    {
        _settings = settings;
        _openAi = openAi;
        _acp = acp;
    }

    private IAgentModelClient Resolve() =>
        Resolve(_settings.Settings.Agent.GetActiveProfile()?.Provider);

    internal IAgentModelClient Resolve(ProviderKind? provider) =>
        provider == ProviderKind.Acp ? _acp : _openAi;

    public Task<AgentChatResult> ChatAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct) =>
        Resolve().ChatAsync(request, toolExecutor, ct);

    public Task<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken ct) =>
        Resolve().CompleteAsync(request, ct);

    public IAsyncEnumerable<AgentStreamEvent> ChatStreamAsync(
        AgentModelRequest request,
        Func<string, JsonElement, CancellationToken, Task<string>>? toolExecutor,
        CancellationToken ct) =>
        Resolve().ChatStreamAsync(request, toolExecutor, ct);
}
