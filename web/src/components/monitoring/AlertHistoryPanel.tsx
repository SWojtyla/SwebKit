import { useState } from "react";
import { BellOff } from "lucide-react";
import type { AlertFiredEvent } from "../../lib/api";

const severityBadge: Record<string, string> = {
  Critical: "text-red-500 bg-red-500/10",
  Warning: "text-yellow-500 bg-yellow-500/10",
};

export function AlertHistoryPanel({
  events,
  onSnooze,
}: {
  events: AlertFiredEvent[];
  onSnooze?: (evt: AlertFiredEvent) => void;
}) {
  const [snoozed, setSnoozed] = useState<Record<string, boolean>>({});

  if (events.length === 0) {
    return (
      <div className="rounded-lg border px-3 py-8 text-center text-sm text-muted-foreground" data-testid="monitoring-history-empty">
        No alert events yet
      </div>
    );
  }

  return (
    <div className="rounded-lg border" data-testid="monitoring-history-panel">
      <table className="w-full text-sm">
        <thead className="border-b bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Rule</th>
            <th className="px-3 py-2 text-left">Severity</th>
            <th className="px-3 py-2 text-left">Time</th>
            <th className="px-3 py-2 text-left">Message</th>
            <th className="px-3 py-2 text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          {events.map((evt, i) => {
            const key = `${evt.ruleId}-${evt.firedAt}-${i}`;
            if (snoozed[key]) return null;
            return (
              <tr key={key} className="border-b last:border-0" data-testid={`monitoring-history-row-${i}`}>
                <td className="px-3 py-2 font-medium">{evt.ruleName}</td>
                <td className="px-3 py-2">
                  <span className={`rounded px-2 py-0.5 text-xs ${severityBadge[evt.severity] ?? severityBadge.Warning}`}>
                    {evt.severity}
                  </span>
                </td>
                <td className="px-3 py-2 text-xs text-muted-foreground">
                  {new Date(evt.firedAt).toLocaleString()}
                </td>
                <td className="px-3 py-2 text-xs">
                  <div>{evt.message}</div>
                  {evt.detail && <div className="text-muted-foreground">{evt.detail}</div>}
                </td>
                <td className="px-3 py-2 text-right">
                  <button
                    onClick={() => {
                      setSnoozed((s) => ({ ...s, [key]: true }));
                      onSnooze?.(evt);
                    }}
                    className="rounded p-1 hover:bg-accent"
                    title="Snooze (this session)"
                    data-testid={`monitoring-history-snooze-${i}`}
                  >
                    <BellOff className="h-3.5 w-3.5" />
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
