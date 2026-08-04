import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "../api";
import type { WorkspaceResourceCandidate, WorkspaceRelationshipSuggestion } from "../types";

/** Nodes/relationships themselves round-trip through `useProfile`/`useUpdateProfile` — they're
 * plain fields on `profile.config.topology`, same as `redisConfig`/`storageAccounts`. This hook
 * only covers the one thing that isn't a persisted profile field: the auto-populated candidate
 * list computed from existing config. */
export function useWorkspaceTopologyCandidates() {
  return useQuery({
    queryKey: ["workspace-topology-candidates"],
    queryFn: () => apiFetch<WorkspaceResourceCandidate[]>("/api/workspace/topology/candidates"),
  });
}

/** workspace-intelligence Module 2's heuristic relationship scan — also never persisted; recomputed
 * server-side on every fetch, so confirming a suggestion (which adds a real relationship via
 * `useUpdateProfile`) makes it naturally disappear from the next fetch instead of needing its own
 * "accepted" bookkeeping. */
export function useWorkspaceTopologySuggestions() {
  return useQuery({
    queryKey: ["workspace-topology-suggestions"],
    queryFn: () => apiFetch<WorkspaceRelationshipSuggestion[]>("/api/workspace/topology/suggestions"),
  });
}
