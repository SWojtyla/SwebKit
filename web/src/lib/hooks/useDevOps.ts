import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend } from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type {
  DevOpsConfig,
  ProfileData,
  ReleaseTrainRecord,
} from "../types";
import { useProfile } from "./useProfile";

export function useDevOpsConfig() {
  const { data: profile } = useProfile();
  return profile?.config.devOpsConfig;
}

export function useUpdateDevOpsConfig() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  const { data: profile } = useProfile();

  return useMutation({
    mutationFn: async (config: DevOpsConfig) => {
      if (!profile) throw new Error("Profile not loaded");
      const updated: ProfileData = {
        ...profile,
        config: { ...profile.config, devOpsConfig: config },
      };
      return apiSend("/api/config/profiles", "PUT", updated);
    },
    onSuccess: () => {
      notify("success", "DevOps settings saved");
      qc.invalidateQueries({ queryKey: ["profile"] });
    },
    onError: (err) => notify("error", "Failed to save DevOps settings", String(err)),
  });
}

export function usePatKeys() {
  return useQuery({
    queryKey: ["devops", "pat-keys"],
    queryFn: () => apiFetch<string[]>("/api/devops/pat-keys"),
  });
}

export function useSavePat() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: ({ key, pat }: { key: string; pat: string }) =>
      apiSend<{ key: string }>("/api/devops/pat", "POST", { key, pat }),
    onSuccess: () => {
      notify("success", "PAT saved securely");
      qc.invalidateQueries({ queryKey: ["devops", "pat-keys"] });
    },
    onError: (err) => notify("error", "Failed to save PAT", String(err)),
  });
}

export function useDeletePat() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (key: string) =>
      apiSend(`/api/devops/pat/${encodeURIComponent(key)}`, "DELETE"),
    onSuccess: () => {
      notify("success", "PAT removed");
      qc.invalidateQueries({ queryKey: ["devops", "pat-keys"] });
    },
    onError: (err) => notify("error", "Failed to remove PAT", String(err)),
  });
}

export function useTestDevOpsConnection() {
  const { notify } = useNotification();

  return useMutation({
    mutationFn: () => apiFetch<{ connected: boolean; mode: string; error?: string }>("/api/devops/test-connection", { method: "POST" }),
    onSuccess: (data) => {
      if (data.connected) notify("success", "DevOps connection OK", data.mode === "demo" ? "Demo mode" : "Live");
      else notify("error", "DevOps connection failed", data.error ?? "Unknown error");
    },
    onError: (err) => notify("error", "DevOps connection test failed", String(err)),
  });
}

// ── Release trains ───────────────────────────────────────────────────────────

export function useReleaseTrains() {
  return useQuery({
    queryKey: ["release-trains"],
    queryFn: () => apiFetch<ReleaseTrainRecord[]>("/api/release-trains"),
  });
}

export function useReleaseTrain(id: string | null) {
  return useQuery({
    queryKey: ["release-trains", id],
    queryFn: () => apiFetch<ReleaseTrainRecord>(`/api/release-trains/${id}`),
    enabled: !!id,
  });
}

export interface CreateReleaseTrainVars {
  profileId: string;
  groupId: string;
  name: string;
  label?: string | null;
  overallRemarks?: string | null;
  components: { componentName: string; version: string; remarks?: string | null }[];
}

export function useCreateReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (vars: CreateReleaseTrainVars) =>
      apiSend<ReleaseTrainRecord>("/api/release-trains", "POST", vars),
    onSuccess: () => {
      notify("success", "Release train created");
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to create release train", String(err)),
  });
}

export function usePreflightReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (id: string) => apiSend<{ canProceed: boolean; issues: { componentName: string; message: string; isBlocking: boolean }[] }>(`/api/release-trains/${id}/preflight`, "POST"),
    onSuccess: (data) => {
      if (data.canProceed) notify("success", "Preflight passed");
      else notify("error", "Preflight failed", data.issues.map((i) => `${i.componentName}: ${i.message}`).join("; "));
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Preflight failed", String(err)),
  });
}

export function useExecuteReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (id: string) => apiSend<ReleaseTrainRecord>(`/api/release-trains/${id}/execute`, "POST"),
    onSuccess: (data) => {
      notify("success", "Release train executing", `Status: ${data.status}`);
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to execute release train", String(err)),
  });
}

export function useRefreshReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (id: string) => apiSend<ReleaseTrainRecord>(`/api/release-trains/${id}/refresh`, "POST"),
    onSuccess: (data) => {
      notify("info", "Release train refreshed", `Status: ${data.status}`);
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to refresh release train", String(err)),
  });
}

export function useCompleteReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (id: string) => apiSend(`/api/release-trains/${id}/complete`, "POST"),
    onSuccess: () => {
      notify("success", "Release train completed");
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to complete release train", String(err)),
  });
}

export function useDeleteReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (id: string) => apiSend(`/api/release-trains/${id}`, "DELETE"),
    onSuccess: () => {
      notify("success", "Release train archived");
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to archive release train", String(err)),
  });
}

export function useAdvanceDemoReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: ({ id, failComponent }: { id: string; failComponent?: string }) =>
      apiSend<ReleaseTrainRecord>(`/api/release-trains/${id}/advance-demo${failComponent ? `?failComponent=${encodeURIComponent(failComponent)}` : ""}`, "POST"),
    onSuccess: (data) => {
      notify("success", "Demo advanced", `Status: ${data.status}`);
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to advance demo", String(err)),
  });
}

export function useRetryReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (id: string) => apiSend<ReleaseTrainRecord>(`/api/release-trains/${id}/retry`, "POST"),
    onSuccess: (data) => {
      notify("success", "Retry completed", `Status: ${data.status}`);
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to retry release train", String(err)),
  });
}

export function useDriftReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: ({ id, componentName }: { id: string; componentName?: string }) =>
      apiSend<ReleaseTrainRecord>(`/api/release-trains/${id}/drift${componentName ? `?componentName=${encodeURIComponent(componentName)}` : ""}`, "POST"),
    onSuccess: (data) => {
      notify("info", "Drift injected", data.driftWarnings?.length ? data.driftWarnings.join("; ") : "No drift detected");
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to inject drift", String(err)),
  });
}

export function useAttachRunReleaseTrain() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (vars: { id: string; componentId: string; projectName: string; pipelineId: number; runId: number; sourceVersion?: string }) =>
      apiSend<ReleaseTrainRecord>(`/api/release-trains/${vars.id}/components/${vars.componentId}/attach-run`, "POST", {
        projectName: vars.projectName,
        pipelineId: vars.pipelineId,
        runId: vars.runId,
        sourceVersion: vars.sourceVersion,
      }),
    onSuccess: () => {
      notify("success", "Run attached");
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to attach run", String(err)),
  });
}

export function useUpdateReleaseTrainRemarks() {
  const qc = useQueryClient();
  const { notify } = useNotification();

  return useMutation({
    mutationFn: (vars: { id: string; overallRemarks?: string | null; componentRemarks?: Record<string, string> }) =>
      apiSend<ReleaseTrainRecord>(`/api/release-trains/${vars.id}/remarks`, "PUT", vars),
    onSuccess: () => {
      notify("success", "Remarks updated");
      qc.invalidateQueries({ queryKey: ["release-trains"] });
    },
    onError: (err) => notify("error", "Failed to update remarks", String(err)),
  });
}
