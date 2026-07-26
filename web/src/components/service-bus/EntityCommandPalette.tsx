import { useState, useEffect, useRef, useMemo } from "react";
import { Search, Mail, MailX, Folder, Send, Trash2, Eye, RefreshCw } from "lucide-react";
import { useSbQueues, useSbTopics } from "@/lib/hooks";
import type { SbEntityInfo } from "@/lib/types";

interface Props {
  open: boolean;
  nsId: string | null;
  onClose: () => void;
  onSelectEntity: (entity: SbEntityInfo) => void;
  onAction: (entity: SbEntityInfo, action: EntityAction) => void;
}

export type EntityAction = "peek-active" | "peek-dlq" | "send" | "purge" | "refresh";

const actionLabels: Record<EntityAction, { label: string; icon: typeof Eye }> = {
  "peek-active": { label: "Peek Active", icon: Eye },
  "peek-dlq": { label: "Peek DLQ", icon: MailX },
  send: { label: "Send Message", icon: Send },
  purge: { label: "Purge", icon: Trash2 },
  refresh: { label: "Refresh", icon: RefreshCw },
};

export function EntityCommandPalette({ open, nsId, onClose, onSelectEntity, onAction }: Props) {
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [showActionsFor, setShowActionsFor] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const { data: queues } = useSbQueues(open ? nsId : null);
  const { data: topics } = useSbTopics(open ? nsId : null);

  const allEntities = useMemo(() => {
    const entities: { entity: SbEntityInfo; type: string }[] = [];
    if (queues) queues.forEach((q) => entities.push({ entity: q, type: "queue" }));
    if (topics) topics.forEach((t) => entities.push({ entity: t, type: "topic" }));
    return entities;
  }, [queues, topics]);

  const filtered = useMemo(() => {
    if (!query.trim()) return allEntities;
    const q = query.toLowerCase();
    return allEntities.filter(({ entity }) =>
      entity.name.toLowerCase().includes(q) ||
      entity.entityPath.toLowerCase().includes(q),
    );
  }, [allEntities, query]);

  const flatItems = useMemo(() => {
    const items: { entity: SbEntityInfo; type: string; action?: EntityAction }[] = [];
    for (const { entity, type } of filtered) {
      items.push({ entity, type });
      if (showActionsFor === entity.entityPath) {
        (Object.keys(actionLabels) as EntityAction[]).forEach((action) => {
          items.push({ entity, type, action });
        });
      }
    }
    return items;
  }, [filtered, showActionsFor]);

  useEffect(() => {
    if (open) {
      setQuery("");
      setSelectedIndex(0);
      setShowActionsFor(null);
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [query]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Escape") {
      e.preventDefault();
      onClose();
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((prev) => Math.min(prev + 1, flatItems.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((prev) => Math.max(prev - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      const item = flatItems[selectedIndex];
      if (item) {
        if (item.action) {
          onAction(item.entity, item.action);
          onClose();
        } else {
          onSelectEntity(item.entity);
          setShowActionsFor(item.entity.entityPath);
        }
      }
    } else if (e.key === "Tab") {
      e.preventDefault();
      const item = flatItems[selectedIndex];
      if (item && !item.action) {
        setShowActionsFor(showActionsFor === item.entity.entityPath ? null : item.entity.entityPath);
      }
    }
  };

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/50 pt-20"
      onClick={onClose}
      data-testid="entity-command-palette"
    >
      <div
        className="w-96 rounded-lg border bg-card shadow-lg"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-2 border-b px-3 py-2">
          <Search className="h-4 w-4 text-muted-foreground" />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Search entities... (Tab for actions)"
            className="flex-1 bg-transparent text-sm outline-none"
            data-testid="entity-palette-search"
          />
        </div>

        <div className="max-h-80 overflow-auto py-1" data-testid="entity-palette-results">
          {flatItems.length === 0 && (
            <div className="px-3 py-4 text-center text-sm text-muted-foreground">
              {nsId ? "No entities found" : "Select a namespace first"}
            </div>
          )}
          {flatItems.map((item, index) => {
            const isAction = !!item.action;
            const ActionIcon = isAction ? actionLabels[item.action!].icon : null;
            const EntityIcon = item.entity.stats?.deadLetterMessageCount && item.entity.stats.deadLetterMessageCount > 0
              ? MailX
              : item.type === "topic"
                ? Folder
                : Mail;

            return (
              <button
                key={`${item.entity.entityPath}-${item.action ?? "entity"}`}
                onClick={() => {
                  if (isAction) {
                    onAction(item.entity, item.action!);
                    onClose();
                  } else {
                    onSelectEntity(item.entity);
                    setShowActionsFor(item.entity.entityPath);
                  }
                }}
                onMouseEnter={() => setSelectedIndex(index)}
                className={`flex w-full items-center gap-2 px-3 py-2 text-left text-sm ${
                  index === selectedIndex ? "bg-accent" : ""
                } ${isAction ? "pl-8" : ""}`}
                data-testid={`entity-palette-item-${index}`}
              >
                {isAction && ActionIcon ? (
                  <ActionIcon className="h-3.5 w-3.5 text-muted-foreground" />
                ) : (
                  <EntityIcon className="h-4 w-4 text-muted-foreground" />
                )}
                <span className={isAction ? "text-muted-foreground" : ""}>
                  {isAction ? actionLabels[item.action!].label : item.entity.name}
                </span>
                {!isAction && (
                  <span className="ml-auto rounded bg-secondary px-1.5 py-0.5 text-xs text-secondary-foreground">
                    {item.type}
                  </span>
                )}
                {!isAction && item.entity.stats?.activeMessageCount != null && item.entity.stats.activeMessageCount > 0 && (
                  <span className="rounded bg-secondary px-1.5 py-0.5 text-xs">
                    {item.entity.stats.activeMessageCount}
                  </span>
                )}
                {!isAction && item.entity.stats?.deadLetterMessageCount != null && item.entity.stats.deadLetterMessageCount > 0 && (
                  <span className="rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
                    {item.entity.stats.deadLetterMessageCount}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        <div className="border-t px-3 py-1.5 text-xs text-muted-foreground">
          Enter to select · Tab for actions · Esc to close
        </div>
      </div>
    </div>
  );
}
