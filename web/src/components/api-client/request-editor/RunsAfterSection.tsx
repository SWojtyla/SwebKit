import { useEffect, useRef, useState } from "react";
import { ListOrdered, Plus, X } from "lucide-react";
import type { ApiCollection } from "@/lib/types";
import {
    dependencyClosure,
    dependencyPickerCandidates,
    findRequestNodeAnyId,
    requestEntryId,
} from "@/lib/api-run-utils";

interface RunsAfterSectionProps {
    /** The collection the edited request lives in (same-collection deps only). */
    collection: ApiCollection | null | undefined;
    /** The request's tree node id (or entry id — lookups are dual-id). */
    requestNodeId: string;
    /** Current `request.dependsOnRequestIds` (already normalized to an array). */
    deps: string[];
    onChange: (deps: string[]) => void;
}

/** Display name for a dep id — resolved across node and entry ids, so chips
 *  still render for hand-edited files that referenced either id space. */
function depDisplayName(
    collection: ApiCollection | null | undefined,
    depId: string,
): { name: string; missing: boolean } {
    if (!collection) return { name: depId, missing: true };
    const node = findRequestNodeAnyId(collection.nodes, depId);
    if (node) return { name: node.name, missing: false };
    return { name: depId, missing: true };
}

/**
 * The "Runs after" strip: one chip per prerequisite (a same-collection request
 * node), an "Add prerequisite" picker that hides anything that would close a
 * dependency cycle, and ✕ removal. Edits land on `request.dependsOnRequestIds`
 * via `onChange`, so they persist through the normal draft→save path (internal
 * whole-store PUT or linked `.swebreq.json` write) untouched.
 */
export function RunsAfterSection({
    collection,
    requestNodeId,
    deps,
    onChange,
}: RunsAfterSectionProps) {
    const [pickerOpen, setPickerOpen] = useState(false);
    const pickerRef = useRef<HTMLDivElement | null>(null);
    const listRef = useRef<HTMLDivElement | null>(null);

    const candidates = collection
        ? dependencyPickerCandidates(collection, requestNodeId, deps)
        : [];

    // A cycle the picker could not prevent (imported/hand-edited data): one of
    // the current deps already reaches back to this request transitively.
    const hasCycle =
        collection != null &&
        deps.some((depId) =>
            dependencyClosure(collection, depId).has(requestNodeId),
        );

    useEffect(() => {
        if (!pickerOpen) return;
        const onDown = (e: MouseEvent) => {
            if (!pickerRef.current?.contains(e.target as Node))
                setPickerOpen(false);
        };
        const onKey = (e: KeyboardEvent) => {
            if (e.key === "Escape") {
                e.stopPropagation();
                setPickerOpen(false);
            }
        };
        document.addEventListener("mousedown", onDown);
        document.addEventListener("keydown", onKey);
        return () => {
            document.removeEventListener("mousedown", onDown);
            document.removeEventListener("keydown", onKey);
        };
    }, [pickerOpen]);

    // Focus the first option when the picker opens; Arrow keys walk the list.
    useEffect(() => {
        if (!pickerOpen) return;
        listRef.current
            ?.querySelector<HTMLButtonElement>("[data-dep-option]")
            ?.focus();
    }, [pickerOpen]);

    const onListKeyDown = (e: React.KeyboardEvent) => {
        if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
        e.preventDefault();
        const items = Array.from(
            listRef.current?.querySelectorAll<HTMLButtonElement>(
                "[data-dep-option]",
            ) ?? [],
        );
        if (items.length === 0) return;
        const index = items.indexOf(
            document.activeElement as HTMLButtonElement,
        );
        const next =
            e.key === "ArrowDown"
                ? (index + 1) % items.length
                : (index - 1 + items.length) % items.length;
        items[next]?.focus();
    };

    const addDep = (id: string) => {
        onChange([...deps, id]);
        setPickerOpen(false);
    };

    return (
        <div
            className="flex flex-wrap items-center gap-1.5 border-b px-3 py-1.5 text-xs"
            data-testid="runs-after-section"
        >
            <span
                className="flex shrink-0 items-center gap-1 text-muted-foreground"
                title="Requests that run before this one when sent with dependencies"
            >
                <ListOrdered className="h-3.5 w-3.5" />
                Runs after
            </span>
            {deps.map((depId) => {
                const { name, missing } = depDisplayName(collection, depId);
                return (
                    <span
                        key={depId}
                        data-testid={`dep-chip-${depId}`}
                        className={`flex items-center gap-1 rounded border px-1.5 py-0.5 ${
                            missing
                                ? "border-dashed text-muted-foreground"
                                : "bg-muted/40"
                        }`}
                        title={
                            missing
                                ? "This prerequisite no longer exists — a run with dependencies fails until it is removed"
                                : name
                        }
                    >
                        {name}
                        {missing && " (missing)"}
                        <button
                            type="button"
                            data-testid={`dep-remove-${depId}`}
                            aria-label={`Remove prerequisite ${name}`}
                            className="rounded p-0.5 hover:bg-accent"
                            onClick={() =>
                                onChange(deps.filter((d) => d !== depId))
                            }
                        >
                            <X className="h-3 w-3" />
                        </button>
                    </span>
                );
            })}
            <div className="relative" ref={pickerRef}>
                <button
                    type="button"
                    data-testid="dep-add-button"
                    className="flex items-center gap-1 rounded border border-dashed px-1.5 py-0.5 text-muted-foreground hover:bg-accent hover:text-foreground disabled:opacity-50"
                    onClick={() => setPickerOpen((v) => !v)}
                    aria-haspopup="menu"
                    aria-expanded={pickerOpen}
                    disabled={!collection}
                    title={
                        !collection
                            ? "Open a request inside a collection to add prerequisites"
                            : "Add a request that must run before this one"
                    }
                >
                    <Plus className="h-3 w-3" /> Add prerequisite
                </button>
                {pickerOpen && (
                    <div
                        ref={listRef}
                        role="menu"
                        data-testid="dep-picker"
                        className="absolute left-0 top-full z-50 mt-1 max-h-60 w-64 overflow-auto rounded-md border bg-popover py-1 shadow-lg"
                        onKeyDown={onListKeyDown}
                    >
                        {candidates.length === 0 && (
                            <div className="px-3 py-1.5 text-xs text-muted-foreground">
                                No available requests — every other request is
                                already a prerequisite or would create a cycle.
                            </div>
                        )}
                        {candidates.map((node) => (
                            <button
                                key={node.id}
                                type="button"
                                role="menuitem"
                                data-dep-option
                                data-testid={`dep-option-${node.id}`}
                                className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent focus:bg-accent focus:outline-none"
                                onClick={() => addDep(requestEntryId(node))}
                            >
                                <span className="truncate">{node.name}</span>
                            </button>
                        ))}
                    </div>
                )}
            </div>
            {hasCycle && (
                <span
                    className="text-xs"
                    style={{ color: "var(--destructive)" }}
                    data-testid="dep-cycle-warning"
                >
                    Dependency cycle detected — a run would be rejected.
                </span>
            )}
        </div>
    );
}
