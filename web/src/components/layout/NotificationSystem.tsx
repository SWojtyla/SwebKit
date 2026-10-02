import { useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useNavigate } from "react-router";
import {
    X,
    CheckCircle,
    AlertCircle,
    Info,
    Bell,
    CheckCheck,
    Trash2,
} from "lucide-react";
import { formatLocalTime } from "@/lib/datetime";
import {
    findDedupeTarget,
    mergeIntoHistory,
    mergeNotification,
    notificationDedupeKey,
} from "@/lib/notification-dedupe";
import {
    NotificationContext,
    type NotificationAction,
    type NotificationItem,
    type NotificationType,
} from "./notification-context";
import {
    getNotificationBellSlot,
    subscribeNotificationBellSlot,
} from "./notification-bell-slot";

interface HistoryItem extends NotificationItem {
    read: boolean;
}

// Module-level factory: impure builtins (Date.now, randomUUID) belong outside the
// component so the render-purity analyzer doesn't flag them — notify only ever runs
// from event handlers anyway.
function createNotification(
    type: NotificationType,
    title: string,
    body?: string,
    action?: NotificationAction,
    link?: string,
): NotificationItem {
    return {
        id: crypto.randomUUID(),
        type,
        title,
        body,
        timestamp: Date.now(),
        action,
        link,
    };
}

