import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiFetch, apiSend, exportSettings, importSettings } from "../api";
import { useNotification } from "@/components/layout/NotificationSystem";
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

/** A whole profile, or a function producing one from what is currently cached. */
export type ProfileUpdate = ProfileData | ((prev: ProfileData) => ProfileData);

export function useUpdateProfile() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation<ProfileData, Error, ProfileUpdate>({
    // Serialized on one scope, so two saves are never in flight at once. Without it,
    // saves raced and their responses landed out of order: clicking the Entra ID radio
    // while a keystroke's save was still settling had the older response overwrite the
    // cache, and the radio snapped straight back. Same defect `useUpdateCollections`
    // was fixed for.
    scope: { id: "profile" },
    mutationFn: async (update) => {
      // Read inside `mutationFn`, which the scope defers until the previous save has
      // settled and written its result back — so an updater always sees current state
      // rather than a snapshot taken before the last save.
      const current = qc.getQueryData<ProfileData>(["profile"]);
      const data = typeof update === "function" ? update(current as ProfileData) : update;
      await apiSend("/api/config/profiles", "PUT", data);
      // The endpoint returns 200 with no body, so the payload we sent *is* the new state.
      return data;
    },
    // Writing the result back rather than invalidating: an invalidate refetched the whole
    // profile after every save, which on a per-keystroke save meant a disk write, two
    // round-trips and a full settings re-render per character.
    onSuccess: (data) => {
      qc.setQueryData(["profile"], data);
    },
    // A failed save leaves the cache describing something the server never accepted, so
    // resync rather than letting the UI quietly disagree with disk.
    onError: (error) => {
      qc.invalidateQueries({ queryKey: ["profile"] });
      notify("error", "Couldn't save setting", String(error));
    },
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
  const { notify } = useNotification();
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
    onError: (error) => notify("error", "Couldn't update pinned resources", String(error)),
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
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (data: UserSettings) =>
      apiSend("/api/config/user-settings", "PUT", data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["user-settings"] }),
    onError: (error) => notify("error", "Couldn't save setting", String(error)),
  });
}

export function useExportSettings() {
  const { notify } = useNotification();
  return useMutation({
    mutationFn: exportSettings,
    onError: (error) => notify("error", "Couldn't export settings", String(error)),
  });
}

export function useImportSettings() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation({
    mutationFn: importSettings,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["profile"] });
      qc.invalidateQueries({ queryKey: ["user-settings"] });
      qc.invalidateQueries({ queryKey: ["config"] });
    },
    onError: (error) => notify("error", "Couldn't import settings", String(error)),
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
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (store: {
      schemaVersion: number;
      environments: import("../types").ApiEnvironment[];
      uiState: import("../types").ApiClientUiState;
    }) => apiSend("/api/config/environments", "PUT", store),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["environments"] }),
    onError: (error) => notify("error", "Couldn't save environments", String(error)),
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
  const { notify } = useNotification();
  return useMutation({
    mutationFn: (enabled: boolean) =>
      apiSend(`/api/demo-mode?enabled=${enabled}`, "POST"),
    onSuccess: () => {
      qc.invalidateQueries();
    },
    onError: (error) => {
      qc.invalidateQueries({ queryKey: ["demo-mode"] });
      notify("error", "Couldn't toggle demo mode", String(error));
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
