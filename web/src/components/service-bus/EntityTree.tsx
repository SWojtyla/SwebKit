import { useMemo, useRef, useState } from "react";
import { ChevronRight, ChevronDown, Mail, MailX, Folder, Search, ArrowUp, ArrowDown } from "lucide-react";
import { useSbQueues, useSbTopics, useSbSubscriptions } from "@/lib/hooks";
import { QueryState } from "@/components/shared/QueryState";
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
  dlqRollup,
}: {
  entity: SbEntityInfo;
  onSelectEntity: (entity: SbEntityInfo, viewMode?: "active" | "dlq") => void;
  /**
   * Rolled-up dead-letter count across a topic's subscriptions. Topics have no active/scheduled
   * count of their own, but a DLQ backlog on a subscription underneath is worth surfacing even
   * while the topic is collapsed — otherwise it's invisible until every subscription is expanded
   * by hand. `undefined` while the rollup is still loading, `null` if it failed to load.
   */
  dlqRollup?: number | null;
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
        <span
          className={`rounded px-1.5 py-0.5 ${dlqRollup ? "bg-destructive/20 text-destructive" : ""}`}
          title="Dead-letter messages across all subscriptions"
          data-testid={`entity-tree-topic-dlq-rollup-${entity.name}`}
        >
          {dlqRollup === undefined ? "·" : dlqRollup === null ? "?" : dlqRollup > 0 ? dlqRollup : "–"}
        </span>
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

// Roving keyboard navigation across `role="treeitem"` rows within a tree
// container. Since EntityTree isn't virtualized, every rendered row already
// exists in the DOM, so a simple DOM query is enough (no scroll-into-view
// dance like the virtualized CollectionTree needs).
function focusAdjacentTreeItem(container: HTMLElement | null, current: HTMLElement, delta: 1 | -1) {
  if (!container) return;
  const items = Array.from(container.querySelectorAll<HTMLElement>('[role="treeitem"]'));
  const index = items.indexOf(current);
  if (index === -1) return;
  const next = items[index + delta];
  next?.focus();
}

const EntityIcon = ({ entity }: { entity: SbEntityInfo }) => {
  if (entity.stats?.deadLetterMessageCount && entity.stats.deadLetterMessageCount > 0) {
    return <MailX className="h-4 w-4 text-destructive" />;
  }
  return <Mail className="h-4 w-4 text-muted-foreground" />;
};

export function EntityTree({ nsId, selectedEntity, onSelectEntity }: Props) {
  const { data: queues, isLoading: queuesLoading, isError: queuesIsError, error: queuesError } = useSbQueues(nsId);
  const { data: topics, isLoading: topicsLoading, isError: topicsIsError, error: topicsError } = useSbTopics(nsId);
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set());
  const [queuesCollapsed, setQueuesCollapsed] = useState(false);
  const [filter, setFilter] = useState("");
  const [sortCol, setSortCol] = useState<SortCol>("name");
  const [sortAsc, setSortAsc] = useState(true);
  const treeRef = useRef<HTMLDivElement | null>(null);

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

  const toggleTopic = (name: string) => {
    setExpandedTopics((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  };

  const expandAllTopics = () => setExpandedTopics(new Set(sortedTopics.map((t) => t.name)));
  const collapseAllTopics = () => setExpandedTopics(new Set());

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

      <div className="flex-1 overflow-auto py-1" role="tree" aria-label="Queues and topics" ref={treeRef}>
        {/* Queues section — a real section header + collapse toggle, matching the reported
            "isn't collapsed by default when it should be" complaint for large namespaces. Its
            loading/error/empty state is independent of Topics below: a slow or broken Topics
            query must not hold up an already-loaded Queues list (or vice versa). */}
        <div className="mb-2" role="group" aria-label="Queues">
          <div className="flex items-center justify-between px-3 py-1">
            <button
              type="button"
              onClick={() => setQueuesCollapsed((v) => !v)}
              className="flex items-center gap-1 text-xs font-semibold uppercase text-muted-foreground hover:text-foreground"
              data-testid="entity-tree-queues-toggle"
              aria-expanded={!queuesCollapsed}
            >
              {queuesCollapsed ? <ChevronRight className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
              Queues{!queuesLoading && !queuesIsError ? ` (${sortedQueues.length})` : ""}
            </button>
          </div>
          {!queuesCollapsed && (
            <QueryState
              data={sortedQueues}
              isLoading={queuesLoading}
              error={queuesError}
              emptyTitle="No queues found"
              skeletonRows={3}
            >
              {(qs) => qs.map((queue) => (
                <div
                  key={queue.entityPath}
                  role="treeitem"
                  aria-level={1}
                  aria-selected={selectedEntity?.entityPath === queue.entityPath}
                  tabIndex={0}
                  data-testid={`entity-tree-queue-${queue.name}`}
                  onClick={() => onSelectEntity(queue)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      onSelectEntity(queue);
                    } else if (e.key === "ArrowDown") {
                      e.preventDefault();
                      focusAdjacentTreeItem(treeRef.current, e.currentTarget, 1);
                    } else if (e.key === "ArrowUp") {
                      e.preventDefault();
                      focusAdjacentTreeItem(treeRef.current, e.currentTarget, -1);
                    }
                  }}
                  className={`flex w-full cursor-pointer items-center gap-2 px-3 py-1.5 text-left hover:bg-accent ${
                    selectedEntity?.entityPath === queue.entityPath ? "bg-accent" : ""
                  }`}
                >
                  <EntityIcon entity={queue} />
                  <span className="truncate flex-1">{queue.name}</span>
                  <EntityStatsBadges entity={queue} onSelectEntity={onSelectEntity} />
                </div>
              ))}
            </QueryState>
          )}
        </div>

        {/* Topics section */}
        <div role="group" aria-label="Topics">
          <div className="flex items-center justify-between px-3 py-1">
            <span className="text-xs font-semibold uppercase text-muted-foreground">
              Topics{!topicsLoading && !topicsIsError ? ` (${sortedTopics.length})` : ""}
            </span>
            {sortedTopics.length > 0 && (
              <div className="flex items-center gap-2 text-[11px] normal-case text-muted-foreground">
                <button
                  type="button"
                  onClick={expandAllTopics}
                  className="hover:text-foreground"
                  data-testid="entity-tree-topics-expand-all"
                >
                  Expand all
                </button>
                <button
                  type="button"
                  onClick={collapseAllTopics}
                  className="hover:text-foreground"
                  data-testid="entity-tree-topics-collapse-all"
                >
                  Collapse all
                </button>
              </div>
            )}
          </div>
          <QueryState
            data={sortedTopics}
            isLoading={topicsLoading}
            error={topicsError}
            emptyTitle="No topics found"
            skeletonRows={3}
          >
            {(ts) => ts.map((topic) => (
              <TopicRow
                key={topic.entityPath}
                nsId={nsId}
                topic={topic}
                isExpanded={expandedTopics.has(topic.name)}
                onToggle={toggleTopic}
                selectedEntity={selectedEntity}
                onSelectEntity={onSelectEntity}
                treeRef={treeRef}
              />
            ))}
          </QueryState>
        </div>
      </div>
    </div>
  );
}

