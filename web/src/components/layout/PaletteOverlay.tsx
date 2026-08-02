import type { KeyboardEvent, MouseEvent, ReactNode } from "react";

interface PaletteOverlayProps {
  overlayTestId: string;
  dialogTestId?: string;
  ariaLabel: string;
  dialogClassName: string;
  paddingTopClassName?: string;
  onClose: () => void;
  onOverlayKeyDown?: (e: KeyboardEvent) => void;
  children: ReactNode;
}

/** Shared backdrop + centered dialog shell for command-palette-style pickers, so a
 * new domain-scoped palette doesn't have to re-derive the overlay/click-outside/
 * role="dialog" markup from scratch. Content (search input, results list, footer
 * hints) stays fully owned by the caller since that's where palettes actually differ. */
export function PaletteOverlay({
  overlayTestId,
  dialogTestId,
  ariaLabel,
  dialogClassName,
  paddingTopClassName = "pt-24",
  onClose,
  onOverlayKeyDown,
  children,
}: PaletteOverlayProps) {
  const stopPropagation = (e: MouseEvent) => e.stopPropagation();

  return (
    <div
      className={`fixed inset-0 z-50 flex items-start justify-center bg-black/50 ${paddingTopClassName}`}
      onClick={onClose}
      onKeyDown={onOverlayKeyDown}
      tabIndex={-1}
      data-testid={overlayTestId}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel}
        className={dialogClassName}
        onClick={stopPropagation}
        data-testid={dialogTestId}
      >
        {children}
      </div>
    </div>
  );
}
