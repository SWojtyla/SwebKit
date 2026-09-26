import { createContext, useContext } from "react";

export type NotificationType = "success" | "error" | "info";

export interface NotificationAction {
    label: string;
    onClick: () => void;
}

export interface NotificationItem {
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

export interface NotificationContextValue {
    notify: (
        type: NotificationType,
        title: string,
        body?: string,
        action?: NotificationAction,
        link?: string,
    ) => void;
    notifications: NotificationItem[];
    dismiss: (id: string) => void;
}

export const NotificationContext = createContext<NotificationContextValue | null>(
    null,
);

export function useNotification() {
    const ctx = useContext(NotificationContext);
    if (!ctx)
        throw new Error(
            "useNotification must be used within NotificationProvider",
        );
    return ctx;
}
