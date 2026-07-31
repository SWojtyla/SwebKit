/// Persisted API Client repository selection.
///
/// Only the *selection* lives here. The authority list of paths the user granted
/// through an OS dialog is persisted on the Rust side and re-admitted via
/// `restoreAllowedRoot` — writing a path into localStorage must never be enough to
/// authorize git access. See
/// docs/features/active/api-client-git-completion/decisions.md DEC-G3.

const STORAGE_KEY = "api-client-git-repos";

export interface GitRepoConfig {
  path: string;
  /**
   * Repository-relative folder holding API collection files. Staging and
   * committing are scoped to it. `null` means the repository root, which the UI
   * warns about because it removes the scoping guard.
   */
  apiSubpath: string | null;
}

export interface GitRepoState {
  repos: GitRepoConfig[];
  /** Path of the selected repo; must match one of `repos`. */
  selectedPath: string | null;
}

const EMPTY: GitRepoState = { repos: [], selectedPath: null };

export function loadGitRepoState(): GitRepoState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return EMPTY;
    const parsed = JSON.parse(raw) as GitRepoState;
    if (!Array.isArray(parsed?.repos)) return EMPTY;

    const repos = parsed.repos.filter(
      (r): r is GitRepoConfig => typeof r?.path === "string" && r.path.length > 0,
    );
    const selectedPath =
      typeof parsed.selectedPath === "string" && repos.some((r) => r.path === parsed.selectedPath)
        ? parsed.selectedPath
        : repos[0]?.path ?? null;

    return { repos, selectedPath };
  } catch {
    return EMPTY;
  }
}

export function saveGitRepoState(state: GitRepoState): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    // ignore storage errors
  }
}

export function addRepo(state: GitRepoState, path: string): GitRepoState {
  if (state.repos.some((r) => r.path === path)) {
    return { ...state, selectedPath: path };
  }
  return {
    repos: [...state.repos, { path, apiSubpath: null }],
    selectedPath: path,
  };
}

export function removeRepo(state: GitRepoState, path: string): GitRepoState {
  const repos = state.repos.filter((r) => r.path !== path);
  return {
    repos,
    selectedPath: state.selectedPath === path ? repos[0]?.path ?? null : state.selectedPath,
  };
}

export function setApiSubpath(
  state: GitRepoState,
  path: string,
  apiSubpath: string | null,
): GitRepoState {
  return {
    ...state,
    repos: state.repos.map((r) => (r.path === path ? { ...r, apiSubpath } : r)),
  };
}

export function selectedRepo(state: GitRepoState): GitRepoConfig | null {
  if (!state.selectedPath) return null;
  return state.repos.find((r) => r.path === state.selectedPath) ?? null;
}
