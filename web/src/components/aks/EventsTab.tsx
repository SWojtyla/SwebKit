import { useAksEvents } from "@/lib/hooks";

export function EventsTab({ ns, isMulti }: { ns: string; isMulti?: boolean }) {
  const { data: events, isLoading } = useAksEvents(ns);

  if (isLoading) return <div className="p-4 text-sm text-muted-foreground">Loading...</div>;
  if (!events || events.length === 0)
    return <div className="p-4 text-sm text-muted-foreground">No events found</div>;

  return (
    <div className="p-4" data-testid="events-list">
      <div className="space-y-1">
        {events.map((evt) => (
          <div
            key={`${evt.namespace}/${evt.name}-${evt.involvedObjectName}`}
            data-testid={`event-item-${evt.name}`}
            className="flex items-start gap-3 rounded-md border p-2 text-sm"
          >
            <span
              className={`mt-0.5 shrink-0 rounded px-1.5 py-0.5 text-xs font-medium ${
                evt.type === "Warning"
                  ? "bg-warning/20 text-warning"
                  : "bg-success/20 text-success"
              }`}
            >
              {evt.type}
            </span>
            <div className="flex-1">
              <div className="flex items-center gap-2">
                {isMulti && <span className="text-xs text-muted-foreground">{evt.namespace}</span>}
                <span className="font-medium">{evt.reason}</span>
                {evt.involvedObjectName && (
                  <span className="text-xs text-muted-foreground">
                    {evt.involvedObjectKind}/{evt.involvedObjectName}
                  </span>
                )}
                {evt.count > 1 && (
                  <span className="text-xs text-muted-foreground">×{evt.count}</span>
                )}
              </div>
              <p className="mt-0.5 text-xs text-muted-foreground">{evt.message}</p>
            </div>
            <span className="shrink-0 text-xs text-muted-foreground">
              {evt.lastTimestamp ? new Date(evt.lastTimestamp).toLocaleTimeString() : ""}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