export function NotificationProvider({ children }: { children: ReactNode }) {
    const [notifications, setNotifications] = useState<NotificationItem[]>([]);
    const [showHistory, setShowHistory] = useState(false);
    const [history, setHistory] = useState<HistoryItem[]>([]);
    const navigate = useNavigate();

    // The bell docks into the sidebar footer slot rendered by AppLayout so it shares
    // layout with the pinned rail — a fixed bottom-left overlay used to cover the
    // last pinned item. The slot registers through a callback ref (AppLayout mounts
    // inside a Suspense boundary that can defer its first commit), so subscribing
    // beats a one-shot DOM lookup. If no slot exists — a shell that doesn't render
    // AppLayout — the bell falls back to the old fixed position.
    const bellHost = useSyncExternalStore(
        subscribeNotificationBellSlot,
        getNotificationBellSlot,
    );

    const unreadCount = history.filter((n) => !n.read).length;

    // Active toasts by id, so `dismiss` can move an item to history without reading
    // state inside another setState updater — StrictMode double-invokes updaters,
    // which used to push the same toast into the bell's history twice.
    const activeItems = useRef(new Map<string, NotificationItem>());
    // Per-toast auto-dismiss timer ids — dedupe bumps re-arm the SAME toast's
    // timer, so the id has to be tracked to cancel it.
    const dismissTimers = useRef(
        new Map<string, ReturnType<typeof setTimeout>>(),
    );

    const dismiss = (id: string) => {
        // The delete also makes dismiss idempotent: a manual close racing the
        // auto-expire timer can't record the entry twice.
        const timer = dismissTimers.current.get(id);
        if (timer !== undefined) {
            clearTimeout(timer);
            dismissTimers.current.delete(id);
        }
        const item = activeItems.current.get(id);
        if (item) {
            activeItems.current.delete(id);
            // Same-key unread rows absorb the dismissal (count adds, timestamp
            // refreshes) rather than stacking one entry per firing.
            setHistory((h) => mergeIntoHistory(h, item));
        }
        setNotifications((prev) => prev.filter((n) => n.id !== id));
    };

    const notify = (
        type: NotificationType,
        title: string,
        body?: string,
        action?: NotificationAction,
        link?: string,
    ) => {
        const item = createNotification(type, title, body, action, link);
        const key = notificationDedupeKey(item);
        if (key !== null) {
            const existing = findDedupeTarget(
                activeItems.current.values(),
                key,
            );
            if (existing) {
                // Same toast firing again (e.g. a monitoring alert each eval
                // tick): bump ×N, refresh the timestamp and re-arm the 5s timer
                // so a burst expires 5s after its LAST firing, not mid-burst.
                const merged = mergeNotification(existing, item);
                activeItems.current.set(existing.id, merged);
                setNotifications((prev) =>
                    prev.map((n) => (n.id === existing.id ? merged : n)),
                );
                const timer = dismissTimers.current.get(existing.id);
                if (timer !== undefined) clearTimeout(timer);
                dismissTimers.current.set(
                    existing.id,
                    setTimeout(() => dismiss(existing.id), 5000),
                );
                return;
            }
        }
        const id = item.id;
        activeItems.current.set(id, item);
        setNotifications((prev) => [...prev, item]);
        dismissTimers.current.set(
            id,
            setTimeout(() => dismiss(id), 5000),
        );
    };

    const markRead = (id: string) =>
        setHistory((h) =>
            h.map((n) => (n.id === id ? { ...n, read: true } : n)),
        );

    const markAllRead = () =>
        setHistory((h) => h.map((n) => ({ ...n, read: true })));

    const clearHistory = () => setHistory([]);

    const openItem = (item: HistoryItem) => {
        markRead(item.id);
        if (item.link) {
            setShowHistory(false);
            navigate(item.link);
        }
    };

    const bell = (
        <div className="relative">
            <button
                onClick={() => setShowHistory(!showHistory)}
                className="relative flex h-9 w-9 items-center justify-center rounded-md border bg-popover text-sidebar-foreground shadow-sm transition-colors hover:bg-sidebar-active"
                title="Notifications"
                data-testid="notification-bell"
            >
                <Bell className="h-4 w-4" />
                {unreadCount > 0 && (
                    <span
                        className="absolute -right-1 -top-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-0.5 text-[10px] text-primary-foreground"
                        data-testid="notification-unread-badge"
                    >
                        {unreadCount > 99 ? "99+" : unreadCount}
                    </span>
                )}
            </button>
            {showHistory && (
                <div
                    className="absolute bottom-full left-0 z-50 mb-2 w-80 rounded-lg border bg-popover shadow-lg"
                    data-testid="notification-history"
                >
                    <div className="flex items-center justify-between border-b px-3 py-2">
                        <span className="text-sm font-semibold">
                            Notifications
                        </span>
                        <div className="flex items-center gap-1">
                            <button
                                onClick={markAllRead}
                                disabled={unreadCount === 0}
                                className="rounded p-1 text-muted-foreground hover:text-foreground disabled:opacity-40"
                                title="Mark all read"
                                data-testid="notification-mark-all-read"
                            >
                                <CheckCheck className="h-3.5 w-3.5" />
                            </button>
                            <button
                                onClick={clearHistory}
                                disabled={history.length === 0}
                                className="rounded p-1 text-muted-foreground hover:text-foreground disabled:opacity-40"
                                title="Dismiss all"
                                data-testid="notification-clear-all"
                            >
                                <Trash2 className="h-3.5 w-3.5" />
                            </button>
                            <button
                                onClick={() => setShowHistory(false)}
                                className="rounded p-1 text-muted-foreground hover:text-foreground"
                                title="Close"
                                data-testid="notification-history-close"
                            >
                                <X className="h-3.5 w-3.5" />
                            </button>
                        </div>
                    </div>
                    <div className="max-h-80 overflow-auto">
                        {history.length === 0 ? (
                            <div className="px-3 py-4 text-center text-sm text-muted-foreground">
                                No notifications
                            </div>
                        ) : (
                            history.map((n) => (
                                <button
                                    key={n.id}
                                    onClick={() => openItem(n)}
                                    className={`block w-full border-b px-3 py-2 text-left last:border-0 hover:bg-accent/50 ${
                                        n.read ? "opacity-60" : ""
                                    }`}
                                    data-testid={`notification-item-${n.id}`}
                                >
                                    <div className="flex items-center gap-2">
                                        <NotificationIcon type={n.type} />
                                        <span
                                            className={`text-sm ${n.read ? "font-normal" : "font-medium"}`}
                                        >
                                            {n.title}
                                        </span>
                                        {(n.count ?? 1) > 1 && (
                                            <span
                                                className="rounded bg-muted px-1 text-[10px] font-semibold text-muted-foreground"
                                                data-testid={`notification-history-count-${n.id}`}
                                            >
                                                ×{n.count}
                                            </span>
                                        )}
                                        {!n.read && (
                                            <span
                                                className="h-1.5 w-1.5 shrink-0 rounded-full bg-primary"
                                                data-testid="notification-unread-dot"
                                            />
                                        )}
                                        <span className="ml-auto text-xs text-muted-foreground">
                                            {formatLocalTime(n.timestamp)}
                                        </span>
                                    </div>
                                    {n.body && (
                                        <p className="mt-1 text-xs text-muted-foreground">
                                            {n.body}
                                        </p>
                                    )}
                                </button>
                            ))
                        )}
                    </div>
                </div>
            )}
        </div>
    );

    return (
        <NotificationContext.Provider
            value={{ notify, notifications, dismiss }}
        >
            {children}
            {/* Toast notifications */}
            <div
                className="fixed bottom-4 right-4 z-50 space-y-2"
                data-testid="notification-toasts"
            >
                {notifications.map((n) => (
                    <Toast
                        key={n.id}
                        notification={n}
                        onDismiss={() => dismiss(n.id)}
                    />
                ))}
            </div>
            {/* Notification bell — docked into the sidebar slot when AppLayout
                renders one, fixed-position fallback otherwise. */}
            {bellHost ? (
                createPortal(bell, bellHost)
            ) : (
                <div className="fixed bottom-4 left-4 z-50">{bell}</div>
            )}
        </NotificationContext.Provider>
    );
}

