import { Globe, Folder, Settings2, GitBranch, AlertTriangle, FolderGit2, HardDrive } from "lucide-react";
import type { ApiCollection, LinkedRootSummary } from "@/lib/types";
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
import { LinkedProjectsDialog } from "./LinkedProjectsDialog";
import { NewCollectionDialog } from "./NewCollectionDialog";
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

  // Split once so each picker offers only environments of its own scope. An environment
  // scoped to some *other* collection is deliberately in neither list — it cannot apply
  // here — but the Environment Manager still shows it, so it is never lost.
  const globalEnvironments = ctx.environments.filter((env) => env.collectionId === null);
  const scopedEnvironments = ctx.currentCollection
    ? ctx.environments.filter((env) => env.collectionId === ctx.currentCollection!.id)
    : [];

  return (
    // `relative` anchors the Git drawer to the page content area instead of the
    // whole viewport, so it no longer covers the app titlebar and status bar.
    <div className="relative flex h-full min-w-0 flex-col" data-testid="api-client-page">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2 border-b px-3 py-1.5 bg-card">
        {/* Two layers apply at once, so both pickers are always shown: a Global
            environment shared by every collection, and one scoped to the current
            collection that overrides it. The project picker is rendered even with no
            collection in context — disabled and saying why — because hiding it was how
            an estate of entirely collection-scoped environments ended up with nothing
            selectable anywhere. */}
        <div className="flex items-center gap-1" title="Global environment — applies to every collection">
          <Globe className="h-4 w-4 text-muted-foreground" />
          <span className="text-xs text-muted-foreground">Global</span>
          <select
            data-testid="env-selector"
            value={ctx.activeGlobalEnvironment?.id ?? ""}
            onChange={(e) => ctx.handleSetActiveEnvironment(e.target.value || null)}
            className="rounded border bg-background px-2 py-1 text-xs"
          >
            <option value="">— None —</option>
            {globalEnvironments.map((env) => (
              <option key={env.id} value={env.id}>{env.name}</option>
            ))}
          </select>
        </div>

        <span className="text-xs text-muted-foreground">+</span>

        <div
          className="flex items-center gap-1"
          title={
            ctx.currentCollection
              ? `Environment for ${ctx.currentCollection.name} — overrides the global one`
              : "Select a collection to choose its environment"
          }
        >
          <Folder className="h-4 w-4 text-muted-foreground" />
          <span className="text-xs text-muted-foreground">
            {ctx.currentCollection ? ctx.currentCollection.name : "Project"}
          </span>
          {/* Where edits to this collection land — files in the linked folder
              vs. SwebKit's own storage. Saving a request must never be a
              surprise about which it was. */}
          {ctx.currentCollection && (
            <StorageChip
              collection={ctx.currentCollection}
              roots={ctx.linkedRoots}
            />
          )}
          <select
            data-testid="env-selector-scoped"
            disabled={!ctx.currentCollection}
            value={ctx.activeScopedEnvironment?.id ?? ""}
            onChange={(e) =>
              ctx.currentCollection &&
              ctx.handleSetScopedEnvironment(ctx.currentCollection.id, e.target.value || null)
            }
            className="rounded border bg-background px-2 py-1 text-xs disabled:opacity-50"
          >
            {ctx.currentCollection ? (
              <>
                <option value="">— None —</option>
                {scopedEnvironments.map((env) => (
                  <option key={env.id} value={env.id}>{env.name}</option>
                ))}
              </>
            ) : (
              <option value="">— Select a collection first —</option>
            )}
          </select>
        </div>

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
        {/* What the two layers actually resolve to. The count is of the *merged* scope,
            which is the number that matters and which neither layer's own count gives. */}
        {ctx.activeEnvironment && (
          <span className="text-xs text-muted-foreground" data-testid="active-env-name">
            {ctx.activeEnvironment.name} ({Object.keys(ctx.variableScope).length} vars in scope)
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
        <div className="border-b bg-destructive/10 px-4 py-3" data-testid="conflict-banner">
          <div className="flex flex-wrap items-center gap-3">
            <AlertTriangle className="h-5 w-5 shrink-0 text-destructive" />
            <span className="flex-1 text-sm">{ctx.conflict.message}</span>
            <button onClick={ctx.handleReloadConflict} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-reload">Reload</button>
            <button onClick={ctx.handleOverwriteConflict} className="rounded bg-destructive px-3 py-1.5 text-xs text-destructive-foreground hover:opacity-90" data-testid="conflict-overwrite">Overwrite</button>
            <button onClick={ctx.handleSaveAsCopy} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-copy">Save as copy</button>
            <button onClick={ctx.dismissConflict} className="rounded border px-3 py-1.5 text-xs hover:bg-accent" data-testid="conflict-dismiss">Dismiss</button>
          </div>
          {ctx.conflict.conflicts.length > 0 && (
            <ul className="mt-2 max-h-24 list-disc space-y-0.5 overflow-auto pl-9 font-mono text-[11px] text-muted-foreground" data-testid="conflict-files">
              {ctx.conflict.conflicts.map((file) => (
                <li key={file} className="truncate" title={file}>{file}</li>
              ))}
            </ul>
          )}
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
            linkedRoots={ctx.linkedRoots}
            selectedNodeId={ctx.selectedNodeId}
            selectedCollectionId={ctx.selectedCollectionId}
            onSelectNode={ctx.handleSelectNode}
            onAddCollection={ctx.handleAddCollection}
            onAddRequest={ctx.handleAddRequest}
            onAddFolder={ctx.handleAddFolder}
            onDeleteNode={ctx.handleDeleteNode}
            onRenameNode={ctx.handleRenameNode}
            onMoveNode={ctx.handleMoveNode}
            onMoveCollection={ctx.handleMoveCollection}
            onExportCollection={ctx.setExportCollectionId}
            onManageProjects={() => ctx.setShowLinkedProjects(true)}
          />

          {/* No `border-r` here — RequestEditor already carries one, and the
              resizer provides the visual divider. */}
          <div className="flex h-full w-full flex-col">
            <RequestTabStrip
              tabs={ctx.tabs}
              activeTabId={ctx.activeTabId}
              onSelectTab={ctx.setActiveTabId}
              onCloseTab={ctx.closeTab}
              onCloseOtherTabs={ctx.closeOtherTabs}
              onCloseAllTabs={ctx.closeAllTabs}
              onPromoteTab={ctx.promoteTab}
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
              variableScope={ctx.variableScope}
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
          confirmText={ctx.confirmDialog.confirmText}
          onConfirm={ctx.confirmDialog.onConfirm}
          onCancel={() => ctx.setConfirmDialog(null)}
        />
      )}
      {ctx.showEnvManager && (
        <EnvironmentManager
          environments={ctx.environments}
          collections={ctx.collections}
          activeEnvironmentId={ctx.activeEnvironmentId}
          activeEnvironmentIdByCollection={ctx.activeEnvironmentIdByCollection}
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
      {ctx.showNewCollection && (
        <NewCollectionDialog
          roots={ctx.linkedRoots}
          onConfirm={ctx.handleCreateCollection}
          onCancel={() => ctx.setShowNewCollection(false)}
          onManageProjects={() => {
            ctx.setShowNewCollection(false);
            ctx.setShowLinkedProjects(true);
          }}
        />
      )}
      {ctx.showLinkedProjects && (
        <LinkedProjectsDialog onClose={() => ctx.setShowLinkedProjects(false)} />
      )}

      {/* Git drawer — sits inside the page content area rather than covering the
          app titlebar and status bar as the previous fixed overlay did. */}
      {ctx.showGitPanel && (
        <GitDrawer onClose={() => ctx.setShowGitPanel(false)} />
      )}
    </div>
  );
}

/** Tiny marker next to the project name in the toolbar — where saves go. */
function StorageChip({ collection, roots }: { collection: ApiCollection; roots: LinkedRootSummary[] }) {
  const root = collection.linkedRootId
    ? roots.find((r) => r.id === collection.linkedRootId)
    : null;

  if (collection.linkedRootId && !root) {
    return (
      <span
        className="flex items-center gap-0.5 rounded bg-destructive/10 px-1 py-0.5 text-[10px]"
        style={{ color: "var(--destructive)" }}
        title="This collection's linked folder is missing or disabled — check Project folders."
        data-testid="storage-chip-broken"
      >
        <FolderGit2 className="h-3 w-3" /> offline
      </span>
    );
  }

  return (
    <span
      className="flex items-center gap-0.5 rounded bg-accent/60 px-1 py-0.5 text-[10px] text-muted-foreground"
      title={
        root
          ? `Saves write files to ${root.path}${root.brunoSyncEnabled && root.brunoSyncFolderPath ? ` and update .bru files in ${root.brunoSyncFolderPath}` : ""}`
          : "Saved inside SwebKit's own storage (collections.json) — not synced to any folder"
      }
      data-testid={root ? "storage-chip-linked" : "storage-chip-local"}
    >
      {root ? <FolderGit2 className="h-3 w-3" /> : <HardDrive className="h-3 w-3" />}
      {root ? root.displayName : "app storage"}
    </span>
  );
}
