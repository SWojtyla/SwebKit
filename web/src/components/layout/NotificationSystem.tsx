import { useState, createContext, useContext, type ReactNode } from "react";
import { X, CheckCircle, AlertCircle, Info, Bell } from "lucide-react";

type NotificationType = "success" | "error" | "info";

interface NotificationItem {
  id: number;
  type: NotificationType;
  title: string;
  body?: string;
  timestamp: number;
}

interface NotificationContextValue {
  notify: (type: NotificationType, title: string, body?: string) => void;
  notifications: NotificationItem[];
  dismiss: (id: number) => void;
}

const NotificationContext = createContext<NotificationContextValue | null>(null);

export function useNotification() {
  const ctx = useContext(NotificationContext);
  if (!ctx) throw new Error("useNotification must be used within NotificationProvider");
  return ctx;
}

export function NotificationProvider({ children }: { children: ReactNode }) {
  const [notifications, setNotifications] = useState<NotificationItem[]>([]);
  const [showHistory, setShowHistory] = useState(false);
  const [history, setHistory] = useState<NotificationItem[]>([]);

  const dismiss = (id: number) => {
    setNotifications((prev) => {
      const item = prev.find((n) => n.id === id);
      if (item) setHistory((h) => [{ ...item }, ...h].slice(0, 50));
      return prev.filter((n) => n.id !== id);
    });
  };

  const notify = (type: NotificationType, title: string, body?: string) => {
    const id = Date.now() + Math.random();
    const item: NotificationItem = { id, type, title, body, timestamp: Date.now() };
    setNotifications((prev) => [...prev, item]);
    setTimeout(() => dismiss(id), 5000);
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
          data-testid="notification-bell"
        >
          <Bell className="h-4 w-4" />
          {history.length > 0 && (
            <span className="absolute -right-1 -top-1 flex h-4 w-4 items-center justify-center rounded-full bg-primary text-[10px] text-primary-foreground">
              {history.length}
            </span>
          )}
        </button>
        {showHistory && (
          <div className="absolute bottom-10 left-0 w-80 rounded-lg border bg-card shadow-lg" data-testid="notification-history">
            <div className="flex items-center justify-between border-b px-3 py-2">
              <span className="text-sm font-semibold">Notifications</span>
              <button onClick={() => setShowHistory(false)} className="text-muted-foreground hover:text-foreground">
                <X className="h-3.5 w-3.5" />
              </button>
            </div>
            <div className="max-h-80 overflow-auto">
              {history.length === 0 ? (
                <div className="px-3 py-4 text-center text-sm text-muted-foreground">No notifications</div>
              ) : (
                history.map((n) => (
                  <div key={n.id} className="border-b last:border-0 px-3 py-2">
                    <div className="flex items-center gap-2">
                      <NotificationIcon type={n.type} />
                      <span className="text-sm font-medium">{n.title}</span>
                      <span className="ml-auto text-xs text-muted-foreground">
                        {new Date(n.timestamp).toLocaleTimeString()}
                      </span>
                    </div>
                    {n.body && <p className="mt-1 text-xs text-muted-foreground">{n.body}</p>}
                  </div>
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
        notification.type === "error" ? "border-destructive/30" : notification.type === "success" ? "border-green-500/30" : ""
      }`}
      data-testid={`notification-toast-${notification.id}`}
    >
      <NotificationIcon type={notification.type} />
      <div className="flex-1">
        <div className="text-sm font-medium">{notification.title}</div>
        {notification.body && <div className="mt-0.5 text-xs text-muted-foreground">{notification.body}</div>}
      </div>
      <button onClick={onDismiss} className="text-muted-foreground hover:text-foreground" data-testid={`notification-dismiss-${notification.id}`}>
        <X className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}

function NotificationIcon({ type }: { type: NotificationType }) {
  if (type === "success") return <CheckCircle className="h-4 w-4 text-green-500" />;
  if (type === "error") return <AlertCircle className="h-4 w-4 text-destructive" />;
  return <Info className="h-4 w-4 text-blue-500" />;
}
