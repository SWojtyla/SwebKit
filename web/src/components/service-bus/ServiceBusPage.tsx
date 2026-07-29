import { useState, useEffect, useCallback, useMemo } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { Plus, Upload, Clock, Search, RotateCcw, ChevronLeft } from "lucide-react";
import { useProfile, useSbPeekMessages, useSbPeekDlq } from "@/lib/hooks";
import { useQueryClient } from "@tanstack/react-query";
import { EntityTree } from "./EntityTree";
import { MessageList } from "./MessageList";
import { MessageDetail } from "./MessageDetail";
import { SidePanel } from "./SidePanel";
import { MessageComposer, type ComposerMode } from "./MessageComposer";
import { BatchSendPanel } from "./BatchSendPanel";
import { ScheduledMessages } from "./ScheduledMessages";
import { EntityCommandPalette, type EntityAction } from "./EntityCommandPalette";
import { BatchReplayPanel } from "./BatchReplayPanel";
import { loadSbPreferences } from "@/lib/stores/sb-preferences";
import type { SbEntityInfo, SbMessage } from "@/lib/types";

export function ServiceBusPage() {
  const { data: profile } = useProfile();
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [composerMode, setComposerMode] = useState<ComposerMode | null>(null);
  const [showBatchSend, setShowBatchSend] = useState(false);
  const [showScheduled, setShowScheduled] = useState(false);
  const [showEntityPalette, setShowEntityPalette] = useState(false);
  const [showBatchReplay, setShowBatchReplay] = useState(false);
  const [showEntityTree, setShowEntityTree] = useState(true);
  const queryClient = useQueryClient();

  const namespaces = profile?.serviceBusNamespaces ?? [];

  const updateParams = useCallback(
    (updates: Record<string, string | null | undefined>, options?: { replace?: boolean }) => {
      const next = new URLSearchParams(searchParams);
      for (const [key, value] of Object.entries(updates)) {
        if (value === null || value === undefined || value === "") next.delete(key);
        else next.set(key, value);
      }
      setSearchParams(next, { replace: options?.replace ?? false, preventScrollReset: true });
    },
    [searchParams, setSearchParams],
  );

  // Drill-down state is read from the URL so back/forward and deep links work.
  const selectedNsId = searchParams.get("ns");
  const setSelectedNsId = useCallback(
    (id: string | null) => updateParams({ ns: id, entity: null, entityName: null, msg: null, seq: null, view: null }),
    [updateParams],
  );

  const selectedEntity = useMemo<SbEntityInfo | null>(() => {
    const entityPath = searchParams.get("entity");
    if (!entityPath) return null;
    const name = searchParams.get("entityName") || entityPath;
    return { entityPath, name } as SbEntityInfo;
  }, [searchParams]);
  const setSelectedEntity = useCallback(
    (entity: SbEntityInfo | null) =>
      updateParams({
        entity: entity?.entityPath ?? null,
        entityName: entity?.name ?? null,
        msg: null,
        seq: null,
      }),
    [updateParams],
  );

  const viewMode = useMemo<"active" | "dlq">(() => {
    const v = searchParams.get("view");
    return v === "dlq" ? "dlq" : "active";
  }, [searchParams]);
  const setViewMode = useCallback(
    (mode: "active" | "dlq") => updateParams({ view: mode }),
    [updateParams],
  );

  const prefs = useMemo(() => {
    if (!selectedNsId || !selectedEntity) return { peekCount: 50 };
    return loadSbPreferences(selectedNsId, selectedEntity.entityPath);
  }, [selectedNsId, selectedEntity]);

  const activeMessagesQuery = useSbPeekMessages(
    viewMode === "active" ? selectedNsId : null,
    selectedEntity?.entityPath ?? null,
    prefs.peekCount,
  );
  const dlqMessagesQuery = useSbPeekDlq(
    viewMode === "dlq" ? selectedNsId : null,
    selectedEntity?.entityPath ?? null,
    prefs.peekCount,
  );
  const messages = viewMode === "active" ? activeMessagesQuery.data : dlqMessagesQuery.data;

  const selectedMessage = useMemo<SbMessage | null>(() => {
    const msgId = searchParams.get("msg");
    const seq = searchParams.get("seq");
    if (!msgId || seq === null || !messages) return null;
    const seqNum = parseInt(seq, 10);
    return messages.find((m) => m.messageId === msgId && m.sequenceNumber === seqNum) ?? null;
  }, [searchParams, messages]);
  const selectMessage = useCallback(
    (message: SbMessage | null) =>
      updateParams({
        msg: message?.messageId ?? null,
        seq: message?.sequenceNumber != null ? String(message.sequenceNumber) : null,
      }),
    [updateParams],
  );

  // Apply a namespace selected from the command palette.
  useEffect(() => {
    const state = location.state as { nsId?: string } | null;
    if (state?.nsId && namespaces.some((ns) => ns.id === state.nsId)) {
      const next = new URLSearchParams();
      next.set("ns", state.nsId);
      navigate({ pathname: location.pathname, search: next.toString() }, { replace: true, state: null });
    }
  }, [location, navigate, namespaces]);

  const handleEntityAction = useCallback((entity: SbEntityInfo, action: EntityAction) => {
    setSelectedEntity(entity);
    if (action === "peek-active") setViewMode("active");
    if (action === "peek-dlq") setViewMode("dlq");
    if (action === "send") setComposerMode("compose");
    if (action === "refresh") queryClient.invalidateQueries({ queryKey: ["sb-"] });
  }, [queryClient, setSelectedEntity, setViewMode]);

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
        <div className="flex flex-1 flex-col overflow-hidden border-r">
          {selectedEntity && (
            <div className="flex items-center gap-2 border-b px-3 py-1.5 text-xs" data-testid="sb-breadcrumb">
              <button
                type="button"
                onClick={() => setSelectedEntity(null)}
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
            onSelectMessage={selectMessage}
          />
        </div>

        {/* Detail pane */}
        {selectedMessage && (
          <SidePanel
            title="Message details"
            onClose={() => selectMessage(null)}
            defaultWidth={380}
            minWidth={240}
            maxWidth={600}
            storageKey="service-bus-message-detail"
          >
            <MessageDetail
              message={selectedMessage}
              nsId={selectedNsId}
              entity={selectedEntity}
              viewMode={viewMode}
              onEditResubmit={(msg) => { selectMessage(msg); setComposerMode("edit"); }}
              onReplay={(msg) => { selectMessage(msg); setComposerMode("replay"); }}
              onSchedule={(msg) => { selectMessage(msg); setComposerMode("schedule"); }}
            />
          </SidePanel>
        )}
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
        }}
        onAction={handleEntityAction}
      />
    </div>
  );
}
