import { useEffect, useState } from "react";
import { X } from "lucide-react";
import { MethodBadge } from "./method-badge";

export interface RequestTab {
  id: string;
  nodeId: string;
  collectionId: string;
  name: string;
  method: string;
  dirty: boolean;
  /**
   * A single reusable "preview" tab for single-click tree navigation: opening
   * another node replaces its content instead of accumulating a permanent tab
   * per row clicked. Promoted to a permanent tab (this becomes `false`) as
   * soon as the draft is edited or the tab is double-clicked.
   */
  isPreview?: boolean;
}

interface TabContextMenuState {
  x: number;
  y: number;
  tabId: string;
}

interface RequestTabStripProps {
  tabs: RequestTab[];
  activeTabId: string | null;
  onSelectTab: (tabId: string) => void;
  onCloseTab: (tabId: string) => void;
  onCloseOtherTabs?: (tabId: string) => void;
  onCloseAllTabs?: () => void;
  onPromoteTab?: (tabId: string) => void;
}

export function RequestTabStrip({
  tabs,
  activeTabId,
  onSelectTab,
  onCloseTab,
  onCloseOtherTabs,
  onCloseAllTabs,
  onPromoteTab,
}: RequestTabStripProps) {
  const [contextMenu, setContextMenu] = useState<TabContextMenuState | null>(null);

  useEffect(() => {
    if (!contextMenu) return;
    const close = () => setContextMenu(null);
    document.addEventListener("click", close);
    document.addEventListener("contextmenu", close);
    return () => {
      document.removeEventListener("click", close);
      document.removeEventListener("contextmenu", close);
    };
  }, [contextMenu]);

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
          onDoubleClick={() => onPromoteTab?.(tab.id)}
          // Middle-click closes a tab, matching the convention of every
          // browser and IDE tab strip. `onMouseDown` also suppresses the
          // platform's own middle-click autoscroll cursor.
          onMouseDown={(e) => {
            if (e.button === 1) e.preventDefault();
          }}
          onAuxClick={(e) => {
            if (e.button === 1) {
              e.preventDefault();
              onCloseTab(tab.id);
            }
          }}
          onContextMenu={(e) => {
            e.preventDefault();
            e.stopPropagation();
            setContextMenu({ x: e.clientX, y: e.clientY, tabId: tab.id });
          }}
          data-testid={`open-tab-${tab.id}`}
        >
          <MethodBadge method={tab.method} variant="text" />
          <span className={`max-w-[120px] truncate ${tab.isPreview ? "italic" : ""}`}>{tab.name}</span>
          {tab.dirty && (
            <span style={{ color: "var(--warning)" }} data-testid={`tab-dirty-${tab.id}`}>●</span>
          )}
          <button
            className="ml-1 rounded p-0.5 opacity-0 group-hover:opacity-100 hover:bg-accent hover:text-destructive"
            onClick={(e) => { e.stopPropagation(); onCloseTab(tab.id); }}
            data-testid={`tab-close-${tab.id}`}
          >
            <X className="h-3 w-3" />
          </button>
        </div>
      ))}

      {contextMenu && (
        <div
          className="fixed z-50 min-w-[160px] rounded-md border bg-popover py-1 shadow-lg"
          style={{
            left: Math.min(contextMenu.x, window.innerWidth - 180),
            top: Math.min(contextMenu.y, window.innerHeight - 140),
          }}
          data-testid="tab-context-menu"
          onClick={(e) => e.stopPropagation()}
        >
          <button
            className="flex w-full items-center px-3 py-1.5 text-left text-sm hover:bg-accent"
            onClick={() => { onCloseTab(contextMenu.tabId); setContextMenu(null); }}
            data-testid="tab-ctx-close"
          >
            Close
          </button>
          <button
            className="flex w-full items-center px-3 py-1.5 text-left text-sm hover:bg-accent disabled:opacity-40"
            disabled={tabs.length <= 1}
            title={tabs.length <= 1 ? "Only one tab open" : undefined}
            onClick={() => { onCloseOtherTabs?.(contextMenu.tabId); setContextMenu(null); }}
            data-testid="tab-ctx-close-others"
          >
            Close Others
          </button>
          <button
            className="flex w-full items-center px-3 py-1.5 text-left text-sm hover:bg-accent"
            onClick={() => { onCloseAllTabs?.(); setContextMenu(null); }}
            data-testid="tab-ctx-close-all"
          >
            Close All
          </button>
        </div>
      )}
    </div>
  );
}
