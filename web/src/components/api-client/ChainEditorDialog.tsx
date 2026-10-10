import { useMemo, useState } from "react";
import {
    AlertTriangle,
    Check,
    GripVertical,
    Loader2,
    Search,
    Trash2,
    X,
} from "lucide-react";
import { Dialog } from "@/components/shared/Dialog";
import { MethodBadge } from "./method-badge";
import { useApiChain } from "@/lib/hooks";
import {
    collectChainCandidates,
    filterChainCandidates,
    hasChainStep,
    moveChainStep,
    newChainStep,
    removeChainStep,
    resolveChainSteps,
    setChainStepEnabled,
    type ChainRequestCandidate,
    type ResolvedChainStep,
} from "@/lib/api-chain-utils";
import type {
    ApiChain,
    ApiChainStep,
    ApiChainUpsert,
    ApiCollection,
} from "@/lib/types";

interface ChainEditorDialogProps {
    /** null → creating a new chain; an id → the chain is fetched for editing. */
    chainId: string | null;
    collections: ApiCollection[];
    /** Persists the draft (POST when `chainId` is null, PUT otherwise);
     *  resolves true on success — the dialog stays open on failure. */
    onSave: (
        chainId: string | null,
        draft: ApiChainUpsert,
    ) => Promise<boolean>;
    onClose: () => void;
}

/**
 * Chain editor — name + description, an ordered step list with HTML5 drag
 * reorder (same drag/keyboard contract as the collection tree), per-step
 * enabled toggle and remove, and an "Add request" picker spanning every loaded
 * collection (internal, linked roots, demo). Steps whose request no longer
 * resolves render marked "missing" — they are kept on save unless removed,
 * because the stored chain must round-trip verbatim.
 */
export function ChainEditorDialog({
    chainId,
    collections,
    onSave,
    onClose,
}: ChainEditorDialogProps) {
    const chainQuery = useApiChain(chainId);

    const title = chainId ? "Edit chain" : "New chain";

    return (
        <Dialog
            onClose={onClose}
            label={title}
            testId="chain-editor-dialog"
            widthClassName="w-[560px]"
        >
            <div className="flex items-center justify-between border-b px-4 py-3">
                <h2 className="text-sm font-semibold">{title}</h2>
                <button
                    onClick={onClose}
                    className="text-muted-foreground hover:text-foreground"
                    data-testid="chain-editor-close"
                    aria-label="Close chain editor"
                >
                    <X className="h-4 w-4" />
                </button>
            </div>
            {chainId !== null && chainQuery.isPending && (
                <div
                    className="flex items-center gap-2 p-6 text-sm text-muted-foreground"
                    data-testid="chain-editor-loading"
                >
                    <Loader2 className="h-4 w-4 animate-spin" /> Loading chain…
                </div>
            )}
            {chainId !== null && chainQuery.isError && (
                <div
                    className="p-6 text-sm text-destructive"
                    data-testid="chain-editor-error"
                >
                    Couldn't load the chain:{" "}
                    {chainQuery.error instanceof Error
                        ? chainQuery.error.message
                        : "unknown error"}
                </div>
            )}
            {(chainId === null || chainQuery.data) && (
                <ChainEditorForm
                    key={chainId ?? "new"}
                    chainId={chainId}
                    initial={chainQuery.data ?? null}
                    collections={collections}
                    onSave={onSave}
                    onClose={onClose}
                />
            )}
        </Dialog>
    );
}

interface ChainEditorFormProps {
    chainId: string | null;
    initial: ApiChain | null;
    collections: ApiCollection[];
    onSave: ChainEditorDialogProps["onSave"];
    onClose: () => void;
}

