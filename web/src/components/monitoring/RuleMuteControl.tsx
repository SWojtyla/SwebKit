import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { BellOff } from "lucide-react";
import {
    MUTE_DURATIONS,
    isRuleMuted,
    muteBadgeLabel,
    muteUntilFor,
} from "./silenceWindows";

/**
 * Per-rule mute control (monitoring-closed-loop item 3): a bell-off button that opens a small
 * menu of durations — 1 hour, until tomorrow, or indefinitely — plus Unmute when the rule is
 * already muted. Shared by `AlertRuleRow` and `AlertHistoryPanel`'s snooze action so both offer
 * the same real mute instead of the old session-only row hide.
 *
 * The menu renders through a portal at the trigger's position (same approach as
 * `aks/ContextMenu`) because both hosts live inside `overflow-auto` containers — an absolutely
 * positioned inline menu would clip at the scroll edge.
 */
export function RuleMuteControl({
    ruleId,
    mutedUntil,
    onMute,
    testIdPrefix,
    title = "Mute this rule",
}: {
    ruleId: string;
    /** Current `rule.mutedUntil`; when omitted (e.g. history rows without the rule list) the
     * menu skips the Unmute entry. */
    mutedUntil?: string | null;
    onMute: (ruleId: string, until: string | null) => void;
    /** The toggle button's testid; the menu is `${testIdPrefix}-menu`, items
     * `${testIdPrefix}-{1h|tomorrow|indefinite|unmute}`. */
    testIdPrefix: string;
    title?: string;
}) {
    const [open, setOpen] = useState(false);
    // Anchor rect captured at open time — reading buttonRef.current during render trips
    // react-hooks/refs, and the rect is genuinely a snapshot (the menu is fixed-positioned
    // and closes on scroll, so it never needs to track a moving anchor).
    const [anchorRect, setAnchorRect] = useState<DOMRect | null>(null);
    const buttonRef = useRef<HTMLButtonElement>(null);
    const menuRef = useRef<HTMLDivElement>(null);
    const muted = isRuleMuted(mutedUntil);

    useEffect(() => {
        if (!open) return;
        const handleClickOutside = (e: MouseEvent) => {
            if (
                menuRef.current?.contains(e.target as Node) ||
                buttonRef.current?.contains(e.target as Node)
            )
                return;
            setOpen(false);
        };
        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === "Escape") setOpen(false);
        };
        const handleScroll = () => setOpen(false);
        document.addEventListener("mousedown", handleClickOutside);
        document.addEventListener("keydown", handleEscape);
        // Defer the scroll listener by a task — a scroll already queued when the menu opens
        // (e.g. scrollIntoView) would otherwise close it instantly.
        const scrollTimer = window.setTimeout(() => {
            document.addEventListener("scroll", handleScroll, true);
        }, 0);
        return () => {
            window.clearTimeout(scrollTimer);
            document.removeEventListener("mousedown", handleClickOutside);
            document.removeEventListener("keydown", handleEscape);
            document.removeEventListener("scroll", handleScroll, true);
        };
    }, [open]);

    const pick = (until: string | null) => {
        onMute(ruleId, until);
        setOpen(false);
    };

    const rect = open ? anchorRect : null;
    const menuX = Math.min(
        (rect?.right ?? 0) - 200,
        window.innerWidth - 220,
    );
    const menuY = Math.min(
        (rect?.bottom ?? 0) + 4,
        window.innerHeight - (MUTE_DURATIONS.length + (muted ? 1 : 0)) * 32 - 20,
    );

    return (
        <>
            <button
                ref={buttonRef}
                onClick={() => {
                    if (!open)
                        setAnchorRect(
                            buttonRef.current?.getBoundingClientRect() ?? null,
                        );
                    setOpen((v) => !v);
                }}
                className={`rounded p-1 hover:bg-accent ${muted ? "text-warning" : ""}`}
                title={muted ? muteBadgeLabel(mutedUntil) : title}
                data-testid={testIdPrefix}
                aria-haspopup="menu"
                aria-expanded={open}
            >
                <BellOff className="h-3.5 w-3.5" />
            </button>
            {open &&
                rect &&
                createPortal(
                    <div
                        ref={menuRef}
                        className="fixed z-50 min-w-[200px] rounded-md border bg-popover py-1 shadow-lg"
                        style={{ left: menuX, top: menuY }}
                        role="menu"
                        data-testid={`${testIdPrefix}-menu`}
                    >
                        {muted && (
                            <>
                                <button
                                    role="menuitem"
                                    onClick={() => pick(null)}
                                    className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent"
                                    data-testid={`${testIdPrefix}-unmute`}
                                >
                                    Unmute
                                </button>
                                <div className="my-1 border-t" />
                            </>
                        )}
                        {MUTE_DURATIONS.map((d) => (
                            <button
                                key={d.id}
                                role="menuitem"
                                onClick={() => pick(muteUntilFor(d.id))}
                                className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent"
                                data-testid={`${testIdPrefix}-${d.id}`}
                            >
                                {d.label}
                            </button>
                        ))}
                    </div>,
                    document.body,
                )}
        </>
    );
}
