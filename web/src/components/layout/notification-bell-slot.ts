// Hand-rolled external store holding the DOM node the notification bell portals
// into. AppLayout registers its sidebar slot via a callback ref; NotificationSystem
// subscribes so the bell docks wherever the shell places the slot. A one-shot
// `getElementById` in an effect is NOT enough: AppLayout lives inside a Suspense
// boundary whose lazy routes can defer its first commit until after the
// provider's effects have already run.
let host: HTMLElement | null = null;
const listeners = new Set<() => void>();

export function setNotificationBellSlot(el: HTMLElement | null) {
    if (host === el) return;
    host = el;
    listeners.forEach((l) => l());
}

export function subscribeNotificationBellSlot(cb: () => void) {
    listeners.add(cb);
    return () => {
        listeners.delete(cb);
    };
}

export function getNotificationBellSlot() {
    return host;
}
