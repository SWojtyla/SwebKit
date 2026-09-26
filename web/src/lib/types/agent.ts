export interface AgentConfig {
    isEnabled: boolean;
    profiles: AgentProfile[];
    activeProfileId: string;
}

export type AgentCapability = "Unknown" | "ChatOnly" | "ToolCalling";

export interface AgentProfile {
    id: string;
    provider: "LmStudio" | "OpenAiCompatible" | "Mistral" | "Acp";
    displayName: string;
    baseUrl: string;
    model: string;
    credentialKey: string;
    timeoutSeconds: number;
    capability: AgentCapability;
    lastTestDiagnostic: string | null;
    requiresApiKey: boolean;
    /** Model's context window in tokens, used to scale when a growing conversation gets rolling
     * summarization (workspace-intelligence Module 5). Null = unknown; the sidecar falls back to a
     * conservative default rather than treating null as unlimited. */
    contextWindowTokens: number | null;
    // ── ACP (external agent subprocess) — only meaningful when provider === "Acp" ──
    /** Executable to spawn (e.g. "npx", "gemini"). */
    command: string;
    /** Command-line args as one string, e.g. "-y @agentclientprotocol/claude-agent-acp". */
    arguments: string;
    /** Working directory for the agent process and the ACP session cwd. */
    workingDirectory: string;
    /** Extra non-secret env vars for the agent process. */
    environmentVariables: Record<string, string>;
    /** Env var the resolved credentialKey secret is injected as (e.g. ANTHROPIC_API_KEY). */
    credentialEnvVar: string;
    /** When true, ACP session/request_permission calls surface as approval cards instead of being auto-approved. */
    requireToolApproval: boolean;
    /** External MCP servers handed to the agent at session/new alongside SwebKit's tools — these
     * are NOT SwebKit tools: calls to them bypass the propose/confirm pipeline, so
     * requireToolApproval is the only gate they get. */
    extraMcpServers: AgentMcpServer[];
    /** Reserved: ACP fs/* client capability — designed in, handlers not implemented. */
    enableFileSystem: boolean;
    /** Reserved: ACP terminal client capability — see enableFileSystem. */
    enableTerminal: boolean;
}

/** One external MCP server an ACP profile attaches at session/new — the agent spawns or connects
 * to it directly; the sidecar only forwards the descriptor. Mirrors AgentMcpServer (SwebKit.Core). */
export interface AgentMcpServer {
    id: string;
    /** MCP server name as advertised to the agent. */
    name: string;
    enabled: boolean;
    /** "http" = remote endpoint (url + headers); "stdio" = the agent spawns it (command + args + env). */
    transport: "http" | "stdio";
    url: string;
    headers: Record<string, string>;
    command: string;
    /** Single arg string, split with shell-style quoting rules sidecar-side. */
    arguments: string;
    environmentVariables: Record<string, string>;
}

export interface AgentCapabilityTestResult {
    serverReachable: boolean;
    modelAvailable: boolean;
    chatValid: boolean;
    toolCallingValid: boolean;
    capability: AgentCapability;
    diagnostic: string | null;
    availableModels: string[] | null;
    /** Best-effort context window read from a non-standard /v1/models field (LM Studio in
     * particular) — null when the provider doesn't advertise one. */
    detectedContextWindowTokens: number | null;
}

/** One step in an assistant turn's tool-call trace — mirrors `SwebKit.Agents.AgentChatStep`. Type is
 * "tool_call" (about to run) or "tool_result" (finished); `summary` is a short, non-sensitive
 * preview, never the full result. */
export interface AgentChatStep {
    type: string;
    toolName?: string;
    summary?: string;
    elapsed?: string;
    /** True for a "tool_result" step whose tool call failed (ux-interaction-consistency unit 7.4) —
     * always false/absent for "tool_call" steps. */
    isFailure?: boolean;
}

export interface AgentReply {
    text: string;
    elapsedMs: number;
    status: string;
    error: boolean;
    /** Per-tool-call trace for this turn (workspace-intelligence Module 6) — empty when no tools were
     * used. Rendered as a collapsed-by-default "Show reasoning" disclosure. */
    steps?: AgentChatStep[];
    /** True when this turn's history was rolling-summarized before being sent (Module 5) — render as
     * an inline "earlier parts of this conversation were summarized" notice. */
    summarized?: boolean;
    /** Percentage of the effective context window this turn's request used. */
    contextUsagePercent?: number;
    /** agent-correlation Module 3 — "workspace" when the agent reached for a tool this turn's
     * scope fence hid (ACP only). Render as a "retry with workspace scope" affordance. */
    suggestedScope?: string;
}

