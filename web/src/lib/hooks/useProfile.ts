import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend, exportSettings, importSettings } from "../api";
import type {
  ProfileData,
  UserSettings,
  EnvironmentsResponse,
  FavoriteResource,
  ObservabilityResource,
} from "../types";

// ── Profile ──────────────────────────────────────────────────────────────────

export function useProfile() {
  return useQuery({
    queryKey: ["profile"],
    queryFn: () => apiFetch<ProfileData>("/api/config/profiles"),
  });
}

export function useUpdateProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: ProfileData) =>
      apiSend("/api/config/profiles", "PUT", data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["profile"] }),
  });
}

export function usePinnedResources() {
  const { data: profile, ...query } = useProfile();
  return {
    ...query,
    data: profile?.config.favoriteResources ?? [],
  };
}

export function useTogglePinnedResource() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { profile: ProfileData; resource: FavoriteResource; pinned: boolean }) => {
      const favorites = vars.pinned
        ? [
            ...vars.profile.config.favoriteResources.filter(
              (favorite) => favorite.snapshot.resource.key !== vars.resource.snapshot.resource.key,
            ),
            vars.resource,
          ]
        : vars.profile.config.favoriteResources.filter(
            (favorite) => favorite.snapshot.resource.key !== vars.resource.snapshot.resource.key,
          );

      return apiSend("/api/config/profiles", "PUT", {
        ...vars.profile,
        config: { ...vars.profile.config, favoriteResources: favorites },
      });
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["profile"] }),
  });
}

// ── User Settings ────────────────────────────────────────────────────────────

export function useUserSettings() {
  return useQuery({
    queryKey: ["user-settings"],
    queryFn: () => apiFetch<UserSettings>("/api/config/user-settings"),
  });
}

export function useUpdateUserSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UserSettings) =>
      apiSend("/api/config/user-settings", "PUT", data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["user-settings"] }),
  });
}

export function useExportSettings() {
  return useMutation({ mutationFn: exportSettings });
}

export function useImportSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: importSettings,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["profile"] });
      qc.invalidateQueries({ queryKey: ["user-settings"] });
      qc.invalidateQueries({ queryKey: ["config"] });
    },
  });
}

// ── Environments ─────────────────────────────────────────────────────────────

export function useEnvironments() {
  return useQuery({
    queryKey: ["environments"],
    queryFn: () => apiFetch<EnvironmentsResponse>("/api/config/environments"),
  });
}

export function useUpdateEnvironments() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (store: {
      schemaVersion: number;
      environments: import("../types").ApiEnvironment[];
      uiState: import("../types").ApiClientUiState;
    }) => apiSend("/api/config/environments", "PUT", store),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["environments"] }),
  });
}

// ── Health ───────────────────────────────────────────────────────────────────

export function useHealth() {
  return useQuery({
    queryKey: ["health"],
    queryFn: () => apiFetch<{ status: string; version: string }>("/health"),
    refetchInterval: 10_000,
  });
}

// ── Demo Mode ────────────────────────────────────────────────────────────────

export function useDemoMode() {
  return useQuery({
    queryKey: ["demo-mode"],
    queryFn: () => apiFetch<{ isDemoMode: boolean }>("/api/demo-mode"),
  });
}

export function useToggleDemoMode() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (enabled: boolean) =>
      apiSend(`/api/demo-mode?enabled=${enabled}`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries();
    },
    onError: () => {
      qc.invalidateQueries({ queryKey: ["demo-mode"] });
    },
  });
}

// ── Observability resources ──────────────────────────────────────────────────

export function useObservabilityResources() {
  return useQuery({
    queryKey: ["observability-resources"],
    queryFn: () => apiFetch<ObservabilityResource[]>("/api/observability/resources"),
    retry: false,
    staleTime: 60_000,
  });
}
