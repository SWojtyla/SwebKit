import { Globe, Settings2, GitBranch, AlertTriangle } from "lucide-react";
import { ApiClientPageProvider, useApiClientPageContext } from "./ApiClientPageContext";
import { CollectionTree } from "./CollectionTree";
import { RequestEditor } from "./RequestEditor";
import { ResponseViewer } from "./ResponseViewer";
import { NameDialog, ConfirmDialog } from "./Dialogs";
import { EnvironmentManager } from "./EnvironmentManager";
import { CollectionVariableEditor } from "./CollectionVariableEditor";
import { RequestTabStrip } from "./RequestTabStrip";
import { CollectionExportDialog } from "./CollectionExportDialog";
import { GitDrawer } from "./GitDrawer";
import { ResizablePanels } from "@/components/ui/ResizablePanels";

export function ApiClientPage() {
  return (
    <ApiClientPageProvider>
      <ApiClientPageContent />
    </ApiClientPageProvider>
  );
}

function ApiClientPageContent() {
  const ctx = useApiClientPageContext();

  if (ctx.isLoading) {
    return (
      <div className="flex h-full items-center justify-center" data-testid="api-client-page">
        Loading collections...
      </div>
    );
  }

  return (
    // `relative` anchors the Git drawer to the page content area instead of the
    // whole viewport, so it no longer covers the app titlebar and status bar.
    <div className="relative flex h-full min-w-0 flex-col" data-testid="api-client-page">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-1.5 bg-card">
        <Globe className="h-4 w-4 text-muted-foreground" />
        <select
          data-testid="env-selector"
          value={ctx.activeEnvironmentId ?? ""}
          onChange={(e) => ctx.handleSetActiveEnvironment(e.target.value || null)}
          className="rounded border bg-background px-2 py-1 text-xs"
        >
          <option value="">— No environment —</option>
          {ctx.environments.map((env) => (
            <option key={env.id} value={env.id}>{env.name}</option>
          ))}
        </select>
        <button
          onClick={() => ctx.setShowEnvManager(true)}
          className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
          data-testid="env-manager-button"
        >
          <Settings2 className="h-3 w-3" /> Manage
        </button>
        {ctx.selectedCollection && (
          <button
            onClick={() => ctx.setShowColVarEditor(true)}
            className="rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="col-vars-button"
          >
            Collection Variables
          </button>
        )}
        {ctx.activeEnvironment && (
          <span className="text-xs text-muted-foreground" data-testid="active-env-name">
            {ctx.activeEnvironment.name} ({ctx.activeEnvironment.variables.filter((v) => v.isEnabled).length} vars)
          </span>
        )}
        <div className="ml-auto" />
        <button
          onClick={() => ctx.setShowGitPanel(!ctx.showGitPanel)}
          className={`flex items-center gap-1 rounded border px-2 py-1 text-xs ${ctx.showGitPanel ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
          data-testid="api-client-git-toggle"
        >
          <GitBranch className="h-3 w-3" /> Git
        </button>
      </div>

      {/* Conflict-resolution banner */}
      {ctx.conflict && (
        <div className="flex flex-wrap items-center gap-3 border-b bg-destructive/10 px-4 py-3" data-testid="conflict-banner">
          <AlertTriangle className="h-5 w-5 shrink-0 text-destructive" />
          <span className="flex-1 text-sm">{ctx.conflict.message}</span>
          <button onClick={ctx.handleReloadConflict} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-reload">Reload</button>
          <button onClick={ctx.handleOverwriteConflict} className="rounded bg-destructive px-3 py-1.5 text-xs text-destructive-foreground hover:opacity-90" data-testid="conflict-overwrite">Overwrite</button>
          <button onClick={ctx.handleSaveAsCopy} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-copy">Save as copy</button>
          <button onClick={ctx.dismissConflict} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-dismiss">Dismiss</button>
        </div>
      )}

      {/* Legacy plaintext secret notice */}
      {ctx.legacySecretCount > 0 && !ctx.legacyNoticeDismissed && (
        <div className="flex items-start gap-2 border-b px-3 py-2 text-xs"
          style={{
            color: "var(--warning)",
            backgroundColor: "color-mix(in oklch, var(--warning) 12%, transparent)",
          }}
          data-testid="legacy-secret-notice">
          <span className="flex-1">
            {ctx.legacySecretCount} API Client auth value{ctx.legacySecretCount === 1 ? "" : "s"} look{ctx.legacySecretCount === 1 ? "s" : ""} like a raw secret stored in collections.json.
            Re-enter {ctx.legacySecretCount === 1 ? "it" : "them"} to move {ctx.legacySecretCount === 1 ? "it" : "them"} to the secure store.
          </span>
          <button
            onClick={ctx.dismissLegacyNotice}
            className="shrink-0 rounded border px-2 py-0.5 hover:bg-accent"
            data-testid="legacy-secret-notice-dismiss"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Main 3-pane layout */}
      <div className="flex min-w-0 flex-1 overflow-hidden">
        {/* The tree's useful width does not scale with the window, so it stays
            roughly fixed while request and response split the leftover space —
            previously the response pane was the only `flex: 1` child and absorbed
            every spare pixel on a wide monitor. */}
        {/* Minimums are sized so all three panes still fit — and stay draggable —
            at a 1280px-wide window; larger values pinned every pane to its
            minimum on a laptop and overflowed the container. */}
        <ResizablePanels
          initialWidths={[300, "1fr", "1fr"]}
          minWidths={[200, 340, 320]}
          storageKey="api-client-panels"
          panelLabels={["collections", "request", "response"]}
          className="w-full min-w-0"
        >
          <CollectionTree
            collections={ctx.collections}
            selectedNodeId={ctx.selectedNodeId}
            selectedCollectionId={ctx.selectedCollectionId}
            onSelectNode={ctx.handleSelectNode}
            onAddCollection={ctx.handleAddCollection}
            onAddRequest={ctx.handleAddRequest}
            onAddFolder={ctx.handleAddFolder}
            onDeleteNode={ctx.handleDeleteNode}
            onRenameNode={ctx.handleRenameNode}
            onExportCollection={ctx.setExportCollectionId}
          />

          {/* No `border-r` here — RequestEditor already carries one, and the
              resizer provides the visual divider. */}
          <div className="flex h-full w-full flex-col">
            <RequestTabStrip
              tabs={ctx.tabs}
              activeTabId={ctx.activeTabId}
              onSelectTab={ctx.setActiveTabId}
              onCloseTab={ctx.closeTab}
            />
            {ctx.activeTabId && ctx.tabStates[ctx.activeTabId] ? (
              <RequestEditor
                request={ctx.tabStates[ctx.activeTabId].draft}
                onChange={(req) => ctx.updateTabDraft(ctx.activeTabId!, req)}
                onSend={ctx.handleSend}
                onSave={ctx.handleSave}
                sending={ctx.tabStates[ctx.activeTabId].sending}
                variableScope={ctx.variableScope}
                environments={ctx.environments}
                captureWarnings={ctx.tabStates[ctx.activeTabId]?.response?.captureWarnings ?? []}
              />
            ) : (
              <div className="flex h-full flex-col items-center justify-center gap-2 p-6 text-sm text-muted-foreground">
                <span data-testid="api-client-empty-editor">
                  Select or create a request to start editing.
                </span>
              </div>
            )}
          </div>

          <div className="flex h-full w-full flex-col overflow-hidden">
            <ResponseViewer
              response={ctx.activeTabId ? ctx.tabStates[ctx.activeTabId]?.response ?? null : null}
              sending={ctx.activeTabId ? ctx.tabStates[ctx.activeTabId]?.sending ?? false : false}
              request={ctx.activeTabId ? ctx.tabStates[ctx.activeTabId]?.draft ?? null : null}
              history={ctx.activeTabId ? ctx.tabStates[ctx.activeTabId]?.history ?? [] : []}
              onSaveExample={ctx.handleSaveExample}
            />
          </div>
        </ResizablePanels>
      </div>

      {/* Dialogs */}
      {ctx.nameDialog && (
        <NameDialog
          title={ctx.nameDialog.title}
          label={ctx.nameDialog.label}
          defaultValue={ctx.nameDialog.defaultValue}
          confirmText={ctx.nameDialog.confirmText}
          onConfirm={ctx.nameDialog.onConfirm}
          onCancel={() => ctx.setNameDialog(null)}
        />
      )}
      {ctx.confirmDialog && (
        <ConfirmDialog
          message={ctx.confirmDialog.message}
          onConfirm={ctx.confirmDialog.onConfirm}
          onCancel={() => ctx.setConfirmDialog(null)}
        />
      )}
      {ctx.showEnvManager && (
        <EnvironmentManager
          environments={ctx.environments}
          collections={ctx.collections}
          activeEnvironmentId={ctx.activeEnvironmentId}
          onSave={ctx.handleSaveEnvironments}
          onClose={() => ctx.setShowEnvManager(false)}
        />
      )}
      {ctx.showColVarEditor && ctx.selectedCollection && (
        <CollectionVariableEditor
          collection={ctx.selectedCollection}
          onSave={ctx.handleSaveCollectionVariables}
          onClose={() => ctx.setShowColVarEditor(false)}
        />
      )}
      {ctx.exportCollectionId && ctx.exportCollection && (
        <CollectionExportDialog
          collection={ctx.exportCollection}
          environments={ctx.environments}
          onClose={() => ctx.setExportCollectionId(null)}
        />
      )}

      {/* Git drawer — sits inside the page content area rather than covering the
          app titlebar and status bar as the previous fixed overlay did. */}
      {ctx.showGitPanel && (
        <GitDrawer onClose={() => ctx.setShowGitPanel(false)} />
      )}
    </div>
  );
}
