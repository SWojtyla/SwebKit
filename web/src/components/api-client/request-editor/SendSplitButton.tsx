import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, Send, Settings2 } from "lucide-react";
import type { ApiRunOptions } from "@/lib/api-run-utils";

interface SendSplitButtonProps {
    sending: boolean;
    /** Same enablement rule the plain Send button has always had. */
    urlEmpty: boolean;
    /** `request.dependsOnRequestIds?.length` — labels "Send with dependencies (N)". */
    depCount: number;
    onSend: () => void;
    onSendWithDeps: () => void;
    runOptions: ApiRunOptions;
    onRunOptionsChange: (next: ApiRunOptions) => void;
}

/**
 * Send as a split button: the primary half stays the single-request send
 * (`request-send-button`, unchanged); the caret opens a menu with
 * "Send with dependencies (N)" — enabled only when the request actually
 * declares prerequisites — and "Run options…", a small popover holding the
 * stop-on-error toggle (default on) and the inter-step delay.
 *
 * Menu + popover render through a portal at the caret's position (the
 * `aks/ContextMenu`/`RuleMuteControl` pattern): the toolbar lives in a wrapping
 * flex row, so an absolutely positioned inline menu would clip or push layout.
 */
export function SendSplitButton({
    sending,
    urlEmpty,
    depCount,
    onSend,
    onSendWithDeps,
    runOptions,
    onRunOptionsChange,
}: SendSplitButtonProps) {
    const [menuOpen, setMenuOpen] = useState(false);
    const [optionsOpen, setOptionsOpen] = useState(false);
    const [anchorRect, setAnchorRect] = useState<DOMRect | null>(null);
    const caretRef = useRef<HTMLButtonElement | null>(null);
    const menuRef = useRef<HTMLDivElement | null>(null);
    const optionsRef = useRef<HTMLDivElement | null>(null);

    const anyOpen = menuOpen || optionsOpen;

    const closeAll = () => {
        setMenuOpen(false);
        setOptionsOpen(false);
    };

    useEffect(() => {
        if (!anyOpen) return;
        const onDown = (e: MouseEvent) => {
            if (
                menuRef.current?.contains(e.target as Node) ||
                optionsRef.current?.contains(e.target as Node) ||
                caretRef.current?.contains(e.target as Node)
            )
                return;
            closeAll();
        };
        const onKey = (e: KeyboardEvent) => {
            if (e.key === "Escape") {
                e.stopPropagation();
                closeAll();
                caretRef.current?.focus();
            }
        };
        const onScroll = () => closeAll();
        document.addEventListener("mousedown", onDown);
        document.addEventListener("keydown", onKey);
        const scrollTimer = window.setTimeout(
            () => document.addEventListener("scroll", onScroll, true),
            0,
        );
        return () => {
            window.clearTimeout(scrollTimer);
            document.removeEventListener("mousedown", onDown);
            document.removeEventListener("keydown", onKey);
            document.removeEventListener("scroll", onScroll, true);
        };
    }, [anyOpen]);

    // Focus the first menu item on open; Arrow keys walk the items.
    useEffect(() => {
        if (!menuOpen) return;
        menuRef.current
            ?.querySelector<HTMLButtonElement>("[role=menuitem]")
            ?.focus();
    }, [menuOpen]);

    const onMenuKeyDown = (e: React.KeyboardEvent) => {
        if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
        e.preventDefault();
        const items = Array.from(
            menuRef.current?.querySelectorAll<HTMLButtonElement>(
                "[role=menuitem]:not(:disabled)",
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

    const toggleCaret = () => {
        if (!anyOpen)
            setAnchorRect(
                caretRef.current?.getBoundingClientRect() ?? null,
            );
        setOptionsOpen(false);
        setMenuOpen((v) => !v);
    };

    const rect = anchorRect;
    const menuX = Math.min((rect?.right ?? 0) - 220, window.innerWidth - 230);
    const menuY = Math.min((rect?.bottom ?? 0) + 4, window.innerHeight - 140);

    const disabled = sending || urlEmpty;

    return (
        <div className="flex shrink-0" data-testid="send-split-button">
            <button
                data-testid="request-send-button"
                className="flex items-center gap-1 rounded-l bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                onClick={onSend}
                disabled={disabled}
                title={
                    sending
                        ? "Sending…"
                        : urlEmpty
                          ? "Enter a URL first"
                          : undefined
                }
            >
                <Send className="h-4 w-4" />
                {sending ? "Sending..." : "Send"}
            </button>
            <button
                ref={caretRef}
                type="button"
                data-testid="send-menu-button"
                className="flex items-center rounded-r border-l border-primary-foreground/25 bg-primary px-1.5 py-1.5 text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
                onClick={toggleCaret}
                disabled={sending}
                title="More send options"
                aria-haspopup="menu"
                aria-expanded={anyOpen}
            >
                <ChevronDown className="h-3.5 w-3.5" />
            </button>

            {menuOpen &&
                rect &&
                createPortal(
                    <div
                        ref={menuRef}
                        role="menu"
                        data-testid="send-menu"
                        className="fixed z-50 min-w-[220px] rounded-md border bg-popover py-1 shadow-lg"
                        style={{ left: menuX, top: menuY }}
                        onKeyDown={onMenuKeyDown}
                    >
                        <button
                            type="button"
                            role="menuitem"
                            data-testid="send-with-deps"
                            disabled={depCount === 0}
                            title={
                                depCount === 0
                                    ? "Add prerequisites under “Runs after” first"
                                    : `Run ${depCount} prerequisite${depCount === 1 ? "" : "s"} first, then this request`
                            }
                            className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent focus:bg-accent focus:outline-none disabled:opacity-50"
                            onClick={() => {
                                closeAll();
                                onSendWithDeps();
                            }}
                        >
                            Send with dependencies ({depCount})
                        </button>
                        <div className="my-1 border-t" />
                        <button
                            type="button"
                            role="menuitem"
                            data-testid="send-run-options"
                            className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent focus:bg-accent focus:outline-none"
                            onClick={() => {
                                setMenuOpen(false);
                                setOptionsOpen(true);
                            }}
                        >
                            <Settings2 className="h-3.5 w-3.5" />
                            Run options…
                        </button>
                    </div>,
                    document.body,
                )}

            {optionsOpen &&
                rect &&
                createPortal(
                    <div
                        ref={optionsRef}
                        data-testid="run-options-popover"
                        className="fixed z-50 w-64 rounded-md border bg-popover p-3 shadow-lg"
                        style={{ left: menuX, top: menuY }}
                    >
                        <div className="mb-2 text-xs font-medium text-muted-foreground">
                            Applies to dependency and batch runs
                        </div>
                        <label className="flex items-center gap-2 text-sm">
                            <input
                                type="checkbox"
                                data-testid="run-opt-stop-on-error"
                                checked={runOptions.stopOnError}
                                onChange={(e) =>
                                    onRunOptionsChange({
                                        ...runOptions,
                                        stopOnError: e.target.checked,
                                    })
                                }
                            />
                            Stop on first error
                        </label>
                        <label className="mt-2 flex items-center gap-2 text-sm">
                            <span className="flex-1">Step delay (ms)</span>
                            <input
                                type="number"
                                data-testid="run-opt-delay-ms"
                                min={0}
                                max={10000}
                                step={100}
                                value={runOptions.delayMs}
                                onChange={(e) =>
                                    onRunOptionsChange({
                                        ...runOptions,
                                        delayMs: Math.max(
                                            0,
                                            Math.min(
                                                10000,
                                                Number(e.target.value) || 0,
                                            ),
                                        ),
                                    })
                                }
                                className="w-24 rounded border bg-background px-2 py-1 text-xs"
                            />
                        </label>
                    </div>,
                    document.body,
                )}
        </div>
    );
}
