import { useEffect, useState } from "react";
import {
    Download,
    Link2,
    MoreVertical,
    Pencil,
    Play,
    Plus,
    SquarePen,
    Trash2,
    Workflow,
} from "lucide-react";
import type { ApiChainSummary } from "@/lib/types";

interface ChainListSectionProps {
    chains: ApiChainSummary[];
    /** "+" — opens the chain editor in create mode. */
    onNewChain: () => void;
    /** Row click / menu "Edit" — opens the chain editor. */
    onEditChain: (chainId: string) => void;
    onRunChain: (chainId: string) => void;
    onRenameChain: (chain: ApiChainSummary) => void;
    onExportChain: (chainId: string) => void;
    onDeleteChain: (chain: ApiChainSummary) => void;
}

interface ChainMenuState {
    chain: ApiChainSummary;
    x: number;
    y: number;
}

/**
 * The "Chains" sidebar section below the collection tree — persisted, named,
 * cross-collection request sequences. Row click opens the editor; the per-row
 * menu mirrors the tree's fixed-position context menu (Run / Edit / Rename /
 * Export / Delete).
 */
export function ChainListSection({
    chains,
    onNewChain,
    onEditChain,
    onRunChain,
    onRenameChain,
    onExportChain,
    onDeleteChain,
}: ChainListSectionProps) {
    const [menu, setMenu] = useState<ChainMenuState | null>(null);

    // Same close-on-outside-interaction contract as the tree's context menu.
    useEffect(() => {
        if (!menu) return;
        const close = () => setMenu(null);
        document.addEventListener("click", close);
        document.addEventListener("contextmenu", close);
        return () => {
            document.removeEventListener("click", close);
            document.removeEventListener("contextmenu", close);
        };
    }, [menu]);

    const openMenu = (e: React.MouseEvent, chain: ApiChainSummary) => {
        e.preventDefault();
        e.stopPropagation();
        setMenu({ chain, x: e.clientX, y: e.clientY });
    };

    return (
        <div
            className="flex max-h-[45%] shrink-0 flex-col border-t"
            data-testid="chain-section"
        >
            <div className="flex items-center justify-between px-2 py-1.5">
                <span className="flex items-center gap-1.5 text-xs font-semibold text-muted-foreground">
                    <Workflow className="h-3.5 w-3.5" /> Chains
                </span>
                <button
                    onClick={onNewChain}
                    className="rounded p-0.5 hover:bg-accent"
                    title="New chain"
                    aria-label="New chain"
                    data-testid="add-chain-button"
                >
                    <Plus className="h-3.5 w-3.5" />
                </button>
            </div>
            <div className="min-h-0 overflow-y-auto px-1 pb-1">
                {chains.length === 0 && (
                    <div className="px-2 py-1 text-xs text-muted-foreground">
                        No chains yet — right-click a request or click +.
                    </div>
                )}
                {chains.map((chain) => (
                    <div
                        key={chain.id}
                        role="button"
                        tabIndex={0}
                        title={chain.description ?? chain.name}
                        data-testid={`chain-row-${chain.id}`}
                        className="group flex cursor-pointer items-center gap-1.5 rounded px-2 py-1 text-sm hover:bg-accent"
                        onClick={() => onEditChain(chain.id)}
                        onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") {
                                e.preventDefault();
                                onEditChain(chain.id);
                            }
                        }}
                        onContextMenu={(e) => openMenu(e, chain)}
                    >
                        <Link2 className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                        <span className="min-w-0 flex-1 truncate">
                            {chain.name}
                        </span>
                        <span
                            className="shrink-0 rounded-full bg-muted px-1.5 text-[10px] leading-4 text-muted-foreground"
                            title={`${chain.stepCount} step${chain.stepCount === 1 ? "" : "s"}`}
                            data-testid={`chain-stepcount-${chain.id}`}
                        >
                            {chain.stepCount}
                        </span>
                        <button
                            className="shrink-0 rounded p-0.5 opacity-0 hover:bg-accent-foreground/10 group-hover:opacity-100 focus-visible:opacity-100"
                            aria-label={`Chain menu for ${chain.name}`}
                            data-testid={`chain-menu-${chain.id}`}
                            onClick={(e) => openMenu(e, chain)}
                        >
                            <MoreVertical className="h-3 w-3" />
                        </button>
                    </div>
                ))}
            </div>

            {menu && (
                <div
                    className="fixed z-50 min-w-[160px] rounded-md border bg-popover py-1 shadow-lg"
                    style={{
                        left: Math.min(menu.x, window.innerWidth - 180),
                        top: Math.min(menu.y, window.innerHeight - 240),
                    }}
                    data-testid="chain-context-menu"
                    onClick={(e) => e.stopPropagation()}
                >
                    <button
                        className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
                        onClick={() => {
                            onRunChain(menu.chain.id);
                            setMenu(null);
                        }}
                        data-testid="chain-ctx-run"
                    >
                        <Play className="h-3.5 w-3.5" /> Run
                    </button>
                    <button
                        className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
                        onClick={() => {
                            onEditChain(menu.chain.id);
                            setMenu(null);
                        }}
                        data-testid="chain-ctx-edit"
                    >
                        <SquarePen className="h-3.5 w-3.5" /> Edit
                    </button>
                    <button
                        className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
                        onClick={() => {
                            onRenameChain(menu.chain);
                            setMenu(null);
                        }}
                        data-testid="chain-ctx-rename"
                    >
                        <Pencil className="h-3.5 w-3.5" /> Rename
                    </button>
                    <button
                        className="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent"
                        onClick={() => {
                            onExportChain(menu.chain.id);
                            setMenu(null);
                        }}
                        data-testid="chain-ctx-export"
                    >
                        <Download className="h-3.5 w-3.5" /> Export
                    </button>
                    <button
                        className="flex w-full items-center gap-2 px-3 py-1.5 text-sm text-destructive hover:bg-destructive/10"
                        onClick={() => {
                            onDeleteChain(menu.chain);
                            setMenu(null);
                        }}
                        data-testid="chain-ctx-delete"
                    >
                        <Trash2 className="h-3.5 w-3.5" /> Delete
                    </button>
                </div>
            )}
        </div>
    );
}