function TopicRow({
  nsId,
  topic,
  isExpanded,
  onToggle,
  selectedEntity,
  onSelectEntity,
  treeRef,
}: {
  nsId: string;
  topic: SbEntityInfo;
  isExpanded: boolean;
  onToggle: (name: string) => void;
  selectedEntity: SbEntityInfo | null;
  onSelectEntity: (entity: SbEntityInfo, viewMode?: "active" | "dlq") => void;
  treeRef: React.RefObject<HTMLDivElement | null>;
}) {
  // Fetched unconditionally — not just while expanded — so the dead-letter rollup badge below
  // stays accurate even for a collapsed topic. Same query TanStack Query would otherwise fetch
  // again on expand, so this doesn't add a second request once the user does expand it.
  const { data: subs, isLoading: subsLoading, isError: subsIsError } = useSbSubscriptions(nsId, topic.name);

  const dlqRollup = useMemo(() => {
    if (subsIsError) return null;
    if (subsLoading || !subs) return undefined;
    return subs.reduce((sum, s) => sum + (s.stats?.deadLetterMessageCount ?? 0), 0);
  }, [subs, subsLoading, subsIsError]);

  return (
    <div>
      <div
        role="treeitem"
        aria-level={1}
        aria-expanded={isExpanded}
        aria-selected={selectedEntity?.entityPath === topic.entityPath}
        tabIndex={0}
        data-testid={`entity-tree-topic-${topic.name}`}
        onClick={() => onToggle(topic.name)}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            onToggle(topic.name);
          } else if (e.key === "ArrowRight") {
            e.preventDefault();
            if (!isExpanded) onToggle(topic.name);
            else focusAdjacentTreeItem(treeRef.current, e.currentTarget, 1);
          } else if (e.key === "ArrowLeft") {
            if (isExpanded) {
              e.preventDefault();
              onToggle(topic.name);
            }
          } else if (e.key === "ArrowDown") {
            e.preventDefault();
            focusAdjacentTreeItem(treeRef.current, e.currentTarget, 1);
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            focusAdjacentTreeItem(treeRef.current, e.currentTarget, -1);
          }
        }}
        // Deliberately not the same hover treatment as queue/subscription rows: those select an
        // entity on click, a topic row only ever toggles expansion, so it shouldn't read as the
        // same kind of clickable target. The chevron button is the one dedicated toggle control.
        className={`flex w-full cursor-pointer items-center gap-2 px-3 py-1.5 text-left hover:bg-muted/40 ${
          selectedEntity?.entityPath === topic.entityPath ? "bg-accent" : ""
        }`}
      >
        <button
          type="button"
          tabIndex={-1}
          onClick={(e) => { e.stopPropagation(); onToggle(topic.name); }}
          className="rounded p-0.5 text-muted-foreground hover:bg-accent hover:text-foreground"
          data-testid={`entity-tree-topic-toggle-${topic.name}`}
          aria-label={isExpanded ? `Collapse ${topic.name}` : `Expand ${topic.name}`}
        >
          {isExpanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
        </button>
        <Folder className="h-4 w-4 text-muted-foreground" />
        <span className="truncate">{topic.name}</span>
        <EntityStatsBadges entity={topic} onSelectEntity={onSelectEntity} dlqRollup={dlqRollup} />
      </div>

      {isExpanded && (
        <SubscriptionRows
          topicName={topic.name}
          subs={subs}
          isLoading={subsLoading}
          isError={subsIsError}
          selectedEntity={selectedEntity}
          onSelectEntity={onSelectEntity}
          treeRef={treeRef}
        />
      )}
    </div>
  );
}

