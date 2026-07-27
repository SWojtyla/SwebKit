import type { AdvancedFilterRule } from "@/components/service-bus/filterTypes";

export interface SbSavedFilter {
  name: string;
  text: string;
  filtersEnabled: boolean;
  advancedEnabled: boolean;
  advancedRules: AdvancedFilterRule[];
  pinnedSessionId: string | null;
}

const STORAGE_PREFIX = "sb-saved-filters";

function getKey(nsId: string, entityPath: string): string {
  return `${STORAGE_PREFIX}:${nsId}:${entityPath}`;
}

export function loadSavedFilters(nsId: string, entityPath: string): SbSavedFilter[] {
  try {
    const raw = localStorage.getItem(getKey(nsId, entityPath));
    if (!raw) return [];
    const parsed = JSON.parse(raw) as SbSavedFilter[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function saveSavedFilters(nsId: string, entityPath: string, filters: SbSavedFilter[]): void {
  try {
    localStorage.setItem(getKey(nsId, entityPath), JSON.stringify(filters));
  } catch {
    // ignore storage errors
  }
}

export function addSavedFilter(
  nsId: string,
  entityPath: string,
  filter: SbSavedFilter,
): SbSavedFilter[] {
  const filters = loadSavedFilters(nsId, entityPath).filter((f) => f.name !== filter.name);
  filters.push(filter);
  saveSavedFilters(nsId, entityPath, filters);
  return filters;
}

export function deleteSavedFilter(
  nsId: string,
  entityPath: string,
  name: string,
): SbSavedFilter[] {
  const filters = loadSavedFilters(nsId, entityPath).filter((f) => f.name !== name);
  saveSavedFilters(nsId, entityPath, filters);
  return filters;
}
