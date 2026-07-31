import { useState, useMemo } from "react";
import { ChevronRight, ChevronDown, Mail, MailX, Folder, Search, ArrowUp, ArrowDown } from "lucide-react";
import { useSbQueues, useSbTopics, useSbSubscriptions } from "@/lib/hooks";
import type { SbEntityInfo } from "@/lib/types";

interface Props {
  nsId: string | null;
  selectedEntity: SbEntityInfo | null;
  onSelectEntity: (entity: SbEntityInfo, viewMode?: "active" | "dlq") => void;
}

type SortCol = "name" | "active" | "dlq" | "sched";

function EntityStatsBadges({
  entity,
  onSelectEntity,
}: {
  entity: SbEntityInfo;
  onSelectEntity: (entity: SbEntityInfo, viewMode?: "active" | "dlq") => void;
}) {
  const CountBadge = ({
    count,
    mode,
  }: {
    count: number | undefined;
    mode?: "active" | "dlq";
  }) => {
    const value = count ?? 0;
    if (value > 0 && mode) {
      return (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onSelectEntity(entity, mode); }}
          className={`rounded px-1.5 py-0.5 hover:opacity-80 ${
            mode === "dlq"
              ? "bg-destructive/20 text-destructive"
              : "bg-secondary text-secondary-foreground"
          }`}
          title={`Open ${mode === "dlq" ? "dead-letter" : "active"} messages`}
        >
          {value}
        </button>
      );
    }
    return (
      <span
        className={`rounded px-1.5 py-0.5 ${
          value > 0 ? "bg-muted text-muted-foreground" : "text-muted-foreground"
        }`}
      >
        {value > 0 ? value : "–"}
      </span>
    );
  };

  if (entity.isTopic) {
    return (
      <span className="ml-auto flex gap-1 text-xs text-muted-foreground">
        <span className="rounded px-1.5 py-0.5">–</span>
        <span className="rounded px-1.5 py-0.5">–</span>
        <span className="rounded px-1.5 py-0.5">–</span>
      </span>
    );
  }

  if (!entity.stats) {
    return (
      <span className="ml-auto flex gap-1 text-xs text-muted-foreground">
        <span className="rounded px-1.5 py-0.5">·</span>
        <span className="rounded px-1.5 py-0.5">·</span>
        <span className="rounded px-1.5 py-0.5">·</span>
      </span>
    );
  }

  return (
    <span className="ml-auto flex gap-1 text-xs">
      <CountBadge count={entity.stats.activeMessageCount} mode="active" />
      <CountBadge count={entity.stats.deadLetterMessageCount} mode="dlq" />
      <CountBadge count={entity.stats.scheduledMessageCount} />
    </span>
  );
}

