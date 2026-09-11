/// Persisted Service Bus namespace/entity selection, so leaving the page and
/// coming back (or restarting the app) restores where the operator left off,
/// the same way `git-repo-preferences.ts` remembers the selected repo.

const LAST_NAMESPACE_KEY = "sb-last-namespace";
const LAST_ENTITY_PREFIX = "sb-last-entity";

export interface SbLastEntity {
  entityPath: string;
  name: string;
}

export function loadLastNamespace(): string | null {
  try {
    return localStorage.getItem(LAST_NAMESPACE_KEY);
  } catch {
    return null;
  }
}

export function saveLastNamespace(nsId: string): void {
  try {
    localStorage.setItem(LAST_NAMESPACE_KEY, nsId);
  } catch {
    // ignore storage errors
  }
}

function entityKey(nsId: string): string {
  return `${LAST_ENTITY_PREFIX}:${nsId}`;
}

export function loadLastEntity(nsId: string): SbLastEntity | null {
  try {
    const raw = localStorage.getItem(entityKey(nsId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as SbLastEntity;
    if (typeof parsed?.entityPath !== "string" || !parsed.entityPath) return null;
    return { entityPath: parsed.entityPath, name: typeof parsed.name === "string" ? parsed.name : parsed.entityPath };
  } catch {
    return null;
  }
}

export function saveLastEntity(nsId: string, entity: SbLastEntity | null): void {
  try {
    if (!entity) {
      localStorage.removeItem(entityKey(nsId));
      return;
    }
    localStorage.setItem(entityKey(nsId), JSON.stringify(entity));
  } catch {
    // ignore storage errors
  }
}
