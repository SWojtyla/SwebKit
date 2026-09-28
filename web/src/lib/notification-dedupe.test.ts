import { describe, it, expect } from "vitest";
import {
    notificationDedupeKey,
    findDedupeTarget,
    mergeNotification,
    mergeIntoHistory,
    type NotificationHistoryEntry,
} from "./notification-dedupe";
import type { NotificationItem } from "@/components/layout/notification-context";

function item(partial: Partial<NotificationItem> = {}): NotificationItem {
    return {
        id: partial.id ?? "id-1",
        type: "error",
        title: "Queue depth high",
        body: "orders/queue has 500 dead letters",
        timestamp: 1000,
        ...partial,
    };
}

function historyEntry(
    partial: Partial<NotificationHistoryEntry> = {},
): NotificationHistoryEntry {
    return { ...item(), read: false, ...partial };
}

describe("notificationDedupeKey", () => {
    it("is type|title|body", () => {
        expect(notificationDedupeKey(item())).toBe(
            "error|Queue depth high|orders/queue has 500 dead letters",
        );
    });

    it("treats a missing body as the empty string so undefined/'' merge", () => {
        const noBody = item({ body: undefined });
        const emptyBody = item({ body: "" });
        expect(notificationDedupeKey(noBody)).toBe(
            notificationDedupeKey(emptyBody),
        );
    });

    it("opts out when an action is present — Undo toasts each live", () => {
        expect(
            notificationDedupeKey(
                item({ action: { label: "Undo", onClick: () => {} } }),
            ),
        ).toBeNull();
    });
});

describe("findDedupeTarget", () => {
    it("returns the live item sharing the key", () => {
        const a = item({ id: "a" });
        const b = item({ id: "b", title: "Different" });
        expect(findDedupeTarget([a, b], notificationDedupeKey(a)!)).toBe(a);
        expect(
            findDedupeTarget([a, b], "info|nope|"),
        ).toBeNull();
    });

    it("skips action items even when type/title/body match", () => {
        const undo = item({
            id: "undo",
            action: { label: "Undo", onClick: () => {} },
        });
        expect(
            findDedupeTarget([undo], "error|Queue depth high|orders/queue has 500 dead letters"),
        ).toBeNull();
    });
});

describe("mergeNotification", () => {
    it("bumps count and refreshes the timestamp, keeping the toast's id", () => {
        const merged = mergeNotification(
            item({ id: "keep-me", timestamp: 1000 }),
            item({ id: "incoming", timestamp: 2000 }),
        );
        expect(merged.id).toBe("keep-me");
        expect(merged.count).toBe(2);
        expect(merged.timestamp).toBe(2000);
    });

    it("counts cumulatively across repeated bumps", () => {
        let live = item({ id: "live" });
        for (let i = 0; i < 4; i++) {
            live = mergeNotification(live, item({ timestamp: 2000 + i }));
        }
        expect(live.count).toBe(5);
    });

    it("prefers the incoming link but keeps the old one when absent", () => {
        const withLink = mergeNotification(
            item({ link: "/monitoring" }),
            item({}),
        );
        expect(withLink.link).toBe("/monitoring");
        const refreshed = mergeNotification(
            item({ link: "/monitoring" }),
            item({ link: "/sql" }),
        );
        expect(refreshed.link).toBe("/sql");
    });
});

describe("mergeIntoHistory", () => {
    it("prepends an unread row when history is empty", () => {
        const result = mergeIntoHistory([], item({ id: "n1" }));
        expect(result).toHaveLength(1);
        expect(result[0]).toMatchObject({ id: "n1", read: false });
    });

    it("folds into an existing unread row with the same key instead of duplicating", () => {
        const history = [
            historyEntry({ id: "other", title: "Different" }),
            historyEntry({ id: "same", count: 2, timestamp: 500 }),
        ];
        const result = mergeIntoHistory(
            history,
            item({ id: "toast", count: 3, timestamp: 9000 }),
        );
        expect(result).toHaveLength(2);
        // Counts add (2 prior firings + the toast's own 3) and the merged row
        // resurfaces at the top with the fresh timestamp.
        expect(result[0]).toMatchObject({
            id: "same",
            count: 5,
            timestamp: 9000,
            read: false,
        });
        expect(result[1].id).toBe("other");
    });

    it("does NOT merge into a read row — the user already saw that batch", () => {
        const history = [historyEntry({ id: "seen", read: true })];
        const result = mergeIntoHistory(history, item({ id: "fresh" }));
        expect(result).toHaveLength(2);
        expect(result[0].id).toBe("fresh");
        expect(result[1]).toMatchObject({ id: "seen", read: true });
        expect(result[1].count).toBeUndefined();
    });

    it("action toasts never merge, even with a same-key unread row", () => {
        const action = { label: "Undo", onClick: () => {} };
        const history = [
            historyEntry({ id: "existing", action }),
        ];
        const result = mergeIntoHistory(
            history,
            item({ id: "another-undo", action }),
        );
        expect(result).toHaveLength(2);
        expect(result[0].id).toBe("another-undo");
    });

    it("respects the history cap", () => {
        const full = Array.from({ length: 50 }, (_, i) =>
            historyEntry({ id: `h-${i}`, title: `Old ${i}` }),
        );
        const result = mergeIntoHistory(full, item({ id: "new" }));
        expect(result).toHaveLength(50);
        expect(result[0].id).toBe("new");
    });
});
