import { useState } from "react";
import { useProfile } from "@/lib/hooks";
import { EntityTree } from "./EntityTree";
import { MessageList } from "./MessageList";
import { MessageDetail } from "./MessageDetail";
import type { SbEntityInfo, SbMessage } from "@/lib/types";

export function ServiceBusPage() {
  const { data: profile } = useProfile();
  const [selectedNsId, setSelectedNsId] = useState<string | null>(null);
  const [selectedEntity, setSelectedEntity] = useState<SbEntityInfo | null>(null);
  const [selectedMessage, setSelectedMessage] = useState<SbMessage | null>(null);
  const [viewMode, setViewMode] = useState<"active" | "dlq">("active");

  const namespaces = profile?.serviceBusNamespaces ?? [];

  return (
    <div className="flex h-full flex-col" data-testid="service-bus-page">
      {/* Namespace selector */}
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
      </div>

      {/* Main content: entity tree | message list | detail */}
      <div className="flex flex-1 overflow-hidden">
        {/* Entity tree */}
        <div className="w-64 overflow-auto border-r">
          <EntityTree
            nsId={selectedNsId}
            selectedEntity={selectedEntity}
            onSelectEntity={(entity) => {
              setSelectedEntity(entity);
              setSelectedMessage(null);
            }}
          />
        </div>

        {/* Message list */}
        <div className="flex w-80 flex-col overflow-hidden border-r">
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
          />
        </div>
      </div>
    </div>
  );
}
