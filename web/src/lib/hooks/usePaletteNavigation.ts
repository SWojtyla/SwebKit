import { useEffect, useRef, useState } from "react";

/** Shared list-navigation state for command-palette-style pickers: tracks the
 * highlighted row, resets it whenever `resetKey` changes (pass the search query, or
 * anything else that reshuffles the visible list), and keeps the highlighted row
 * scrolled into view. Enter/Escape/Tab semantics differ per palette (drill-down vs.
 * direct navigate), so those stay in each component's own onKeyDown — this only owns
 * up/down movement. */
export function usePaletteNavigation(itemCount: number, resetKey: unknown) {
  const [selectedIndex, setSelectedIndex] = useState(0);
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setSelectedIndex(0);
    // itemCount intentionally excluded: this should reset on the *cause* of a list
    // change (e.g. query text), not merely because the count happens to differ.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resetKey]);

  useEffect(() => {
    const container = scrollRef.current;
    if (!container) return;
    const child = container.children[selectedIndex] as HTMLElement | undefined;
    child?.scrollIntoView({ block: "nearest" });
  }, [selectedIndex]);

  const moveDown = () => setSelectedIndex((i) => Math.min(i + 1, itemCount - 1));
  const moveUp = () => setSelectedIndex((i) => Math.max(i - 1, 0));

  return { selectedIndex, setSelectedIndex, scrollRef, moveDown, moveUp };
}

/** Shared open-lifecycle: focuses the search input shortly after the palette opens
 * (letting the mount/animation settle first) and runs `onOpen` to reset per-palette
 * state (query, MRU, drill-down level, ...). */
export function usePaletteFocusOnOpen(open: boolean, onOpen: () => void) {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (open) {
      onOpen();
      const t = setTimeout(() => inputRef.current?.focus(), 50);
      return () => clearTimeout(t);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  return inputRef;
}
