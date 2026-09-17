/**
 * Pure logic behind `ProfileListLayout` — selection resolution, add-detection,
 * filtering, and grouping, pulled out so they can be unit-tested without a DOM.
 */

/** Which item the detail pane should show: the explicit selection if it still
 * exists, else the entry marked active, else the first entry. */
export function resolveSelectedItem<T>(
    items: T[],
    selectedId: string | null,
    getKey: (item: T) => string,
    isActive?: (item: T) => boolean,
): T | undefined {
    return (
        items.find((item) => getKey(item) === selectedId) ??
        (isActive ? items.find(isActive) : undefined) ??
        items[0]
    );
}

/** The first key present in `items` but not in `previousIds` — i.e. what "Add" just
 * created, so the layout can auto-select its editor. `null` means "first render,
 * don't treat existing entries as new". */
export function detectNewItemId<T>(
    items: T[],
    getKey: (item: T) => string,
    previousIds: Set<string> | null,
): string | null {
    if (!previousIds) return null;
    for (const item of items) {
        const id = getKey(item);
        if (!previousIds.has(id)) return id;
    }
    return null;
}

/** Case-insensitive substring match across the text the row displays plus any extra
 * fields the caller opted in (e.g. a database name). */
export function filterItems<T>(
    items: T[],
    term: string,
    getFields: (item: T) => (string | null | undefined)[],
): T[] {
    const normalized = term.trim().toLowerCase();
    if (!normalized) return items;
    return items.filter((item) =>
        getFields(item)
            .map((f) => f ?? "")
            .join("\n")
            .toLowerCase()
            .includes(normalized),
    );
}

/** Bucket items by their group key, groups sorted alphabetically. Items keep their
 * original relative order inside each group. */
export function groupItems<T>(
    items: T[],
    getGroup: (item: T) => string,
): [string, T[]][] {
    const map = new Map<string, T[]>();
    for (const item of items) {
        const key = getGroup(item);
        map.set(key, [...(map.get(key) ?? []), item]);
    }
    return [...map.entries()].sort((a, b) => a[0].localeCompare(b[0]));
}
