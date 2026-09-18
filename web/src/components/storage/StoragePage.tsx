import { RotateCcw } from "lucide-react";
import { useNavigate } from "react-router";
import { StoragePageProvider, useStoragePageContext } from "./StoragePageContext";
import { BlobBrowserPanel } from "./BlobBrowserPanel";
import { BlobDetailPanel } from "./BlobDetailPanel";
import { BlobRecoveryPanel } from "./BlobRecoveryPanel";
import { ResizablePanels } from "@/components/ui/ResizablePanels";

export function StoragePage() {
  return (
    <StoragePageProvider>
      <StoragePageContent />
    </StoragePageProvider>
  );
}

function StoragePageContent() {
  const ctx = useStoragePageContext();
  const navigate = useNavigate();

  if (!ctx.resolvedAccountId) {
    return (
      <div className="p-6" data-testid="storage-page">
        <h1 className="text-2xl font-bold" data-testid="storage-title">Storage</h1>
        <p className="mt-4 text-muted-foreground" data-testid="storage-no-account">
          No storage account configured. Add one in{" "}
          <button
            onClick={() => navigate("/settings", { state: { tab: "storage" } })}
            className="text-primary underline"
            data-testid="storage-goto-settings"
          >
            Settings → Storage
          </button>
          .
        </p>
      </div>
    );
  }

  const recovery = ctx.storageViewMode === "recovery";

  const containerList = (
    <div key="containers" className="h-full w-full overflow-auto" data-testid="storage-container-list">
      <div className="px-3 py-2 text-xs font-semibold text-muted-foreground uppercase">Containers</div>
      {ctx.containers.isLoading && (
        <div className="px-3 py-2 text-sm text-muted-foreground">Loading...</div>
      )}
      {ctx.containers.error && (
        <div className="px-3 py-2 text-sm text-destructive" data-testid="storage-container-error">
          Error: {ctx.containers.error.message}
        </div>
      )}
      {ctx.containers.data?.map((c) => (
        <button
          key={c.name}
          data-testid={`storage-container-${c.name}`}
          onClick={() => ctx.handleSelectContainer(c.name)}
          className={`flex w-full items-center px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent ${
            ctx.selectedContainer === c.name ? "bg-accent" : ""
          }`}
          title={c.name}
        >
          <span className="truncate font-mono">{c.name}</span>
        </button>
      ))}
      {(!ctx.containers.data || ctx.containers.data.length === 0) && !ctx.containers.isLoading && (
        <div className="px-3 py-2 text-sm text-muted-foreground">No containers</div>
      )}
    </div>
  );

  // An explicit array, not a conditional fragment: ResizablePanels sizes one pane per
  // direct child, and a fragment would collapse the browser and detail panes into one.
  const panels = recovery
    ? [containerList, <BlobRecoveryPanel key="recovery" />]
    : [containerList, <BlobBrowserPanel key="browser" />, <BlobDetailPanel key="detail" />];

  return (
    <div className="flex h-full flex-col" data-testid="storage-page">
      <div className="border-b px-6 py-3">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-bold" data-testid="storage-title">Storage</h1>
            {ctx.accounts.length > 1 && (
              <select
                data-testid="storage-account-select"
                value={ctx.resolvedAccountId ?? ""}
                onChange={(e) => ctx.handleSelectAccount(e.target.value)}
                className="rounded-md border bg-background px-2 py-1 text-sm"
              >
                {ctx.accounts.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.displayName}
                  </option>
                ))}
              </select>
            )}
          </div>
          <div className="flex gap-1">
            <button
              onClick={() => ctx.setStorageViewMode("browser")}
              className={`rounded-md px-3 py-1.5 text-xs ${ctx.storageViewMode === "browser" ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
              data-testid="storage-view-browser"
            >
              Browser
            </button>
            <span title={ctx.selectedContainer ? "View deleted blobs in this container" : "Select a container to view its deleted blobs"}>
              <button
                onClick={() => ctx.setStorageViewMode("recovery")}
                disabled={!ctx.selectedContainer}
                className={`flex items-center gap-1 rounded-md px-3 py-1.5 text-xs disabled:cursor-not-allowed disabled:opacity-50 ${ctx.storageViewMode === "recovery" ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
                data-testid="storage-view-recovery"
              >
                <RotateCcw className="h-3 w-3" />
                Recovery
              </button>
            </span>
          </div>
        </div>
      </div>

      <div className="flex min-w-0 flex-1 overflow-hidden">
        {/* Recovery mode drops the detail pane, so it persists under its own key — a stored
            3-panel layout is discarded rather than migrated on a panel-count change. */}
        <ResizablePanels
          key={recovery ? "recovery" : "browser"}
          initialWidths={recovery ? [220, "1fr"] : [220, "1fr", "1fr"]}
          minWidths={recovery ? [160, 320] : [160, 280, 320]}
          storageKey={recovery ? "storage-recovery-panels" : "storage-panels"}
          panelLabels={recovery ? ["containers", "recovery"] : ["containers", "blobs", "blob detail"]}
          className="w-full min-w-0"
        >
          {panels}
        </ResizablePanels>
      </div>
    </div>
  );
}