function Toast({
    notification,
    onDismiss,
}: {
    notification: NotificationItem;
    onDismiss: () => void;
}) {
    return (
        <div
            className={`flex w-80 items-start gap-2 rounded-lg border bg-card p-3 shadow-lg ${
                notification.type === "error"
                    ? "border-destructive/30"
                    : notification.type === "success"
                      ? "border-success/30"
                      : ""
            }`}
            data-testid={`notification-toast-${notification.id}`}
        >
            <NotificationIcon type={notification.type} />
            <div className="flex-1">
                <div className="text-sm font-medium">
                    {notification.title}
                    {(notification.count ?? 1) > 1 && (
                        <span
                            className="ml-1.5 rounded bg-muted px-1 py-0.5 text-[10px] font-semibold text-muted-foreground"
                            data-testid={`notification-count-${notification.id}`}
                        >
                            ×{notification.count}
                        </span>
                    )}
                </div>
                {notification.body && (
                    <div className="mt-0.5 whitespace-pre-line text-xs text-muted-foreground">
                        {notification.body}
                    </div>
                )}
                {notification.action && (
                    <button
                        onClick={() => {
                            notification.action?.onClick();
                            onDismiss();
                        }}
                        className="mt-1 text-xs font-medium text-primary hover:underline"
                        data-testid={`notification-action-${notification.id}`}
                    >
                        {notification.action.label}
                    </button>
                )}
            </div>
            <button
                onClick={onDismiss}
                className="text-muted-foreground hover:text-foreground"
                data-testid={`notification-dismiss-${notification.id}`}
            >
                <X className="h-3.5 w-3.5" />
            </button>
        </div>
    );
}

function NotificationIcon({ type }: { type: NotificationType }) {
    if (type === "success")
        return <CheckCircle className="h-4 w-4 text-success" />;
    if (type === "error")
        return <AlertCircle className="h-4 w-4 text-destructive" />;
    return <Info className="h-4 w-4 text-blue-500" />;
}
