import { useMemo } from "react";
import { RotateCcw } from "lucide-react";
import { useNavigate } from "react-router";
import { SearchableSelect } from "@/components/shared/SearchableSelect";
import { QueryState } from "@/components/shared/QueryState";
import { StoragePageProvider } from "./StoragePageContext";
import {
    useStorageAccount,
    useStorageNav,
    useStorageQueries,
    useStorageShare,
} from "./storage-context";
import { BlobBrowserPanel } from "./BlobBrowserPanel";
import { BlobDetailPanel } from "./BlobDetailPanel";
import { BlobRecoveryPanel } from "./BlobRecoveryPanel";
import { ShareBrowserPanel } from "./ShareBrowserPanel";
import { ShareFileDetailPanel } from "./ShareFileDetailPanel";
import { ResizablePanels } from "@/components/ui/ResizablePanels";

export function StoragePage() {
    return (
        <StoragePageProvider>
            <StoragePageContent />
        </StoragePageProvider>
    );
}

function StoragePageContent() {
    const account = useStorageAccount();
    const nav = useStorageNav();
    const share = useStorageShare();
    const queries = useStorageQueries();
    const ctx = useMemo(
        () => ({ ...account, ...nav, ...share, ...queries }),
        [account, nav, share, queries],
    );
    const navigate = useNavigate();

    if (!ctx.resolvedAccountId) {
        return (
            <div className="p-6" data-testid="storage-page">
                <h1 className="text-2xl font-bold" data-testid="storage-title">
                    Storage
                </h1>
                <p
                    className="mt-4 text-muted-foreground"
                    data-testid="storage-no-account"
                >
                    No storage account configured. Add one in{" "}
                    <button
                        onClick={() =>
                            navigate("/settings", { state: { tab: "storage" } })
                        }
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
        <div
            key="containers"
            className="h-full w-full overflow-auto"
            data-testid="storage-container-list"
        >
            <div className="px-3 py-2 text-xs font-semibold text-muted-foreground uppercase">
                Containers
            </div>
            <QueryState
                isLoading={ctx.containers.isLoading}
                error={ctx.containers.error}
                data={ctx.containers.data}
                emptyTitle="No containers"
                emptyDescription="This storage account has no blob containers."
                skeletonRows={6}
            >
                {(containers) =>
                    containers.map((c) => (
                        <button
                            key={c.name}
                            data-testid={`storage-container-${c.name}`}
                            onClick={() => ctx.handleSelectContainer(c.name)}
                            className={`flex w-full items-center px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent ${
                                ctx.selectedContainer === c.name
                                    ? "bg-accent"
                                    : ""
                            }`}
                            title={c.name}
                        >
                            <span className="truncate font-mono">{c.name}</span>
                        </button>
                    ))
                }
            </QueryState>

            {/* File shares sit in the same tree — picking one swaps the browser and
                detail panes for the share equivalents (URL params keep share and
                container selection mutually exclusive). */}
            <div className="mt-2 border-t px-3 py-2 text-xs font-semibold text-muted-foreground uppercase">
                File Shares
            </div>
            <QueryState
                isLoading={ctx.fileShares.isLoading}
                error={ctx.fileShares.error}
                data={ctx.fileShares.data}
                emptyTitle="No file shares"
                emptyDescription="This storage account has no Azure Files shares."
                skeletonRows={2}
            >
                {(shares) =>
                    shares.map((s) => (
                        <button
                            key={s.name}
                            data-testid={`storage-share-${s.name}`}
                            onClick={() => ctx.handleSelectShare(s.name)}
                            className={`flex w-full items-center px-3 py-1.5 text-left text-sm transition-colors hover:bg-accent ${
                                ctx.selectedShare === s.name ? "bg-accent" : ""
                            }`}
                            title={
                                s.quotaGiB
                                    ? `${s.name} (${s.quotaGiB} GiB)`
                                    : s.name
                            }
                        >
                            <span className="truncate font-mono">{s.name}</span>
                        </button>
                    ))
                }
            </QueryState>
        </div>
    );

    // An explicit array, not a conditional fragment: ResizablePanels sizes one pane per
    // direct child, and a fragment would collapse the browser and detail panes into one.
    const inShareView = !recovery && ctx.selectedShare !== null;
    const panels = recovery
        ? [containerList, <BlobRecoveryPanel key="recovery" />]
        : inShareView
          ? [
                containerList,
                <ShareBrowserPanel key="share-browser" />,
                <ShareFileDetailPanel key="share-detail" />,
            ]
          : [
                containerList,
                <BlobBrowserPanel key="browser" />,
                <BlobDetailPanel key="detail" />,
            ];

    return (
        <div className="flex h-full flex-col" data-testid="storage-page">
            <div className="border-b px-6 py-3">
                <div className="flex items-center justify-between gap-3">
                    <div className="flex items-center gap-3">
                        <h1
                            className="text-2xl font-bold"
                            data-testid="storage-title"
                        >
                            Storage
                        </h1>
                        {ctx.accounts.length > 1 && (
                            <SearchableSelect
                                items={ctx.accounts.map((a) => ({
                                    value: a.id,
                                    label: a.displayName,
                                    subtitle: a.accountName,
                                }))}
                                value={ctx.resolvedAccountId}
                                onChange={(item) =>
                                    ctx.handleSelectAccount(item.value)
                                }
                                placeholder="Select account..."
                                filterPlaceholder="Filter accounts..."
                                testId="storage-account"
                                nativeSelectTestId="storage-account-select"
                                listAriaLabel="Storage accounts"
                                buttonClassName="min-w-[10rem]"
                            />
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
                        <span
                            title={
                                ctx.selectedContainer
                                    ? "View deleted blobs in this container"
                                    : "Select a container to view its deleted blobs"
                            }
                        >
                            <button
                                onClick={() =>
                                    ctx.setStorageViewMode("recovery")
                                }
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
                    initialWidths={
                        recovery ? [220, "1fr"] : [220, "1fr", "1fr"]
                    }
                    minWidths={recovery ? [160, 320] : [160, 280, 320]}
                    storageKey={
                        recovery ? "storage-recovery-panels" : "storage-panels"
                    }
                    panelLabels={
                        recovery
                            ? ["containers", "recovery"]
                            : ["containers", "blobs", "blob detail"]
                    }
                    className="w-full min-w-0"
                >
                    {panels}
                </ResizablePanels>
            </div>
        </div>
    );
}
