import { useEffect, useState } from "react";
import { useRespondAcpPermission } from "@/lib/hooks/useAgent";
import { formatExpiryCountdown } from "./PendingActionCard";
import type { AcpPermission } from "@/lib/types";

/** Ticks once a second so the expiry countdown actually counts down. */
function useNow(intervalMs = 1000): number {
 const [now, setNow] = useState(() => Date.now());
 useEffect(() => {
  const id = setInterval(() => setNow(Date.now()), intervalMs);
  return () => clearInterval(id);
 }, [intervalMs]);
 return now;
}

function optionClassName(kind: string | null): string {
 if (kind?.startsWith("reject"))
  return "rounded-md border px-3 py-1 text-xs hover:bg-accent disabled:opacity-50";
 if (kind === "allow_always")
  return "rounded-md bg-secondary px-3 py-1 text-xs font-medium text-secondary-foreground hover:opacity-90 disabled:opacity-50";
 return "rounded-md bg-primary px-3 py-1 text-xs font-medium text-primary-foreground hover:opacity-90 disabled:opacity-50";
}

/**
 * The approval surface for an ACP agent's `session/request_permission` — rendered only when the
 * active ACP profile has "Ask before the agent runs a tool call" on (otherwise the sidecar
 * auto-approves and nothing ever lands here). Each option the agent offered becomes one button;
 * clicking it resolves the parked JSON-RPC call and unblocks the agent's turn.
 */
export function AcpPermissionCard({ permission }: { permission: AcpPermission }) {
 const respond = useRespondAcpPermission();
 const now = useNow();

 return (
  <div
   className="space-y-2 rounded-lg border border-primary/30 bg-primary/5 p-3"
   data-testid={`acp-permission-${permission.id}`}
  >
   <p className="text-sm font-medium">
    {permission.toolCallTitle}
   </p>
   <p className="text-xs text-muted-foreground">
    The external agent is asking permission to continue.
   </p>
   {respond.isError ? (
    <div className="rounded-md border border-destructive/40 bg-destructive/10 p-2 text-xs text-destructive">
     Couldn&apos;t send your answer:{" "}
     {respond.error instanceof Error
      ? respond.error.message
      : String(respond.error)}
    </div>
   ) : (
    <div className="flex flex-wrap items-center gap-2">
     {permission.options.map((opt) => (
      <button
       key={opt.optionId}
       onClick={() =>
        respond.mutate({ id: permission.id, optionId: opt.optionId })
       }
       disabled={respond.isPending}
       className={optionClassName(opt.kind)}
       data-testid={`acp-permission-option-${permission.id}-${opt.optionId}`}
      >
       {respond.isPending ? "Sending…" : opt.name}
      </button>
     ))}
     <span className="text-xs text-muted-foreground">
      {formatExpiryCountdown(permission.expiresAt, now)}
     </span>
    </div>
   )}
  </div>
 );
}
