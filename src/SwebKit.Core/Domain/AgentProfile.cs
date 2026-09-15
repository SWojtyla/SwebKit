namespace SwebKit.Core.Domain;

/// <summary>
/// Result of the last capability test for a profile.
/// </summary>
public enum AgentCapability
{
    /// <summary>Not yet tested.</summary>
    Unknown,

    /// <summary>Server reachable but model does not support tool calling.</summary>
    ChatOnly,

    /// <summary>Model supports native tool calling.</summary>
    ToolCalling,
}

/// <summary>
/// A named LLM provider profile with endpoint, model, credential reference and runtime parameters.
/// Stored in <see cref="AgentConfig.Profiles"/>; the active profile is selected by
/// <see cref="AgentConfig.ActiveProfileId"/>.
/// </summary>
public sealed class AgentProfile
{
    /// <summary>Stable unique identifier for this profile (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Human-readable name shown in the UI (e.g. "LM Studio Local", "Mistral Cloud").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Provider type determining presets and behaviour.</summary>
    public ProviderKind Provider { get; set; } = ProviderKind.LmStudio;

    /// <summary>
    /// Base URL of the OpenAI-compatible API endpoint (e.g. <c>http://localhost:1234/v1</c>).
    /// Must not include a trailing slash.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Model identifier to use for chat completions (e.g. <c>mistral-medium-latest</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Logical credential key for the API key (e.g. <c>SwebKit-Agent:Mistral-ApiKey</c>).
    /// The actual secret is resolved via <c>ICredentialStore</c> at runtime.
    /// Empty or null for providers that don't require a key (e.g. LM Studio).
    /// </summary>
    public string? CredentialKey { get; set; }

    /// <summary>Request timeout in seconds. 0 means use the HttpClient default.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Capability result from the last connection/capability test.</summary>
    public AgentCapability Capability { get; set; } = AgentCapability.Unknown;

    /// <summary>Human-readable diagnostic from the last test (null if no test was run).</summary>
    public string? LastTestDiagnostic { get; set; }

    /// <summary>
    /// The model's context window, in tokens — used by <c>SidecarAgentChatService</c> to decide
    /// when a growing conversation needs rolling summarization (workspace-intelligence Module 5).
    /// Best-effort auto-populated by <see cref="SwebKit.Agents.AgentCapabilityTester"/> when a
    /// provider's <c>/v1/models</c> response happens to advertise it (not all do); the user can
    /// also set or override it directly. Null means "unknown" — treated as a conservative default,
    /// not zero/unlimited, everywhere it's read.
    /// </summary>
    public int? ContextWindowTokens { get; set; }

    // ── ACP (external agent subprocess) fields — only meaningful when Provider == Acp ──

    /// <summary>Executable to spawn for an ACP agent (e.g. <c>npx</c>, <c>gemini</c>). On Windows,
    /// <c>.cmd</c>/<c>.bat</c> shims are resolved through PATHEXT by the launcher.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Command-line arguments for <see cref="Command"/> as a single string, split with
    /// shell-style quoting rules by the launcher (e.g. <c>-y @agentclientprotocol/claude-agent-acp</c>).</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Working directory for the agent process and the ACP session's <c>cwd</c>. Empty
    /// means the sidecar's own working directory.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Extra environment variables passed to the agent process (non-secret config only —
    /// secrets go through <see cref="CredentialKey"/> + <see cref="CredentialEnvVar"/>).</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

    /// <summary>Name of the environment variable the resolved <see cref="CredentialKey"/> secret is
    /// injected as when spawning the agent (e.g. <c>ANTHROPIC_API_KEY</c>). Ignored when
    /// <see cref="CredentialKey"/> is empty.</summary>
    public string CredentialEnvVar { get; set; } = string.Empty;

    /// <summary>When true, ACP <c>session/request_permission</c> calls are surfaced to the user as
    /// approval cards instead of being auto-approved. Default false: SwebKit's own tools are
    /// already self-gating (mutations only create pending-action proposals), so per-tool-call
    /// agent permission prompts would be a redundant second click.</summary>
    public bool RequireToolApproval { get; set; }

    /// <summary>Reserved: advertise ACP <c>fs/*</c> client capabilities so the agent can read and
    /// write files on this machine. Ships disabled — the capability is designed in but the
    /// server-side handlers are not implemented.</summary>
    public bool EnableFileSystem { get; set; }

    /// <summary>Reserved: advertise the ACP <c>terminal</c> client capability so the agent can run
    /// shell commands on this machine. Ships disabled — see <see cref="EnableFileSystem"/>.</summary>
    public bool EnableTerminal { get; set; }

    /// <summary>Whether this profile requires an API key to function. ACP agents own their auth
    /// (e.g. an existing <c>claude</c> login) — <see cref="CredentialKey"/> is optional there and
    /// only injects an env var when set.</summary>
    public bool RequiresApiKey => Provider is not ProviderKind.LmStudio and not ProviderKind.Acp;
}