/** One incremental event from POST /api/agent/chat/stream — see streamAgentChat in lib/api.ts and
 * IAgentModelClient.ChatStreamAsync (SwebKit.Agents) for the producing side. "done" always carries
 * `result` and is always the last event on success; "error" always carries `errorMessage` and is
 * always the last event on failure — nothing follows either. */
export type AgentStreamEventKind =
    | "token"
    | "toolCallStarted"
    | "toolCallResult"
    | "done"
    | "error"
    /** ACP agent_thought_chunk — the agent's reasoning, not reply text. */
    | "thought"
    /** An ACP permission request is parked waiting for the user (RequireToolApproval on). */
    | "permissionRequired";

export interface AgentStreamEvent {
    kind: AgentStreamEventKind;
    token?: string;
    toolName?: string;
    result?: AgentReply;
    errorMessage?: string;
}

/** A parked ACP session/request_permission call awaiting a user decision — only populated when
 * the active ACP profile has requireToolApproval on (otherwise they're auto-approved and never
 * reach this list). Mirrors PendingAction's poll-and-respond shape. */
export interface AcpPermissionOption {
    optionId: string;
    name: string;
    kind: string | null;
}

export interface AcpPermission {
    id: string;
    toolCallTitle: string;
    options: AcpPermissionOption[];
    expiresAt: string;
}

export interface AgentStatus {
    historyCount: number;
    /** Rough ~4-chars-per-token estimate over this session's history — not real tokenization, just
     * enough to let the user watch the conversation grow (see SidecarAgentChatService.GetEstimatedTokens). */
    estimatedTokens: number;
    /** Percentage of the active profile's effective context window the most recent turn's
     * fully-constructed request used (workspace-intelligence Module 5/6) — 0 if no turn has been
     * sent yet in this session. */
    contextUsagePercent: number;
    /** The percentage at which the context-usage indicator should switch to a warning color — the
     * same scaled threshold the backend uses to trigger rolling summarization
     * (workspace-intelligence Module 7). */
    contextUsageWarningPercent: number;
}

/** "ask" = read-only tools only. "ask_and_do" = mutating propose/prepare tools are also
 * available (still gated behind a confirm card — see PendingActionCard). */
export type AgentChatMode = "ask" | "ask_and_do";

/** "feature" (default) = tools scoped to the current contextual panel's area only. "workspace" =
 * the "search across my whole workspace" escalation (workspace-intelligence Module 3) — every
 * configured area's tools become visible for this turn. Orthogonal to `AgentChatMode`: scope gates
 * which area's tools are visible, mode gates whether mutate tools are available at all. */
export type AgentChatScope = "feature" | "workspace";

/** What the current page has open, passed to a contextual assistant conversation so the model can
 * be told what's on screen and so tool visibility scopes to that one feature area. `featureArea`
 * must match a backend FeatureArea enum member name (e.g. "Aks", "Redis") — see
 * SidecarAgentChatService.cs for the parsing side. */
export interface AgentChatContext {
    featureArea: string;
    selection?: Record<string, string>;
}

export interface PendingAction {
    id: string;
    type: string;
    summary: string;
    target: string;
    risk: "None" | "Low" | "High";
    preview: string;
    expiresAt: string;
}

export interface AgentActionApplyResult {
    isSuccess: boolean;
    errorMessage: string | null;
    resultSummary: string | null;
}

export interface ChatMessage {
    id: string;
    role: "user" | "assistant";
    content: string;
    elapsedMs?: number;
    error?: boolean;
    /** Tool-call trace for this reply, if any (workspace-intelligence Module 6). */
    steps?: AgentChatStep[];
    /** True if this reply's turn triggered rolling summarization of older history (Module 5). */
    summarized?: boolean;
    /** True when the user clicked "Stop" mid-stream (ux-interaction-consistency unit 7.3) —
     * rendered as a neutral "Stopped" notice rather than the red error state `error` produces, since
     * this was a deliberate user action, not a failure. Whatever partial `content` had already
     * streamed in is preserved, not overwritten. */
    stopped?: boolean;
    /** Accumulated ACP agent_thought_chunk reasoning for this reply (agent-correlation Module 4) —
     * rendered collapsed + muted: it's raw model reasoning, not authoritative output. */
    thoughts?: string;
}
