import { useState } from "react";
import { ChevronRight, ChevronDown, Mail, MailX, Folder } from "lucide-react";
import { useSbQueues, useSbTopics, useSbSubscriptions } from "@/lib/hooks";
import type { SbEntityInfo } from "@/lib/types";

interface Props {
  nsId: string | null;
  selectedEntity: SbEntityInfo | null;
  onSelectEntity: (entity: SbEntityInfo) => void;
}

export function EntityTree({ nsId, selectedEntity, onSelectEntity }: Props) {
  const { data: queues, isLoading: queuesLoading } = useSbQueues(nsId);
  const { data: topics, isLoading: topicsLoading } = useSbTopics(nsId);
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set());

  if (!nsId) {
    return (
      <div className="p-4 text-sm text-muted-foreground" data-testid="entity-tree-empty">
        Select a namespace to view entities
      </div>
    );
  }

  if (queuesLoading && topicsLoading) {
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

  const EntityBadges = ({ entity }: { entity: SbEntityInfo }) => {
    if (!entity.stats) return null;
    return (
      <span className="ml-auto flex gap-1 text-xs">
        {entity.stats.activeMessageCount > 0 && (
          <span className="rounded bg-secondary px-1.5 py-0.5 text-secondary-foreground">
            {entity.stats.activeMessageCount}
          </span>
        )}
        {entity.stats.deadLetterMessageCount > 0 && (
          <span className="rounded bg-destructive/20 px-1.5 py-0.5 text-destructive">
            {entity.stats.deadLetterMessageCount}
          </span>
        )}
      </span>
    );
  };

  return (
    <div className="py-2 text-sm">
      {/* Queues section */}
      {queues && queues.length > 0 && (
        <div className="mb-2">
          <div className="px-3 py-1 text-xs font-semibold uppercase text-muted-foreground">
            Queues
          </div>
          {queues.map((queue) => (
            <button
              key={queue.entityPath}
              data-testid={`entity-tree-queue-${queue.name}`}
              onClick={() => onSelectEntity(queue)}
              className={`flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-accent ${
                selectedEntity?.entityPath === queue.entityPath
                  ? "bg-accent"
                  : ""
              }`}
            >
              <EntityIcon entity={queue} />
              <span className="truncate">{queue.name}</span>
              <EntityBadges entity={queue} />
            </button>
          ))}
        </div>
      )}

      {/* Topics section */}
      {topics && topics.length > 0 && (
        <div>
          <div className="px-3 py-1 text-xs font-semibold uppercase text-muted-foreground">
            Topics
          </div>
          {topics.map((topic) => (
            <div key={topic.entityPath}>
              <button
                data-testid={`entity-tree-topic-${topic.name}`}
                onClick={() => toggleTopic(topic.name)}
                className={`flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-accent ${
                  selectedEntity?.entityPath === topic.entityPath
                    ? "bg-accent"
                    : ""
                }`}
              >
                {expandedTopics.has(topic.name) ? (
                  <ChevronDown className="h-3 w-3" />
                ) : (
                  <ChevronRight className="h-3 w-3" />
                )}
                <Folder className="h-4 w-4 text-muted-foreground" />
                <span className="truncate">{topic.name}</span>
                <EntityBadges entity={topic} />
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

      {(!queues || queues.length === 0) && (!topics || topics.length === 0) && (
        <div className="p-4 text-sm text-muted-foreground">
          No entities found
        </div>
      )}
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
  onSelectEntity: (entity: SbEntityInfo) => void;
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
          <span className="truncate">{sub.name}</span>
          {sub.stats?.deadLetterMessageCount && sub.stats.deadLetterMessageCount > 0 ? (
            <span className="ml-auto rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
              {sub.stats.deadLetterMessageCount}
            </span>
          ) : null}
          {sub.stats?.activeMessageCount && sub.stats.activeMessageCount > 0 ? (
            <span className="ml-auto rounded bg-secondary px-1.5 py-0.5 text-xs text-secondary-foreground">
              {sub.stats.activeMessageCount}
            </span>
          ) : null}
        </button>
      ))}
    </div>
  );
}