export function EntityTree({ nsId, selectedEntity, onSelectEntity }: Props) {
  const { data: queues, isLoading: queuesLoading } = useSbQueues(nsId);
  const { data: topics, isLoading: topicsLoading } = useSbTopics(nsId);
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set());
  const [filter, setFilter] = useState("");
  const [sortCol, setSortCol] = useState<SortCol>("name");
  const [sortAsc, setSortAsc] = useState(true);

  const toggleSort = (col: SortCol) => {
    if (sortCol === col) setSortAsc(!sortAsc);
    else { setSortCol(col); setSortAsc(col !== "dlq" && col !== "active"); }
  };

  const sortItems = (items: SbEntityInfo[]) => {
    const filtered = filter.trim()
      ? items.filter((e) => e.name.toLowerCase().includes(filter.toLowerCase()))
      : [...items];
    return filtered.sort((a, b) => {
      let cmp = 0;
      switch (sortCol) {
        case "active": cmp = (a.stats?.activeMessageCount ?? 0) - (b.stats?.activeMessageCount ?? 0); break;
        case "dlq": cmp = (a.stats?.deadLetterMessageCount ?? 0) - (b.stats?.deadLetterMessageCount ?? 0); break;
        case "sched": cmp = (a.stats?.scheduledMessageCount ?? 0) - (b.stats?.scheduledMessageCount ?? 0); break;
        default: cmp = a.name.localeCompare(b.name); break;
      }
      return sortAsc ? cmp : -cmp;
    });
  };

  const sortedQueues = useMemo(() => queues ? sortItems(queues) : [], [queues, filter, sortCol, sortAsc]);
  const sortedTopics = useMemo(() => topics ? sortItems(topics) : [], [topics, filter, sortCol, sortAsc]);

  const SortArrow = ({ col }: { col: SortCol }) => {
    if (sortCol !== col) return null;
    return sortAsc ? <ArrowUp className="inline h-3 w-3" /> : <ArrowDown className="inline h-3 w-3" />;
  };

  if (!nsId) {
    return (
      <div className="p-4 text-sm text-muted-foreground" data-testid="entity-tree-empty">
        Select a namespace to view entities
      </div>
    );
  }

  // Either query can resolve before the other; showing the loading state
  // requires waiting for both, otherwise the tree flashes an incomplete list
  // (or a false "No entities found") while the slower query is still in flight.
  if (queuesLoading || topicsLoading) {
    return <div className="p-4 text-sm text-muted-foreground" data-testid="entity-tree-loading">Loading...</div>;
  }

  const toggleTopic = (name: string) => {
    setExpandedTopics((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  };

  const EntityIcon = ({ entity }: { entity: SbEntityInfo }) => {
    if (entity.stats?.deadLetterMessageCount && entity.stats.deadLetterMessageCount > 0) {
      return <MailX className="h-4 w-4 text-destructive" />;
    }
    return <Mail className="h-4 w-4 text-muted-foreground" />;
  };



  return (
    <div className="flex h-full flex-col text-sm">
      {/* Filter input */}
      <div className="border-b px-3 py-2">
        <div className="relative">
          <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Filter entities..."
            className="w-full rounded-md border bg-card py-1.5 pl-8 text-xs"
            data-testid="entity-filter"
          />
        </div>
      </div>

      {/* Column headers */}
      <div className="flex items-center gap-1 border-b px-3 py-1 text-xs text-muted-foreground">
        <button onClick={() => toggleSort("name")} className="flex-1 text-left font-medium hover:text-foreground">
          Name <SortArrow col="name" />
        </button>
        <button onClick={() => toggleSort("active")} className="w-10 text-center font-medium hover:text-foreground" title="Active count">
          A <SortArrow col="active" />
        </button>
        <button onClick={() => toggleSort("dlq")} className="w-10 text-center font-medium hover:text-foreground" title="Dead-letter count">
          DLQ <SortArrow col="dlq" />
        </button>
        <button onClick={() => toggleSort("sched")} className="w-10 text-center font-medium hover:text-foreground" title="Scheduled count">
          Sch <SortArrow col="sched" />
        </button>
      </div>

      <div className="flex-1 overflow-auto py-1">
        {/* Queues section */}
        {sortedQueues.length > 0 && (
          <div className="mb-2">
            <div className="px-3 py-1 text-xs font-semibold uppercase text-muted-foreground">
              Queues ({sortedQueues.length})
            </div>
            {sortedQueues.map((queue) => (
              <button
                key={queue.entityPath}
                data-testid={`entity-tree-queue-${queue.name}`}
                onClick={() => onSelectEntity(queue)}
                className={`flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-accent ${
                  selectedEntity?.entityPath === queue.entityPath ? "bg-accent" : ""
                }`}
              >
                <EntityIcon entity={queue} />
                <span className="truncate flex-1">{queue.name}</span>
                <EntityStatsBadges entity={queue} onSelectEntity={onSelectEntity} />
              </button>
            ))}
          </div>
        )}

        {/* Topics section */}
        {sortedTopics.length > 0 && (
          <div>
            <div className="px-3 py-1 text-xs font-semibold uppercase text-muted-foreground">
              Topics ({sortedTopics.length})
            </div>
            {sortedTopics.map((topic) => (
              <div key={topic.entityPath}>
                <button
                  data-testid={`entity-tree-topic-${topic.name}`}
                  onClick={() => toggleTopic(topic.name)}
                  className={`flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-accent ${
                    selectedEntity?.entityPath === topic.entityPath ? "bg-accent" : ""
                  }`}
                >
                  {expandedTopics.has(topic.name) ? (
                    <ChevronDown className="h-3 w-3" />
                  ) : (
                    <ChevronRight className="h-3 w-3" />
                  )}
                  <Folder className="h-4 w-4 text-muted-foreground" />
                  <span className="truncate">{topic.name}</span>
                  <EntityStatsBadges entity={topic} onSelectEntity={onSelectEntity} />
                </button>

                {expandedTopics.has(topic.name) && (
                  <TopicSubscriptions
                    nsId={nsId}
                    topicName={topic.name}
                    selectedEntity={selectedEntity}
                    onSelectEntity={onSelectEntity}
                  />
                )}
              </div>
            ))}
          </div>
        )}

        {sortedQueues.length === 0 && sortedTopics.length === 0 && (
          <div className="p-4 text-sm text-muted-foreground">
            No entities found
          </div>
        )}
      </div>
    </div>
  );
}

function TopicSubscriptions({
  nsId,
  topicName,
  selectedEntity,
  onSelectEntity,
}: {
  nsId: string;
  topicName: string;
  selectedEntity: SbEntityInfo | null;
  onSelectEntity: (entity: SbEntityInfo, viewMode?: "active" | "dlq") => void;
}) {
  const { data: subs, isLoading } = useSbSubscriptions(nsId, topicName);

  if (isLoading) return <div className="px-6 py-1 text-xs text-muted-foreground">Loading...</div>;
  if (!subs || subs.length === 0) return null;

  return (
    <div className="ml-4">
      {subs.map((sub) => (
        <button
          key={sub.entityPath}
          data-testid={`entity-tree-sub-${sub.name}`}
          onClick={() => onSelectEntity(sub)}
          className={`flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-accent ${
            selectedEntity?.entityPath === sub.entityPath ? "bg-accent" : ""
          }`}
        >
          <Mail className="h-3.5 w-3.5 text-muted-foreground" />
          <span className="truncate flex-1">{sub.name}</span>
          <EntityStatsBadges entity={sub} onSelectEntity={onSelectEntity} />
        </button>
      ))}
    </div>
  );
}