function ChainEditorForm({
    chainId,
    initial,
    collections,
    onSave,
    onClose,
}: ChainEditorFormProps) {
    const [name, setName] = useState(initial?.name ?? "");
    const [description, setDescription] = useState(initial?.description ?? "");
    const [steps, setSteps] = useState<ApiChainStep[]>(initial?.steps ?? []);
    const [pickerQuery, setPickerQuery] = useState("");
    const [saving, setSaving] = useState(false);

    // Same HTML5 drag contract as CollectionTree: whole row is the drag source,
    // grip as affordance, before/after indicators, Alt+Arrow for keyboard.
    const [dragIndex, setDragIndex] = useState<number | null>(null);
    const [dropTarget, setDropTarget] = useState<{
        index: number;
        placement: "before" | "after";
    } | null>(null);

    const resolved = useMemo(
        () => resolveChainSteps(steps, collections),
        [steps, collections],
    );

    const pickerGroups = useMemo(() => {
        const candidates = filterChainCandidates(
            collectChainCandidates(collections),
            pickerQuery,
        );
        const groups: {
            collectionId: string;
            collectionName: string;
            items: ChainRequestCandidate[];
        }[] = [];
        for (const c of candidates) {
            let group = groups.find((g) => g.collectionId === c.collectionId);
            if (!group) {
                group = {
                    collectionId: c.collectionId,
                    collectionName: c.collectionName,
                    items: [],
                };
                groups.push(group);
            }
            group.items.push(c);
        }
        return groups;
    }, [collections, pickerQuery]);

    const handleDrop = (targetIndex: number, placement: "before" | "after") => {
        if (dragIndex === null || dragIndex === targetIndex) return;
        let to = placement === "before" ? targetIndex : targetIndex + 1;
        // `to` is computed against the pre-removal list; removing the dragged
        // step shifts later indexes left by one.
        if (dragIndex < to) to -= 1;
        setSteps((prev) => moveChainStep(prev, dragIndex, to));
        setDragIndex(null);
        setDropTarget(null);
    };

    const handleSave = async () => {
        const trimmed = name.trim();
        if (!trimmed || saving) return;
        setSaving(true);
        const ok = await onSave(chainId, {
            name: trimmed,
            description: description.trim() || null,
            steps,
        });
        setSaving(false);
        if (ok) onClose();
    };

    return (
        <div className="flex max-h-[80vh] flex-col p-4">
            <div className="space-y-2">
                <div>
                    <label className="mb-1 block text-xs font-medium text-muted-foreground">
                        Name
                    </label>
                    <input
                        type="text"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        placeholder="Chain name"
                        autoFocus={!initial}
                        className="w-full rounded-md border bg-background px-3 py-1.5 text-sm"
                        data-testid="chain-name-input"
                    />
                </div>
                <div>
                    <label className="mb-1 block text-xs font-medium text-muted-foreground">
                        Description{" "}
                        <span className="font-normal">(optional)</span>
                    </label>
                    <input
                        type="text"
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        className="w-full rounded-md border bg-background px-3 py-1.5 text-sm"
                        data-testid="chain-description-input"
                    />
                </div>
            </div>

            <div className="mt-3 flex items-center justify-between">
                <span className="text-xs font-medium text-muted-foreground">
                    Steps ({steps.length}) — drag or Alt+↑/↓ to reorder
                </span>
            </div>
            <div
                className="mt-1 max-h-48 min-h-16 overflow-y-auto rounded border"
                data-testid="chain-steps-list"
                role="list"
            >
                {resolved.length === 0 && (
                    <div className="p-3 text-xs text-muted-foreground">
                        No steps yet — pick requests below.
                    </div>
                )}
                {resolved.map((r, i) => (
                    <ChainStepRow
                        key={r.step.id}
                        resolved={r}
                        index={i}
                        isDragging={dragIndex === i}
                        dropPlacement={
                            dropTarget?.index === i
                                ? dropTarget.placement
                                : null
                        }
                        onDragStart={() => setDragIndex(i)}
                        onDragOver={(e) => {
                            e.preventDefault();
                            if (dragIndex === null || dragIndex === i) {
                                setDropTarget(null);
                                return;
                            }
                            const rect =
                                e.currentTarget.getBoundingClientRect();
                            setDropTarget({
                                index: i,
                                placement:
                                    e.clientY < rect.top + rect.height / 2
                                        ? "before"
                                        : "after",
                            });
                        }}
                        onDrop={(e) => {
                            e.preventDefault();
                            if (dropTarget?.index === i) {
                                handleDrop(i, dropTarget.placement);
                            }
                        }}
                        onDragEnd={() => {
                            setDragIndex(null);
                            setDropTarget(null);
                        }}
                        onKeyboardMove={(direction) => {
                            setSteps((prev) =>
                                moveChainStep(
                                    prev,
                                    i,
                                    direction === "up" ? i - 1 : i + 1,
                                ),
                            );
                        }}
                        onToggle={(enabled) =>
                            setSteps((prev) =>
                                setChainStepEnabled(prev, r.step.id, enabled),
                            )
                        }
                        onRemove={() =>
                            setSteps((prev) =>
                                removeChainStep(prev, r.step.id),
                            )
                        }
                    />
                ))}
            </div>

            <div className="mt-3 flex min-h-0 flex-1 flex-col">
                <div className="flex items-center gap-1.5 rounded-t border bg-muted/30 px-2 py-1.5">
                    <Search className="h-3 w-3 shrink-0 text-muted-foreground" />
                    <input
                        type="text"
                        value={pickerQuery}
                        onChange={(e) => setPickerQuery(e.target.value)}
                        placeholder="Add request — filter by name, collection, method…"
                        className="flex-1 bg-transparent text-xs outline-none placeholder:text-muted-foreground"
                        data-testid="chain-picker-search"
                    />
                </div>
                <div
                    className="max-h-44 overflow-y-auto rounded-b border border-t-0"
                    data-testid="chain-picker"
                >
                    {pickerGroups.length === 0 && (
                        <div className="p-3 text-xs text-muted-foreground">
                            No matching requests.
                        </div>
                    )}
                    {pickerGroups.map((group) => (
                        <div key={group.collectionId}>
                            <div
                                className="sticky top-0 bg-muted/80 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground"
                                data-testid={`chain-picker-group-${group.collectionId}`}
                            >
                                {group.collectionName}
                            </div>
                            {group.items.map((c) => {
                                const already = hasChainStep(steps, c);
                                return (
                                    <button
                                        key={c.nodeId}
                                        type="button"
                                        disabled={already}
                                        onClick={() =>
                                            setSteps((prev) => [
                                                ...prev,
                                                newChainStep(c),
                                            ])
                                        }
                                        className="flex w-full items-center gap-2 px-2 py-1 text-left text-sm hover:bg-accent disabled:opacity-50"
                                        title={
                                            already
                                                ? "Already a step in this chain"
                                                : `Add "${c.name}" to the chain`
                                        }
                                        data-testid={`chain-picker-option-${c.nodeId}`}
                                    >
                                        <MethodBadge
                                            method={c.method}
                                            variant="text"
                                            className="w-10 text-right"
                                        />
                                        <span className="min-w-0 flex-1 truncate">
                                            {c.name}
                                        </span>
                                        {already && (
                                            <Check className="h-3 w-3 shrink-0 text-success" />
                                        )}
                                    </button>
                                );
                            })}
                        </div>
                    ))}
                </div>
            </div>

            <div className="mt-4 flex justify-end gap-2">
                <button
                    onClick={onClose}
                    className="rounded-md border px-3 py-1.5 text-xs hover:bg-accent"
                    data-testid="chain-editor-cancel"
                >
                    Cancel
                </button>
                <button
                    onClick={() => void handleSave()}
                    disabled={!name.trim() || saving}
                    title={!name.trim() ? "Enter a name first" : undefined}
                    className="rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                    data-testid="chain-save-button"
                >
                    {saving ? "Saving…" : chainId ? "Save" : "Create"}
                </button>
            </div>
        </div>
    );
}

