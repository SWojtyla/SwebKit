import { X } from "lucide-react";

export interface RequestTab {
  id: string;
  nodeId: string;
  collectionId: string;
  name: string;
  method: string;
  dirty: boolean;
}

interface RequestTabStripProps {
  tabs: RequestTab[];
  activeTabId: string | null;
  onSelectTab: (tabId: string) => void;
  onCloseTab: (tabId: string) => void;
}

const methodColors: Record<string, string> = {
  Get: "text-blue-500",
  Post: "text-green-500",
  Put: "text-yellow-500",
  Patch: "text-orange-500",
  Delete: "text-red-500",
  Head: "text-purple-500",
  Options: "text-gray-500",
  GraphQL: "text-pink-500",
  WebSocket: "text-cyan-500",
};

export function RequestTabStrip({ tabs, activeTabId, onSelectTab, onCloseTab }: RequestTabStripProps) {
  if (tabs.length === 0) return null;

  return (
    <div
      className="flex items-center gap-0.5 border-b bg-muted/30 overflow-x-auto"
      data-testid="request-tab-strip"
    >
      {tabs.map((tab) => (
        <div
          key={tab.id}
          className={`group flex cursor-pointer items-center gap-1.5 border-r px-3 py-1.5 text-xs whitespace-nowrap ${
            tab.id === activeTabId
              ? "bg-card text-foreground border-b-2 border-b-primary"
              : "text-muted-foreground hover:bg-accent/50"
          }`}
          onClick={() => onSelectTab(tab.id)}
          data-testid={`open-tab-${tab.id}`}
        >
          <span className={`font-semibold ${methodColors[tab.method] ?? ""}`}>
            {tab.method.toUpperCase()}
          </span>
          <span className="max-w-[120px] truncate">{tab.name}</span>
          {tab.dirty && <span className="text-yellow-500" data-testid={`tab-dirty-${tab.id}`}>●</span>}
          <button
            className="ml-1 rounded p-0.5 opacity-0 group-hover:opacity-100 hover:bg-accent hover:text-destructive"
            onClick={(e) => { e.stopPropagation(); onCloseTab(tab.id); }}
            data-testid={`tab-close-${tab.id}`}
          >
            <X className="h-3 w-3" />
          </button>
        </div>
      ))}
    </div>
  );
}