function SubscriptionRows({
  topicName,
  subs,
  isLoading,
  isError,
  selectedEntity,
  onSelectEntity,
  treeRef,
}: {
  topicName: string;
  subs: SbEntityInfo[] | undefined;
  isLoading: boolean;
  isError: boolean;
  selectedEntity: SbEntityInfo | null;
  onSelectEntity: (entity: SbEntityInfo, viewMode?: "active" | "dlq") => void;
  treeRef: React.RefObject<HTMLDivElement | null>;
}) {
  // A fetch failure here must not look like "this topic has no subscriptions" — same class of
  // bug as the top-level Queues/Topics sections, just one level deeper in the tree.
  if (isError) {
    return (
      <div className="px-6 py-1 text-xs text-destructive" data-testid={`entity-tree-sub-error-${topicName}`}>
        Couldn't load subscriptions
      </div>
    );
  }
  if (isLoading) {
    return <div className="px-6 py-1 text-xs text-muted-foreground">Loading...</div>;
  }
  if (!subs || subs.length === 0) {
    return (
      <div className="px-6 py-1 text-xs text-muted-foreground" data-testid={`entity-tree-sub-empty-${topicName}`}>
        No subscriptions
      </div>
    );
  }

  return (
    <div className="ml-4" role="group" aria-label={`Subscriptions of ${topicName}`}>
      {subs.map((sub) => (
        <div
          key={sub.entityPath}
          role="treeitem"
          aria-level={2}
          aria-selected={selectedEntity?.entityPath === sub.entityPath}
          tabIndex={0}
          data-testid={`entity-tree-sub-${sub.name}`}
          onClick={() => onSelectEntity(sub)}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault();
              onSelectEntity(sub);
            } else if (e.key === "ArrowDown") {
              e.preventDefault();
              focusAdjacentTreeItem(treeRef.current, e.currentTarget, 1);
            } else if (e.key === "ArrowUp" || e.key === "ArrowLeft") {
              e.preventDefault();
              focusAdjacentTreeItem(treeRef.current, e.currentTarget, -1);
            }
          }}
          className={`flex w-full cursor-pointer items-center gap-2 px-3 py-1.5 text-left hover:bg-accent ${
            selectedEntity?.entityPath === sub.entityPath ? "bg-accent" : ""
          }`}
        >
          <Mail className="h-3.5 w-3.5 text-muted-foreground" />
          <span className="truncate flex-1">{sub.name}</span>
          <EntityStatsBadges entity={sub} onSelectEntity={onSelectEntity} />
        </div>
      ))}
    </div>
  );
}
