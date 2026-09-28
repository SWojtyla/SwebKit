/**
 * Toast/notification dedupe (ux-power-pack §5).
 *
 * Pure merge logic extracted from NotificationSystem so it can run under vitest
 * (the repo's unit suite is pure-function only — keep React/DOM out of here;
 * the NotificationItem import is type-only and erased at compile).
 *
 * A burst of identical notifications — a monitoring rule firing every eval
 * tick is the canonical source — collapses into one toast whose `count` bumps
 * and whose dismiss timer re-arms, instead of stacking N identical toasts.
 * Toasts carrying an `action` (e.g. Undo) opt out entirely: each one is its
 * own undoable event and must live independently.
 */

import type { NotificationItem } from "@/components/layout/notification-context";

/** History entries are dismissed toasts plus a read marker (NotificationSystem's HistoryItem). */
export interface NotificationHistoryEntry extends NotificationItem {
    read: boolean;
}

/**
 * Dedupe key `${type}|${title}|${body}` — null when the item must never merge.
 * `action` is part of the *opt-out*, not the key: two callbacks are never
 * interchangeable even when labels match, so action toasts return null rather
 * than a key that includes a label.
 */
export function notificationDedupeKey(
    item: Pick<NotificationItem, "type" | "title" | "body" | "action">,
): string | null {
    if (item.action) return null;
    return `${item.type}|${item.title}|${item.body ?? ""}`;
}

/** First live item sharing `key`, or null. Scans a Map's values()/array alike. */
export function findDedupeTarget(
    items: Iterable<NotificationItem>,
    key: string,
): NotificationItem | null {
    for (const candidate of items) {
        if (notificationDedupeKey(candidate) === key) return candidate;
    }
    return null;
}

/**
 * Fold an incoming same-key notification into the live one: bump the count,
 * refresh the timestamp, keep the newest link. Title/body/type are identical
 * by definition (that's what the key means). The caller re-arms the toast's
 * dismiss timer — staying pure, this only computes the merged item.
 */
export function mergeNotification(
    existing: NotificationItem,
    incoming: NotificationItem,
): NotificationItem {
    return {
        ...existing,
        count: (existing.count ?? 1) + 1,
        timestamp: incoming.timestamp,
        link: incoming.link ?? existing.link,
    };
}

/**
 * Move a dismissed toast into the history list. An existing UNREAD row with the
 * same dedupe key absorbs it — counts add, the timestamp refreshes and the row
 * resurfaces at the top — instead of stacking a duplicate entry per firing.
 * Read rows are left alone: the user already saw that batch, a fresh entry is
 * honest. `action` items (key=null) always prepend a new row.
 */
export function mergeIntoHistory(
    history: NotificationHistoryEntry[],
    item: NotificationItem,
    limit = 50,
): NotificationHistoryEntry[] {
    const key = notificationDedupeKey(item);
    if (key !== null) {
        const index = history.findIndex(
            (entry) => !entry.read && notificationDedupeKey(entry) === key,
        );
        if (index >= 0) {
            const existing = history[index];
            const merged: NotificationHistoryEntry = {
                ...existing,
                count: (existing.count ?? 1) + (item.count ?? 1),
                timestamp: item.timestamp,
                link: item.link ?? existing.link,
            };
            return [
                merged,
                ...history.slice(0, index),
                ...history.slice(index + 1),
            ].slice(0, limit);
        }
    }
    return [{ ...item, read: false }, ...history].slice(0, limit);
}
