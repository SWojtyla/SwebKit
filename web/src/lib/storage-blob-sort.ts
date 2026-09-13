import type { StorageBlobItem } from "./types";

export type StorageBlobSortKey = "name" | "size" | "modified";
export type StorageBlobSortDir = "asc" | "desc";

/**
 * Sorts blob/prefix rows for display. Purely a presentation concern over whatever
 * page(s) are already loaded — there's no server-side sort, so this only orders what
 * the filter has already narrowed down to.
 */
export function sortStorageBlobItems(
  items: StorageBlobItem[],
  key: StorageBlobSortKey,
  dir: StorageBlobSortDir,
): StorageBlobItem[] {
  const sign = dir === "asc" ? 1 : -1;
  return [...items].sort((a, b) => {
    let cmp: number;
    if (key === "name") {
      cmp = a.name.localeCompare(b.name);
    } else if (key === "size") {
      cmp = (a.sizeBytes ?? -1) - (b.sizeBytes ?? -1);
    } else {
      const at = a.lastModified ? Date.parse(a.lastModified) : -1;
      const bt = b.lastModified ? Date.parse(b.lastModified) : -1;
      cmp = at - bt;
    }
    return cmp * sign;
  });
}
