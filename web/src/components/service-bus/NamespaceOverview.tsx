import { useMemo } from "react";
import { Mail, MailX, Clock, Inbox, ListTree, AlertCircle } from "lucide-react";
import { useSbNamespaceInfo, useSbQueues, useSbTopics } from "@/lib/hooks";
import { EmptyState } from "@/components/shared/EmptyState";
import type { SbEntityInfo, ServiceBusNamespace } from "@/lib/types";

interface Props {
  nsId: string | null;
  namespaces: ServiceBusNamespace[];
  onSelectEntity: (entity: SbEntityInfo, viewMode?: "active" | "dlq") => void;
}

/**
 * The pane shown before an entity is selected — a namespace-level summary plus
 * the entities carrying dead-letter backlog as one-click jump targets. Replaces
 * the bare "Select an entity" placeholder, which told the operator nothing.
 */
export function NamespaceOverview({ nsId, namespaces, onSelectEntity }: Props) {
  const ns = namespaces.find((n) => n.id === nsId);
  const { data: info } = useSbNamespaceInfo(nsId);
  const { data: queues, isLoading: queuesLoading, isError: queuesError } = useSbQueues(nsId);
  const { data: topics, isLoading: topicsLoading, isError: topicsError } = useSbTopics(nsId);

  const isLoading = queuesLoading || topicsLoading;
  const loadError = queuesError || topicsError;

  const totals = useMemo(() => {
    let active = 0;
    let dlq = 0;
    let scheduled = 0;
    for (const q of queues ?? []) {
      active += q.stats?.activeMessageCount ?? 0;
      dlq += q.stats?.deadLetterMessageCount ?? 0;
      scheduled += q.stats?.scheduledMessageCount ?? 0;
    }
    // Topics carry no active/scheduled count of their own — only the DLQ
    // rollup across their subscriptions.
    for (const t of topics ?? []) {
      dlq += t.subscriptionDeadLetterCount ?? 0;
    }
    return { active, dlq, scheduled };
  }, [queues, topics]);

  const dlqBacklog = useMemo(() => {
    const queuesWithDlq = (queues ?? []).filter((q) => (q.stats?.deadLetterMessageCount ?? 0) > 0);
    const topicsWithDlq = (topics ?? []).filter((t) => (t.subscriptionDeadLetterCount ?? 0) > 0);
    return { queues: queuesWithDlq, topics: topicsWithDlq };
  }, [queues, topics]);

  if (!nsId) {
    return (
      <EmptyState
        icon={Inbox}
        title="Select a namespace to get started"
        description="Pick a Service Bus namespace above to browse its queues, topics and subscriptions."
        testId="sb-overview-empty"
      />
    );
  }

  if (isLoading) {
    return (
      <div className="p-4 text-sm text-muted-foreground" data-testid="sb-overview-loading">
        Loading namespace overview...
      </div>
    );
  }

  if (loadError) {
    return (
      <div className="flex items-center gap-2 p-4 text-sm text-destructive" data-testid="sb-overview-error">
        <AlertCircle className="h-4 w-4 shrink-0" />
        <span>Couldn't load the entity list for this namespace.</span>
      </div>
    );
  }

  const dlqCount = dlqBacklog.queues.length + dlqBacklog.topics.length;

  return (
    <div className="flex-1 overflow-auto p-4" data-testid="sb-namespace-overview">
      <h2 className="text-sm font-semibold" data-testid="sb-overview-title">
        {ns?.alias ?? info?.name ?? "Namespace overview"}
      </h2>
      {info?.endpoint && <p className="mt-0.5 text-xs text-muted-foreground">{info.endpoint}</p>}

      {/* Totals */}
      <div className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
        <StatCard icon={ListTree} label="Queues" value={queues?.length ?? 0} testId="sb-overview-queues" />
        <StatCard icon={ListTree} label="Topics" value={topics?.length ?? 0} testId="sb-overview-topics" />
        <StatCard icon={Mail} label="Active messages" value={totals.active} testId="sb-overview-active" />
        <StatCard icon={MailX} label="Dead-lettered" value={totals.dlq} testId="sb-overview-dlq" highlight={totals.dlq > 0} />
        <StatCard icon={Clock} label="Scheduled" value={totals.scheduled} testId="sb-overview-scheduled" />
      </div>

      {/* DLQ backlog — the entities an operator most likely came here for. */}
      <div className="mt-6">
        <h3 className="text-xs font-semibold uppercase text-muted-foreground">Needs attention</h3>
        {dlqCount === 0 ? (
          <p className="mt-2 text-sm text-muted-foreground" data-testid="sb-overview-dlq-empty">
            No dead-lettered messages — select an entity on the left to browse it.
          </p>
        ) : (
          <div className="mt-2 divide-y rounded-md border" data-testid="sb-overview-dlq-list">
            {dlqBacklog.queues.map((q) => (
              <button
                key={q.entityPath}
                onClick={() => onSelectEntity(q, "dlq")}
                className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-accent"
                data-testid={`sb-overview-dlq-${q.entityPath}`}
              >
                <MailX className="h-4 w-4 shrink-0 text-destructive" />
                <span className="min-w-0 flex-1 truncate">{q.name}</span>
                <span className="rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
                  {q.stats?.deadLetterMessageCount}
                </span>
              </button>
            ))}
            {dlqBacklog.topics.map((t) => (
              <button
                key={t.entityPath}
                onClick={() => onSelectEntity(t)}
                className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-accent"
                data-testid={`sb-overview-dlq-${t.entityPath}`}
              >
                <MailX className="h-4 w-4 shrink-0 text-destructive" />
                <span className="min-w-0 flex-1 truncate">
                  {t.name} <span className="text-xs text-muted-foreground">(topic — expand to pick a subscription)</span>
                </span>
                <span className="rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
                  {t.subscriptionDeadLetterCount}
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function StatCard({
  icon: Icon,
  label,
  value,
  testId,
  highlight,
}: {
  icon: typeof Mail;
  label: string;
  value: number;
  testId: string;
  highlight?: boolean;
}) {
  return (
    <div
      className={`flex items-center gap-2 rounded-md border px-3 py-2 ${highlight ? "border-destructive/40 bg-destructive/10" : "bg-card"}`}
      data-testid={testId}
    >
      <Icon className={`h-4 w-4 shrink-0 ${highlight ? "text-destructive" : "text-muted-foreground"}`} />
      <div className="min-w-0">
        <div className="text-lg font-semibold leading-5">{value}</div>
        <div className="truncate text-xs text-muted-foreground">{label}</div>
      </div>
    </div>
  );
}
