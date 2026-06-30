using SwebKit.Core.Services;

namespace SwebKit.Agents;

/// <summary>
/// Builds context information about the current SwebKit workspace for AI agent consumption.
/// </summary>
public interface IAgentContextBuilder
{
    /// <summary>
    /// Builds a context string that describes the current workspace configuration.
    /// This is injected into the system prompt sent to Mistral.
    /// </summary>
    string BuildContext(AppStateService appState);
}
