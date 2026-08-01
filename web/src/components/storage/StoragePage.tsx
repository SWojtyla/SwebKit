import { RotateCcw } from "lucide-react";
import { StoragePageProvider, useStoragePageContext } from "./StoragePageContext";
import { BlobBrowserPanel } from "./BlobBrowserPanel";
import { BlobDetailPanel } from "./BlobDetailPanel";
import { BlobRecoveryPanel } from "./BlobRecoveryPanel";

export function StoragePage() {
  return (
    <StoragePageProvider>
      <StoragePageContent />
    </StoragePageProvider>
  );
}

function StoragePageContent() {
  const ctx = useStoragePageContext();

  if (!ctx.resolvedAccountId) {
    return (
      <div className="p-6" data-testid="storage-page">
        <h1 className="text-2xl font-bold" data-testid="storage-title">Storage</h1>
        <p className="mt-4 text-muted-foreground" data-testid="storage-no-account">
          No storage account configured. Add one in Settings.
        </p>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col" data-testid="storage-page">
      <div className="border-b px-6 py-3">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold" data-testid="storage-title">Storage</h1>
          <div className="flex gap-1">
            <button
              onClick={() => ctx.setStorageViewMode("browser")}
              className={`rounded-md px-3 py-1.5 text-xs ${ctx.storageViewMode === "browser" ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
              data-testid="storage-view-browser"
            >
              Browser
            </button>
            <button
              onClick={() => ctx.setStorageViewMode("recovery")}
              disabled={!ctx.selectedContainer}
              className={`flex items-center gap-1 rounded-md px-3 py-1.5 text-xs disabled:opacity-50 ${ctx.storageViewMode === "recovery" ? "bg-primary text-primary-foreground" : "border hover:bg-accent"}`}
              data-testid="storage-view-recovery"
            >
              <RotateCcw className="h-3 w-3" />
              Recovery
            </button>
          </div>
        </div>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Container list */}
        <div className="w-48 border-r overflow-auto" data-testid="storage-container-list">
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
            >
              <span className="truncate font-mono">{c.name}</span>
            </button>
          ))}
          {(!ctx.containers.data || ctx.containers.data.length === 0) && !ctx.containers.isLoading && (
            <div className="px-3 py-2 text-sm text-muted-foreground">No containers</div>
          )}
        </div>

        {/* Recovery mode */}
        {ctx.storageViewMode === "recovery" ? (
          <div className="flex-1 overflow-auto">
            <BlobRecoveryPanel />
          </div>
        ) : (
          <>
            <BlobBrowserPanel />
            <BlobDetailPanel />
          </>
        )}
      </div>
    </div>
  );
}
