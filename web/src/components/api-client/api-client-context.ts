import { createContext, useContext } from "react";
import type { ResponseHistoryEntry } from "./ResponseViewer";
import type { RequestTab } from "./RequestTabStrip";
import type { MoveNodeTarget, MoveCollectionTarget } from "@/lib/collection-tree-utils";
import type {
  ApiCollection,
  ApiCollectionNode,
  HttpRequestEntry,
  ApiClientExecutionResponse,
  ApiEnvironment,
  CollectionVariable,
} from "@/lib/types";

export interface TabState {
  draft: HttpRequestEntry;
  response: ApiClientExecutionResponse | null;
  sending: boolean;
  dirty: boolean;
  /**
   * Owned here rather than in `ResponseViewer` so history survives a remount and
   * is scoped per tab instead of per mounted component.
   */
  history: ResponseHistoryEntry[];
}

export interface NameDialogState {
  title: string;
  label: string;
  defaultValue: string;
  confirmText: string;
  onConfirm: (name: string) => void;
}

export interface ConfirmDialogState {
  message: string;
  onConfirm: () => void;
  /**
   * The confirm button's label. Reserve "Delete" (the `ConfirmDialog` default)
   * for actual deletion — a tab close or a git revert is a different action
   * with a different consequence and should say so.
   */
  confirmText?: string;
}

export interface ApiClientPageContextValue {
  collections: ApiCollection[];
  isLoading: boolean;

  environments: ApiEnvironment[];
  activeEnvironmentId: string | null;
  /** The layer that wins — the scoped one when set, otherwise the global one. */
  activeEnvironment: ApiEnvironment | null;
  /** The global layer, applied to every collection. */
  activeGlobalEnvironment: ApiEnvironment | null;
  /** The layer scoped to the current collection, applied over the global one. */
  activeScopedEnvironment: ApiEnvironment | null;
  /** The collection the environment pickers work against: the open tab's, else the tree selection. */
  currentCollection: ApiCollection | null;
  /** Active collection-scoped environment per collection id, for display in the manager. */
  activeEnvironmentIdByCollection: Record<string, string>;
  handleSetActiveEnvironment: (envId: string | null) => void;
  handleSetScopedEnvironment: (collectionId: string, envId: string | null) => void;
  handleSaveEnvironments: (envs: ApiEnvironment[], activeId: string | null) => void;

  selectedNodeId: string | null;
  selectedCollectionId: string | null;
  selectedCollection: ApiCollection | null;
  handleSelectNode: (node: ApiCollectionNode, collectionId: string) => void;

  variableScope: Record<string, string | null>;

  handleAddCollection: () => void;
  handleAddRequest: (collectionId: string, parentId?: string) => void;
  handleAddFolder: (collectionId: string, parentId?: string) => void;
  handleDeleteNode: (nodeId: string, collectionId: string) => void;
  handleRenameNode: (nodeId: string, collectionId: string, newName: string) => void;
  handleMoveNode: (nodeId: string, sourceCollectionId: string, target: MoveNodeTarget) => void;
  handleMoveCollection: (collectionId: string, target: MoveCollectionTarget) => void;

  conflict: { message: string } | null;
  dismissConflict: () => void;
  handleReloadConflict: () => Promise<void>;
  handleOverwriteConflict: () => Promise<void>;
  handleSaveAsCopy: () => Promise<void>;

  legacySecretCount: number;
  legacyNoticeDismissed: boolean;
  dismissLegacyNotice: () => void;

  showEnvManager: boolean;
  setShowEnvManager: (v: boolean) => void;
  showColVarEditor: boolean;
  setShowColVarEditor: (v: boolean) => void;
  exportCollectionId: string | null;
  setExportCollectionId: (v: string | null) => void;
  exportCollection: ApiCollection | null;
  showGitPanel: boolean;
  setShowGitPanel: (v: boolean) => void;

  nameDialog: NameDialogState | null;
  setNameDialog: (v: NameDialogState | null) => void;
  confirmDialog: ConfirmDialogState | null;
  setConfirmDialog: (v: ConfirmDialogState | null) => void;

  handleSaveCollectionVariables: (variables: CollectionVariable[]) => void;
}

/**
 * Per-keystroke tab state, isolated from `ApiClientPageContextValue`: every
 * `updateTabDraft` bumps `tabs`/`tabStates`, so only the tab strip, request
 * editor and response viewer should subscribe — subscribing here is opting
 * into a re-render on every keystroke in the request editor.
 */
export interface ApiClientTabsContextValue {
  tabs: RequestTab[];
  activeTabId: string | null;
  setActiveTabId: (tabId: string | null) => void;
  tabStates: Record<string, TabState>;
  closeTab: (tabId: string) => void;
  closeOtherTabs: (tabId: string) => void;
  closeAllTabs: () => void;
  promoteTab: (tabId: string) => void;
  updateTabDraft: (tabId: string, draft: HttpRequestEntry) => void;

  activeTab: RequestTab | null;
  activeCollection: ApiCollection | null | undefined;

  handleSave: () => Promise<boolean>;
  handleSend: () => Promise<void>;
  handleSaveExample: (name: string, response: ApiClientExecutionResponse) => Promise<void>;
}

export const ApiClientPageContext = createContext<ApiClientPageContextValue | null>(null);
export const ApiClientTabsContext = createContext<ApiClientTabsContextValue | null>(null);

export function useApiClientPageContext(): ApiClientPageContextValue {
  const ctx = useContext(ApiClientPageContext);
  if (!ctx) throw new Error("useApiClientPageContext must be used within ApiClientPageProvider");
  return ctx;
}

export function useApiClientTabs(): ApiClientTabsContextValue {
  const ctx = useContext(ApiClientTabsContext);
  if (!ctx) throw new Error("useApiClientTabs must be used within ApiClientPageProvider");
  return ctx;
}
