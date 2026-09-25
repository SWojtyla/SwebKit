import { useMemo, useState } from "react";
import { RotateCcw, Search } from "lucide-react";
import { useUndeleteBlob } from "@/lib/hooks";
import {
    useStorageAccount,
    useStorageActions,
    useStorageNav,
    useStorageQueries,
} from "./StoragePageContext";
import { formatBytes } from "@/lib/format-bytes";
import { formatLocalDateTime } from "@/lib/datetime";
import { ConfirmBar } from "@/components/shared/ConfirmBar";

interface DeletedBlob {
    name: string;
    deletedAt: string;
    daysRemaining: number;
    sizeBytes: number | null;
}

export function BlobRecoveryPanel() {
    const account = useStorageAccount();
    const nav = useStorageNav();
    const queries = useStorageQueries();
    const actions = useStorageActions();
    const ctx = useMemo(
        () => ({ ...account, ...nav, ...queries, ...actions }),
        [account, nav, queries, actions],
    );
    const accountId = ctx.resolvedAccountId;
    const container = ctx.selectedContainer;
    const allowMutations = ctx.allowMutations;
    const serverDeletedBlobs = ctx.deletedBlobs.data;

    const deletedBlobs: DeletedBlob[] = (serverDeletedBlobs ?? []).map((b) => ({
        name: b.name,
        deletedAt: b.deletedOn,
        daysRemaining: b.remainingDays,
        sizeBytes: null,
    }));
    const [filter, setFilter] = useState("");
    const [recovering, setRecovering] = useState<Set<string>>(new Set());
    const [recovered, setRecovered] = useState<Set<string>>(new Set());
    const [errorByBlob, setErrorByBlob] = useState<Record<string, string>>({});
    const [checkingCollision, setCheckingCollision] = useState<Set<string>>(
        new Set(),
    );
    const [confirmRecover, setConfirmRecover] = useState<{
        name: string;
        collision: boolean;
    } | null>(null);
    const undeleteBlob = useUndeleteBlob(accountId, container);

    const filtered = filter
        ? deletedBlobs.filter((b) =>
              b.name.toLowerCase().includes(filter.toLowerCase()),
          )
        : deletedBlobs;

    const handleRecover = async (name: string) => {
        setRecovering((prev) => new Set(prev).add(name));
        setErrorByBlob((prev) => ({ ...prev, [name]: "" }));
        try {
            await undeleteBlob.mutateAsync(name);
            setRecovered((prev) => new Set(prev).add(name));
        } catch (e) {
            setErrorByBlob((prev) => ({
                ...prev,
                [name]: e instanceof Error ? e.message : String(e),
            }));
        } finally {
            setRecovering((prev) => {
                const next = new Set(prev);
                next.delete(name);
                return next;
            });
        }
    };

    // Recover previously fired immediately with no confirmation and no explanation of what
    // happens if a live blob of the same name already exists — Azure's undelete behavior in
    // that case depends on the account, so the confirm states both possible outcomes rather
    // than guessing one.
    const handleRecoverClick = async (name: string) => {
        setCheckingCollision((prev) => new Set(prev).add(name));
        try {
            const collision = await ctx.checkBlobExists(name);
            setConfirmRecover({ name, collision });
        } finally {
            setCheckingCollision((prev) => {
                const next = new Set(prev);
                next.delete(name);
                return next;
            });
        }
    };

    const handleRecoverConfirm = () => {
        if (!confirmRecover) return;
        const { name } = confirmRecover;
        setConfirmRecover(null);
        void handleRecover(name);
    };

    if (!accountId || !container) {
        return (
            <div
                className="flex h-full items-center justify-center text-sm text-muted-foreground"
                data-testid="blob-recovery-empty"
            >
                Select a container to view deleted blobs
            </div>
        );
    }

    return (
        <div className="flex h-full flex-col" data-testid="blob-recovery-panel">
            <div className="border-b px-4 py-3">
                <h2 className="text-sm font-semibold">
                    Blob Recovery — {container}
                </h2>
                <p className="mt-1 text-xs text-muted-foreground">
                    Recover soft-deleted blobs within retention period
                </p>
            </div>

            <div className="border-b px-4 py-2">
                <div className="relative">
                    <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
                    <input
                        type="text"
                        value={filter}
                        onChange={(e) => setFilter(e.target.value)}
                        placeholder="Search deleted blobs..."
                        className="w-full rounded-md border bg-card py-1.5 pl-8 pr-3 text-xs"
                        data-testid="blob-recovery-filter"
                    />
                </div>
            </div>

            <div className="flex-1 overflow-auto">
                {filtered.length === 0 ? (
                    <div
                        className="flex h-full items-center justify-center text-sm text-muted-foreground"
                        data-testid="blob-recovery-no-results"
                    >
                        No deleted blobs found
                    </div>
                ) : (
                    <table
                        className="w-full text-sm"
                        data-testid="blob-recovery-table"
                    >
                        <thead className="border-b bg-muted/50 sticky top-0">
                            <tr>
                                <th className="px-3 py-2 text-left">
                                    Blob Name
                                </th>
                                <th className="px-3 py-2 text-left">Deleted</th>
                                <th className="px-3 py-2 text-left">
                                    Days Remaining
                                </th>
                                <th className="px-3 py-2 text-left">Size</th>
                                <th className="px-3 py-2 text-right">
                                    Actions
                                </th>
                            </tr>
                        </thead>
                        <tbody>
                            {filtered.map((blob) => {
                                const isRecovered = recovered.has(blob.name);
                                const isRecovering = recovering.has(blob.name);
                                const isCheckingCollision =
                                    checkingCollision.has(blob.name);
                                const error = errorByBlob[blob.name];
                                return (
                                    <tr
                                        key={blob.name}
                                        className="border-b last:border-0"
                                    >
                                        <td className="px-3 py-2 font-mono text-xs">
                                            {blob.name}
                                        </td>
                                        <td className="px-3 py-2 text-xs text-muted-foreground">
                                            {formatLocalDateTime(
                                                blob.deletedAt,
                                            )}
                                        </td>
                                        <td className="px-3 py-2 text-xs">
                                            <span
                                                className={
                                                    blob.daysRemaining <= 3
                                                        ? "text-destructive"
                                                        : "text-muted-foreground"
                                                }
                                            >
                                                {blob.daysRemaining}d
                                            </span>
                                        </td>
                                        <td className="px-3 py-2 text-xs text-muted-foreground">
                                            {formatBytes(blob.sizeBytes)}
                                        </td>
                                        <td className="px-3 py-2 text-right">
                                            {isRecovered ? (
                                                <span
                                                    className="text-xs text-success"
                                                    data-testid={`blob-recovered-${blob.name}`}
                                                >
                                                    Recovered
                                                </span>
                                            ) : (
                                                <span
                                                    title={
                                                        allowMutations
                                                            ? "Recover deleted blob"
                                                            : "Mutations are disabled for this storage account. Enable allowMutations in Settings."
                                                    }
                                                >
                                                    <button
                                                        onClick={() =>
                                                            handleRecoverClick(
                                                                blob.name,
                                                            )
                                                        }
                                                        disabled={
                                                            isRecovering ||
                                                            isCheckingCollision ||
                                                            !allowMutations
                                                        }
                                                        className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
                                                        data-testid={`blob-recover-btn-${blob.name}`}
                                                    >
                                                        <RotateCcw className="h-3 w-3" />
                                                        {isRecovering
                                                            ? "Recovering..."
                                                            : isCheckingCollision
                                                              ? "Checking..."
                                                              : "Recover"}
                                                    </button>
                                                    {error && (
                                                        <span className="block text-xs text-destructive">
                                                            {error}
                                                        </span>
                                                    )}
                                                </span>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                )}
            </div>

            {confirmRecover && (
                <ConfirmBar
                    message={
                        confirmRecover.collision
                            ? `Restore "${confirmRecover.name}" to ${container}? A live blob with this name already exists — depending on the account's configuration, recovering may overwrite it or fail.`
                            : `Restore "${confirmRecover.name}" to ${container}?`
                    }
                    confirmLabel="Recover"
                    onConfirm={handleRecoverConfirm}
                    onCancel={() => setConfirmRecover(null)}
                    testId="blob-recover-confirm"
                    confirmTestId="blob-recover-confirm-yes"
                    cancelTestId="blob-recover-confirm-cancel"
                />
            )}
        </div>
    );
}
