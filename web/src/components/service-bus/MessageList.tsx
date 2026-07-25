import { useState, useMemo } from "react";
import { Search, Filter, X } from "lucide-react";
import { useSbPeekMessages, useSbPeekDlq } from "@/lib/hooks";
import type { SbEntityInfo, SbMessage } from "@/lib/types";
import { applyFilters } from "./filterLogic";
import { AdvancedFilterPanel } from "./AdvancedFilterPanel";
import type { AdvancedFilterRule } from "./filterTypes";

interface Props {
  nsId: string | null;
  entity: SbEntityInfo | null;
  viewMode: "active" | "dlq";
  selectedMessage: SbMessage | null;
  onSelectMessage: (message: SbMessage) => void;
}

export function MessageList({ nsId, entity, viewMode, selectedMessage, onSelectMessage }: Props) {
  const [textFilter, setTextFilter] = useState("");
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [advancedRules, setAdvancedRules] = useState<AdvancedFilterRule[]>([]);
  const [advancedEnabled, setAdvancedEnabled] = useState(true);

  const activeQuery = useSbPeekMessages(
    viewMode === "active" ? nsId : null,
    entity?.entityPath ?? null,
  );
  const dlqQuery = useSbPeekDlq(
    viewMode === "dlq" ? nsId : null,
    entity?.entityPath ?? null,
  );

  const messages = viewMode === "active" ? activeQuery.data : dlqQuery.data;
  const isLoading = viewMode === "active" ? activeQuery.isLoading : dlqQuery.isLoading;

  const filteredMessages = useMemo(
    () =>
      applyFilters(messages ?? [], textFilter, advancedRules, advancedEnabled && showAdvanced, null),
    [messages, textFilter, advancedRules, advancedEnabled, showAdvanced],
  );

  const activeRuleCount = advancedRules.filter((r) => r.enabled && r.value.trim()).length;

  if (!entity) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="message-list-empty">
        Select an entity
      </div>
    );
  }

  if (isLoading) {
    return <div className="p-4 text-sm text-muted-foreground" data-testid="message-list-loading">Loading messages...</div>;
  }

  return (
    <div className="flex h-full flex-col" data-testid="message-list-container">
      {/* Filter bar */}
      <div className="flex items-center gap-1.5 border-b px-2 py-1.5">
        <div className="relative flex-1">
          <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <input
            type="text"
            data-testid="message-text-filter"
            value={textFilter}
            onChange={(e) => setTextFilter(e.target.value)}
            placeholder="Search messages..."
            className="w-full rounded-md border bg-card py-1.5 pl-8 pr-7 text-xs"
          />
          {textFilter && (
            <button
              onClick={() => setTextFilter("")}
              className="absolute right-1.5 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
              title="Clear search"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
        <button
          data-testid="toggle-advanced-filter"
          onClick={() => setShowAdvanced(!showAdvanced)}
          title="Advanced filters"
          className={`flex items-center gap-1 rounded-md border px-2 py-1.5 text-xs ${
            showAdvanced || activeRuleCount > 0
              ? "border-primary bg-primary/10 text-primary"
              : "text-muted-foreground hover:bg-accent"
          }`}
        >
          <Filter className="h-3.5 w-3.5" />
          {activeRuleCount > 0 && (
            <span className="rounded-full bg-primary px-1.5 text-[10px] text-primary-foreground">
              {activeRuleCount}
            </span>
          )}
        </button>
      </div>

      {/* Advanced filter panel */}
      {showAdvanced && (
        <>
          <div className="flex items-center justify-between border-b bg-muted/20 px-2 py-1">
            <label className="flex items-center gap-1.5 text-xs">
              <input
                type="checkbox"
                checked={advancedEnabled}
                onChange={(e) => setAdvancedEnabled(e.target.checked)}
              />
              <span className="font-medium">Advanced filters</span>
            </label>
            {advancedRules.length > 0 && (
              <button
                onClick={() => setAdvancedRules([])}
                className="text-xs text-muted-foreground hover:text-foreground"
              >
                Clear all
              </button>
            )}
          </div>
          <AdvancedFilterPanel rules={advancedRules} onChange={setAdvancedRules} />
        </>
      )}

      {/* Message list */}
      {filteredMessages.length === 0 ? (
        <div
          className="flex h-full items-center justify-center text-sm text-muted-foreground"
          data-testid={messages && messages.length === 0 ? "message-list-no-messages" : "message-list-no-matches"}
        >
          {messages && messages.length === 0
            ? `No ${viewMode === "dlq" ? "dead-lettered" : "active"} messages`
            : "No messages match the current filters"}
        </div>
      ) : (
        <div className="overflow-auto" data-testid="message-list">
          {filteredMessages.map((msg) => (
            <button
              key={`${msg.messageId}-${msg.sequenceNumber}`}
              data-testid={`message-item-${msg.sequenceNumber}`}
              onClick={() => onSelectMessage(msg)}
              className={`block w-full border-b px-3 py-2 text-left hover:bg-accent ${
                selectedMessage?.messageId === msg.messageId &&
                selectedMessage?.sequenceNumber === msg.sequenceNumber
                  ? "bg-accent"
                  : ""
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="truncate text-sm font-medium">
                  {msg.subject || msg.messageId}
                </span>
                {viewMode === "dlq" && msg.deliveryCount > 0 && (
                  <span className="shrink-0 rounded bg-destructive/20 px-1.5 py-0.5 text-xs text-destructive">
                    ×{msg.deliveryCount}
                  </span>
                )}
              </div>
              <div className="mt-0.5 flex items-center gap-2 text-xs text-muted-foreground">
                <span>#{msg.sequenceNumber}</span>
                <span>·</span>
                <span>{new Date(msg.enqueuedAt).toLocaleTimeString()}</span>
              </div>
              {msg.deadLetterReason && viewMode === "dlq" && (
                <div className="mt-1 truncate text-xs text-destructive">
                  {msg.deadLetterReason}
                </div>
              )}
            </button>
          ))}
        </div>
      )}

      {/* Filter result count */}
      {(textFilter.trim() || activeRuleCount > 0) && filteredMessages.length > 0 && (
        <div className="border-t px-3 py-1 text-xs text-muted-foreground" data-testid="message-filter-count">
          {filteredMessages.length} of {messages?.length ?? 0} messages
        </div>
      )}
    </div>
  );
}
