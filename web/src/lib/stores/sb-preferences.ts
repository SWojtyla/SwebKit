export type RowDensity = "compact" | "default" | "comfort";

export interface SbListPreferences {
  peekCount: number;
  autoRefreshInterval: number; // 0 = off, seconds otherwise
  rowDensity: RowDensity;
  visibleColumns: string[];
  customColumns: string[];
}

const DEFAULT_PREFS: SbListPreferences = {
  peekCount: 50,
  autoRefreshInterval: 0,
  rowDensity: "default",
  visibleColumns: ["subject", "sequenceNumber", "enqueuedAt"],
  customColumns: [],
};

const STORAGE_PREFIX = "sb-list-prefs";

function getKey(nsId: string, entityPath: string): string {
  return `${STORAGE_PREFIX}:${nsId}:${entityPath}`;
}

export function loadSbPreferences(nsId: string, entityPath: string): SbListPreferences {
  try {
    const raw = localStorage.getItem(getKey(nsId, entityPath));
    if (!raw) return DEFAULT_PREFS;
    const parsed = JSON.parse(raw);
    return { ...DEFAULT_PREFS, ...parsed };
  } catch {
    return DEFAULT_PREFS;
  }
}

export function saveSbPreferences(nsId: string, entityPath: string, prefs: SbListPreferences): void {
  try {
    localStorage.setItem(getKey(nsId, entityPath), JSON.stringify(prefs));
  } catch {
    // ignore storage errors
  }
}

export const PEEK_COUNT_OPTIONS = [5, 10, 50, 100, 200];
export const AUTO_REFRESH_OPTIONS = [
  { label: "Off", value: 0 },
  { label: "10s", value: 10 },
  { label: "30s", value: 30 },
  { label: "60s", value: 60 },
];

export const ALL_BUILTIN_COLUMNS = [
  "subject",
  "messageId",
  "correlationId",
  "sequenceNumber",
  "enqueuedAt",
  "deliveryCount",
  "contentType",
  "sessionId",
  "partitionKey",
  "deadLetterReason",
];
