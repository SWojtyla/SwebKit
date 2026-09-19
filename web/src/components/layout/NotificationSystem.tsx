import { useState, createContext, useContext, type ReactNode } from "react";
import { useNavigate } from "react-router";
import { X, CheckCircle, AlertCircle, Info, Bell, CheckCheck, Trash2 } from "lucide-react";

type NotificationType = "success" | "error" | "info";

interface NotificationAction {
  label: string;
  onClick: () => void;
}

interface NotificationItem {
  id: string;
  type: NotificationType;
  title: string;
  body?: string;
  timestamp: number;
  /** Optional recovery action rendered as a button on the toast (e.g. "Undo"). Never persisted to history. */
  action?: NotificationAction;
  /** Route to navigate to when the history entry is clicked (e.g. "/monitoring" for a fired alert). */
  link?: string;
}

interface NotificationContextValue {
  notify: (type: NotificationType, title: string, body?: string, action?: NotificationAction, link?: string) => void;
  notifications: NotificationItem[];
  dismiss: (id: string) => void;
}

const NotificationContext = createContext<NotificationContextValue | null>(null);

export function useNotification() {
  const ctx = useContext(NotificationContext);
  if (!ctx) throw new Error("useNotification must be used within NotificationProvider");
  return ctx;
}

interface HistoryItem extends NotificationItem {
  read: boolean;
}

export function NotificationProvider({ children }: { children: ReactNode }) {
  const [notifications, setNotifications] = useState<NotificationItem[]>([]);
  const [showHistory, setShowHistory] = useState(false);
  const [history, setHistory] = useState<HistoryItem[]>([]);
  const navigate = useNavigate();

  const unreadCount = history.filter((n) => !n.read).length;

  const dismiss = (id: string) => {
    setNotifications((prev) => {
      const item = prev.find((n) => n.id === id);
      if (item) setHistory((h) => [{ ...item, read: false }, ...h].slice(0, 50));
      return prev.filter((n) => n.id !== id);
    });
  };

  const notify = (type: NotificationType, title: string, body?: string, action?: NotificationAction, link?: string) => {
    const id = crypto.randomUUID();
    const item: NotificationItem = { id, type, title, body, timestamp: Date.now(), action, link };
    setNotifications((prev) => [...prev, item]);
    setTimeout(() => dismiss(id), 5000);
  };

  const markRead = (id: string) =>
    setHistory((h) => h.map((n) => (n.id === id ? { ...n, read: true } : n)));

  const markAllRead = () => setHistory((h) => h.map((n) => ({ ...n, read: true })));

  const clearHistory = () => setHistory([]);

  const openItem = (item: HistoryItem) => {
    markRead(item.id);
    if (item.link) {
      setShowHistory(false);
      navigate(item.link);
    }
  };

  return (
    <NotificationContext.Provider value={{ notify, notifications, dismiss }}>
      {children}
      {/* Toast notifications */}
      <div className="fixed bottom-4 right-4 z-50 space-y-2" data-testid="notification-toasts">
        {notifications.map((n) => (
          <Toast key={n.id} notification={n} onDismiss={() => dismiss(n.id)} />
        ))}
      </div>
      {/* Notification bell + history */}
      <div className="fixed bottom-4 left-4 z-50">
        <button
          onClick={() => setShowHistory(!showHistory)}
          className="relative rounded-full border bg-card p-2 shadow-md hover:bg-accent"
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
          <div className="absolute bottom-10 left-0 w-80 rounded-lg border bg-card shadow-lg" data-testid="notification-history">
            <div className="flex items-center justify-between border-b px-3 py-2">
              <span className="text-sm font-semibold">Notifications</span>
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
                <div className="px-3 py-4 text-center text-sm text-muted-foreground">No notifications</div>
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
                      <span className={`text-sm ${n.read ? "font-normal" : "font-medium"}`}>{n.title}</span>
                      {!n.read && <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-primary" data-testid="notification-unread-dot" />}
                      <span className="ml-auto text-xs text-muted-foreground">
                        {new Date(n.timestamp).toLocaleTimeString()}
                      </span>
                    </div>
                    {n.body && <p className="mt-1 text-xs text-muted-foreground">{n.body}</p>}
                  </button>
                ))
              )}
            </div>
          </div>
        )}
      </div>
    </NotificationContext.Provider>
  );
}

function Toast({ notification, onDismiss }: { notification: NotificationItem; onDismiss: () => void }) {
  return (
    <div
      className={`flex w-80 items-start gap-2 rounded-lg border bg-card p-3 shadow-lg ${
        notification.type === "error" ? "border-destructive/30" : notification.type === "success" ? "border-success/30" : ""
      }`}
      data-testid={`notification-toast-${notification.id}`}
    >
      <NotificationIcon type={notification.type} />
      <div className="flex-1">
        <div className="text-sm font-medium">{notification.title}</div>
        {notification.body && <div className="mt-0.5 text-xs text-muted-foreground">{notification.body}</div>}
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
      <button onClick={onDismiss} className="text-muted-foreground hover:text-foreground" data-testid={`notification-dismiss-${notification.id}`}>
        <X className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}

function NotificationIcon({ type }: { type: NotificationType }) {
  if (type === "success") return <CheckCircle className="h-4 w-4 text-success" />;
  if (type === "error") return <AlertCircle className="h-4 w-4 text-destructive" />;
  return <Info className="h-4 w-4 text-blue-500" />;
}
