import type { PendingAction } from "@/lib/types";

/**
 * Maps a `PendingAction.type` (a backend `AgentActionType` enum member name) to the feature area
 * that proposes it, so the card can show "Proposed from: <area> · <target>" (ux-interaction-
 * consistency unit 7.2) without the backend having to carry a new `featureArea`/`sessionId` field —
 * this plan's non-goals limit backend changes to surfacing data that already exists, and the action
 * `type` (feature-specific by construction) plus the existing `target` (already a specific,
 * human-readable instance description — a request name+id, a Redis key, a blob path) together give
 * the same "which conversation proposed this" answer a dedicated attribution field would.
 */
const FEATURE_AREA_BY_ACTION_TYPE: Record<string, string> = {
  CreateRequest: "API Client",
  UpdateRequest: "API Client",
  DeleteRequest: "API Client",
  DuplicateRequest: "API Client",
  MoveRequest: "API Client",
  RenameFolder: "API Client",
  DeleteFolder: "API Client",
  ExecuteHttpRequest: "API Client",
  DeleteRedisKey: "Redis",
  SetRedisKeyTtl: "Redis",
  CopyBlob: "Storage",
  ExecuteSql: "SQL",
  ApplyAksYaml: "AKS",
};

export function describePendingActionOrigin(action: Pick<PendingAction, "type" | "target">): string {
  const area = FEATURE_AREA_BY_ACTION_TYPE[action.type] ?? "Agent";
  return `${area} · ${action.target}`;
}

/** Formats the time remaining until `expiresAt` as a short, live-updating countdown ("expires in
 * 47s") instead of a static clock time that never changes on screen (unit 7.6) — `now` is passed in
 * (rather than read internally) so this stays a pure, easily-testable function. */
export function formatExpiryCountdown(expiresAt: string, now: number): string {
  const remainingMs = new Date(expiresAt).getTime() - now;
  if (remainingMs <= 0) return "expired";

  const totalSeconds = Math.ceil(remainingMs / 1000);
  if (totalSeconds < 60) return `expires in ${totalSeconds}s`;

  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `expires in ${minutes}m ${seconds.toString().padStart(2, "0")}s`;
}