interface ChainStepRowProps {
    resolved: ResolvedChainStep;
    index: number;
    isDragging: boolean;
    dropPlacement: "before" | "after" | null;
    onDragStart: () => void;
    onDragOver: (e: React.DragEvent<HTMLDivElement>) => void;
    onDrop: (e: React.DragEvent<HTMLDivElement>) => void;
    onDragEnd: () => void;
    onKeyboardMove: (direction: "up" | "down") => void;
    onToggle: (enabled: boolean) => void;
    onRemove: () => void;
}

function ChainStepRow({
    resolved,
    index,
    isDragging,
    dropPlacement,
    onDragStart,
    onDragOver,
    onDrop,
    onDragEnd,
    onKeyboardMove,
    onToggle,
    onRemove,
}: ChainStepRowProps) {
    const { step, collection, node, missing } = resolved;
    return (
        <div
            role="listitem"
            tabIndex={0}
            draggable
            data-testid={`chain-step-${step.id}`}
            data-missing={missing || undefined}
            className={`flex items-center gap-1.5 border-b border-border/50 px-2 py-1 text-sm last:border-b-0 outline-none focus-visible:bg-accent ${
                isDragging ? "tree-dragging" : ""
            } ${
                dropPlacement === "before"
                    ? "tree-drag-over-before"
                    : dropPlacement === "after"
                      ? "tree-drag-over-after"
                      : ""
            } ${!step.enabled ? "opacity-60" : ""}`}
            // Chromium does not focus draggable elements on mousedown — focus
            // explicitly or Alt+Arrow reordering silently breaks (same pitfall
            // the collection tree hit).
            onClick={(e) => e.currentTarget.focus()}
            onKeyDown={(e) => {
                if (!e.altKey) return;
                if (e.key === "ArrowUp") {
                    e.preventDefault();
                    onKeyboardMove("up");
                } else if (e.key === "ArrowDown") {
                    e.preventDefault();
                    onKeyboardMove("down");
                }
            }}
            onDragStart={(e) => {
                e.dataTransfer.effectAllowed = "move";
                e.dataTransfer.setData(
                    "application/json+swebkit-chain-step",
                    step.id,
                );
                onDragStart();
            }}
            onDragOver={onDragOver}
            onDrop={onDrop}
            onDragEnd={onDragEnd}
        >
            <GripVertical
                className="h-3 w-3 shrink-0 cursor-grab text-muted-foreground"
                aria-label="Drag to reorder"
            />
            <span className="w-5 shrink-0 text-right text-xs text-muted-foreground">
                {index + 1}
            </span>
            <input
                type="checkbox"
                checked={step.enabled}
                onChange={(e) => onToggle(e.target.checked)}
                onClick={(e) => e.stopPropagation()}
                title={step.enabled ? "Step runs" : "Step skipped at run time"}
                className="shrink-0"
                data-testid={`chain-step-enabled-${step.id}`}
            />
            {missing ? (
                <span
                    className="flex min-w-0 flex-1 items-center gap-1.5 text-xs"
                    style={{ color: "var(--warning)" }}
                    data-testid={`chain-step-missing-${step.id}`}
                >
                    <AlertTriangle className="h-3 w-3 shrink-0" />
                    <span className="truncate">
                        Missing request{" "}
                        <span className="font-mono opacity-70">
                            ({step.requestId})
                        </span>
                    </span>
                </span>
            ) : (
                <>
                    <MethodBadge
                        method={node!.request!.method}
                        variant="text"
                        className="w-10 text-right"
                    />
                    <span className="min-w-0 flex-1 truncate">{node!.name}</span>
                </>
            )}
            <span
                className="shrink-0 rounded border bg-muted/40 px-1.5 py-0 text-[10px] text-muted-foreground"
                title={
                    collection
                        ? `Collection: ${collection.name}`
                        : "Collection not found"
                }
                data-testid={`chain-step-collection-${step.id}`}
            >
                {collection?.name ?? "?"}
            </span>
            <button
                type="button"
                onClick={(e) => {
                    e.stopPropagation();
                    onRemove();
                }}
                className="shrink-0 rounded p-0.5 text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
                title="Remove step"
                aria-label="Remove step"
                data-testid={`chain-step-remove-${step.id}`}
            >
                <Trash2 className="h-3 w-3" />
            </button>
        </div>
    );
}
