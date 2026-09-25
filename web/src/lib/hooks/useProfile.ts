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
    queryFn: ({ signal }) => apiFetch<ProfileData>("/api/config/profiles", { signal }),
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

export function useTogglePinnedResource() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation<ProfileData, Error, { resource: FavoriteResource; pinned: boolean }>({
    scope: { id: "profile" },
    mutationFn: async (vars) => {
      const profile = qc.getQueryData<ProfileData>(["profile"]);
      if (!profile) throw new Error("Profile is not loaded");

      const favorites = vars.pinned
        ? [
            ...profile.config.favoriteResources.filter(
              (favorite) => favorite.snapshot.resource.key !== vars.resource.snapshot.resource.key,
            ),
            vars.resource,
          ]
        : profile.config.favoriteResources.filter(
            (favorite) => favorite.snapshot.resource.key !== vars.resource.snapshot.resource.key,
          );
      const data = {
        ...profile,
        config: { ...profile.config, favoriteResources: favorites },
      };

      await apiSend("/api/config/profiles", "PUT", data);
      return data;
    },
    onSuccess: (data) => qc.setQueryData(["profile"], data),
    onError: (error) => {
      qc.invalidateQueries({ queryKey: ["profile"] });
      notify("error", "Couldn't update pinned resources", String(error));
    },
  });
}

// ── User Settings ────────────────────────────────────────────────────────────

export function useUserSettings() {
  return useQuery({
    queryKey: ["user-settings"],
    queryFn: ({ signal }) => apiFetch<UserSettings>("/api/config/user-settings", { signal }),
  });
}

/** Whole settings, or a function producing them from what is currently cached. */
export type UserSettingsUpdate = UserSettings | ((prev: UserSettings) => UserSettings);

export function useUpdateUserSettings() {
  const qc = useQueryClient();
  const { notify } = useNotification();
  return useMutation<UserSettings, Error, UserSettingsUpdate>({
    // Same serialization `useUpdateProfile` uses, for the same reason: `AgentSettings` (the
    // heaviest user of this hook) now commits via `DraftInput`, and two commits close together
    // (e.g. blurring one field while another's save is still settling) must not have the
    // earlier response land after the later one and overwrite it.
    scope: { id: "user-settings" },
    mutationFn: async (update) => {
      // Same pattern as `useUpdateProfile`: resolve updaters inside `mutationFn`, which the
      // scope defers until the previous save has settled — a second change fired while the
      // first is still in flight otherwise spreads a stale snapshot and silently reverts it.
      const current = qc.getQueryData<UserSettings>(["user-settings"]);
      const data = typeof update === "function" ? update(current as UserSettings) : update;
      await apiSend("/api/config/user-settings", "PUT", data);
      return data;
    },
    onSuccess: (data) => qc.setQueryData(["user-settings"], data),
    onError: (error) => {
      qc.invalidateQueries({ queryKey: ["user-settings"] });
      notify("error", "Couldn't save setting", String(error));
    },
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
    queryFn: ({ signal }) => apiFetch<EnvironmentsResponse>("/api/config/environments", { signal }),
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
    queryFn: ({ signal }) => apiFetch<{ status: string; version: string }>("/health", { signal }),
    refetchInterval: 10_000,
  });
}

// ── Demo Mode ────────────────────────────────────────────────────────────────

export function useDemoMode() {
  return useQuery({
    queryKey: ["demo-mode"],
    queryFn: ({ signal }) => apiFetch<{ isDemoMode: boolean }>("/api/demo-mode", { signal }),
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
    queryFn: ({ signal }) => apiFetch<ObservabilityResource[]>("/api/observability/resources", { signal }),
    retry: false,
    staleTime: 60_000,
  });
}

// ── Settings readiness ───────────────────────────────────────────────────────

/** One entry per settings tab that has a "configured or not" concept worth signaling. */
export type SettingsReadinessArea = "aks" | "service-bus" | "redis" | "sql" | "storage";

export type SettingsReadiness = Record<SettingsReadinessArea, boolean>;

/**
 * The same "is this area configured" booleans `GeneralSettings`' getting-started checklist
 * computes, lifted out so `SettingsPage`'s tab strip can show the same signal (a small
 * readiness dot) without recomputing its own, possibly-drifting copy. `null` while the
 * profile hasn't loaded yet — callers should treat that as "don't render a signal".
 */
export function useSettingsReadiness(): SettingsReadiness | null {
  const { data: profile } = useProfile();
  const { data: demoMode } = useDemoMode();

  if (!profile) return null;

  const isDemo = demoMode?.isDemoMode ?? false;
  return {
    aks: isDemo || !!profile.config.aksConfig,
    "service-bus": profile.serviceBusNamespaces.length > 0,
    redis: (profile.config.redisConfig?.caches.length ?? 0) > 0,
    sql: (profile.config.sqlConfig?.connections.length ?? 0) > 0,
    storage: profile.config.storageAccounts.length > 0,
  };
}
