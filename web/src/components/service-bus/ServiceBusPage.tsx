import { useState, useEffect, useCallback } from "react";
import { Plus, Upload, Clock, Search, RotateCcw, ChevronLeft } from "lucide-react";
import { useProfile } from "@/lib/hooks";
import { useQueryClient } from "@tanstack/react-query";
import { EntityTree } from "./EntityTree";
import { MessageList } from "./MessageList";
import { MessageDetail } from "./MessageDetail";
import { MessageComposer, type ComposerMode } from "./MessageComposer";
import { BatchSendPanel } from "./BatchSendPanel";
import { ScheduledMessages } from "./ScheduledMessages";
import { EntityCommandPalette, type EntityAction } from "./EntityCommandPalette";
import { BatchReplayPanel } from "./BatchReplayPanel";
import type { SbEntityInfo, SbMessage } from "@/lib/types";

export function ServiceBusPage() {
  const { data: profile } = useProfile();
  const [selectedNsId, setSelectedNsId] = useState<string | null>(null);
  const [selectedEntity, setSelectedEntity] = useState<SbEntityInfo | null>(null);
  const [selectedMessage, setSelectedMessage] = useState<SbMessage | null>(null);
  const [viewMode, setViewMode] = useState<"active" | "dlq">("active");
  const [composerMode, setComposerMode] = useState<ComposerMode | null>(null);
  const [showBatchSend, setShowBatchSend] = useState(false);
  const [showScheduled, setShowScheduled] = useState(false);
  const [showEntityPalette, setShowEntityPalette] = useState(false);
  const [showBatchReplay, setShowBatchReplay] = useState(false);
  const [showEntityTree, setShowEntityTree] = useState(true);
  const queryClient = useQueryClient();

  const handleEntityAction = useCallback((entity: SbEntityInfo, action: EntityAction) => {
    setSelectedEntity(entity);
    setSelectedMessage(null);
    if (action === "peek-active") setViewMode("active");
    if (action === "peek-dlq") setViewMode("dlq");
    if (action === "send") setComposerMode("compose");
    if (action === "refresh") queryClient.invalidateQueries({ queryKey: ["sb-"] });
  }, [queryClient]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === "e" || e.key === "E")) {
        e.preventDefault();
        setShowEntityPalette((prev) => !prev);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

  const namespaces = profile?.serviceBusNamespaces ?? [];
  const selectedNs = namespaces.find((ns) => ns.id === selectedNsId);

  return (
    <div className="flex h-full flex-col" data-testid="service-bus-page">
      {/* Namespace selector + compose button */}
      <div className="flex items-center gap-3 border-b px-4 py-2">
        <span className="text-sm font-medium">Namespace:</span>
        <select
          data-testid="sb-namespace-select"
          value={selectedNsId ?? ""}
          onChange={(e) => {
            setSelectedNsId(e.target.value || null);
            setSelectedEntity(null);
            setSelectedMessage(null);
          }}
          className="rounded-md border bg-card px-3 py-1.5 text-sm"
        >
          <option value="">Select namespace...</option>
          {namespaces.map((ns) => (
            <option key={ns.id} value={ns.id}>
              {ns.alias || ns.fullyQualifiedNamespace}
            </option>
          ))}
        </select>
        {namespaces.length === 0 && (
          <span className="text-xs text-muted-foreground">
            Configure namespaces in Settings
          </span>
        )}
        <button
          data-testid="toggle-entity-tree"
          onClick={() => setShowEntityTree((v) => !v)}
          className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
        >
          {showEntityTree ? "Hide Entities" : "Show Entities"}
        </button>
        <div className="flex-1" />
        <button
          data-testid="sb-entity-search"
          onClick={() => setShowEntityPalette(true)}
          disabled={!selectedNsId}
          className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
        >
          <Search className="h-3.5 w-3.5" />
          Search Entities
        </button>
        <button
          data-testid="sb-compose-button"
          onClick={() => setComposerMode("compose")}
          disabled={!selectedNsId}
          className="flex items-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
        >
          <Plus className="h-3.5 w-3.5" />
          Compose
        </button>
        <button
          data-testid="sb-batch-send-button"
          onClick={() => setShowBatchSend(true)}
          disabled={!selectedNsId}
          className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
        >
          <Upload className="h-3.5 w-3.5" />
          Batch Send
        </button>
        <button
          data-testid="sb-scheduled-button"
          onClick={() => setShowScheduled(true)}
          disabled={!selectedNsId || !selectedEntity}
          className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
        >
          <Clock className="h-3.5 w-3.5" />
          Scheduled
        </button>
        <button
          data-testid="sb-batch-replay-button"
          onClick={() => setShowBatchReplay(true)}
          disabled={!selectedNsId || !selectedEntity}
          className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs hover:bg-accent disabled:opacity-50"
        >
          <RotateCcw className="h-3.5 w-3.5" />
          Batch Replay
        </button>
      </div>

      {/* Main content: entity tree | message list | detail */}
      <div className="flex flex-1 overflow-hidden">
        {/* Entity tree */}
        {showEntityTree ? (
          <div className="w-64 overflow-auto border-r">
            <EntityTree
              nsId={selectedNsId}
              selectedEntity={selectedEntity}
              onSelectEntity={(entity, mode) => {
                setSelectedEntity(entity);
                setSelectedMessage(null);
                if (mode) setViewMode(mode);
              }}
            />
          </div>
        ) : (
          <button
            data-testid="show-entity-tree"
            onClick={() => setShowEntityTree(true)}
            className="flex items-center border-r bg-card px-1.5 py-2 text-xs text-muted-foreground hover:bg-accent hover:text-foreground"
            title="Show entity tree"
          >
            Entities
          </button>
        )}

        {/* Message list */}
        <div className="flex w-80 flex-col overflow-hidden border-r">
          {selectedEntity && (
            <div className="flex items-center gap-2 border-b px-3 py-1.5 text-xs" data-testid="sb-breadcrumb">
              <button
                type="button"
                onClick={() => { setSelectedEntity(null); setSelectedMessage(null); }}
                className="flex items-center gap-1 text-muted-foreground hover:text-foreground"
                title="Return to entity overview"
              >
                <ChevronLeft className="h-3 w-3" /> Overview
              </button>
              <span className="text-muted-foreground">/</span>
              <span className="truncate text-muted-foreground" title={selectedNs?.alias ?? selectedNsId ?? ""}>
                {selectedNs?.alias ?? selectedNsId}
              </span>
              <span className="text-muted-foreground">/</span>
              <span className="truncate font-medium" title={selectedEntity.name}>
                {selectedEntity.name}
              </span>
            </div>
          )}
          {selectedEntity && (
            <div className="flex border-b">
              <button
                data-testid="sb-view-active"
                onClick={() => setViewMode("active")}
                className={`flex-1 px-3 py-2 text-sm font-medium ${
                  viewMode === "active"
                    ? "border-b-2 border-primary text-foreground"
                    : "text-muted-foreground hover:text-foreground"
                }`}
              >
                Active {selectedEntity.stats && `(${selectedEntity.stats.activeMessageCount})`}
              </button>
              <button
                data-testid="sb-view-dlq"
                onClick={() => setViewMode("dlq")}
                className={`flex-1 px-3 py-2 text-sm font-medium ${
                  viewMode === "dlq"
                    ? "border-b-2 border-primary text-foreground"
                    : "text-muted-foreground hover:text-foreground"
                }`}
              >
                DLQ {selectedEntity.stats && `(${selectedEntity.stats.deadLetterMessageCount})`}
              </button>
            </div>
          )}
          <MessageList
            nsId={selectedNsId}
            entity={selectedEntity}
            viewMode={viewMode}
            selectedMessage={selectedMessage}
            onSelectMessage={setSelectedMessage}
          />
        </div>

        {/* Detail pane */}
        <div className="flex-1 overflow-auto">
          <MessageDetail
            message={selectedMessage}
            nsId={selectedNsId}
            entity={selectedEntity}
            viewMode={viewMode}
            onClose={() => setSelectedMessage(null)}
            onEditResubmit={(msg) => { setSelectedMessage(msg); setComposerMode("edit"); }}
            onReplay={(msg) => { setSelectedMessage(msg); setComposerMode("replay"); }}
            onSchedule={(msg) => { setSelectedMessage(msg); setComposerMode("schedule"); }}
          />
        </div>
      </div>

      {/* Message composer modal */}
      {composerMode && (
        <MessageComposer
          mode={composerMode}
          nsId={selectedNsId}
          namespaces={namespaces}
          entity={selectedEntity}
          sourceMessage={composerMode === "replay" || composerMode === "edit" ? selectedMessage : null}
          onClose={() => setComposerMode(null)}
        />
      )}

      {/* Batch send modal */}
      {showBatchSend && (
        <BatchSendPanel
          nsId={selectedNsId}
          namespaces={namespaces}
          entity={selectedEntity}
          onClose={() => setShowBatchSend(false)}
        />
      )}

      {/* Scheduled messages modal */}
      {showScheduled && selectedNsId && selectedEntity && (
        <ScheduledMessages
          nsId={selectedNsId}
          entityPath={selectedEntity.entityPath}
          onClose={() => setShowScheduled(false)}
        />
      )}

      {/* Batch replay modal */}
      {showBatchReplay && selectedNsId && selectedEntity && (
        <BatchReplayPanel
          nsId={selectedNsId}
          entity={selectedEntity}
          onClose={() => setShowBatchReplay(false)}
        />
      )}

      {/* Entity command palette */}
      <EntityCommandPalette
        open={showEntityPalette}
        nsId={selectedNsId}
        onClose={() => setShowEntityPalette(false)}
        onSelectEntity={(entity) => {
          setSelectedEntity(entity);
          setSelectedMessage(null);
        }}
        onAction={handleEntityAction}
      />
    </div>
  );
}
