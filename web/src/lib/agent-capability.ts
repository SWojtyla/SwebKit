import type { AgentProfile } from "./types";

/**
 * Whether an agent profile can drive SwebKit tools.
 *
 * ACP profiles deliberately count as tool-capable regardless of the stored `capability` value:
 * that value only reflects the last "Test connection" click and stays "Unknown" until the user
 * runs it, while ACP tool delivery is actually gated by the agent's live `mcpCapabilities` from
 * `session/new`. `SidecarAgentChatService.ResolveTools` bypasses the stored capability the same
 * way — this helper keeps the frontend gates in parity with the backend.
 */
export function profileSupportsTools(
  profile: Pick<AgentProfile, "provider" | "capability"> | undefined,
): boolean {
  if (profile?.provider === "Acp") return true;
  return profile?.capability === "ToolCalling";
}
