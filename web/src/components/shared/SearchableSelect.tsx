import { useEffect, useMemo, useRef, useState } from "react";
import { Check, Loader2 } from "lucide-react";

export interface SearchableSelectItem {
  /** The value reported to `onChange` and matched against `value`. */
  value: string;
  label: string;
  /** Small secondary line under the label (cluster name, FQDN, server…). */
  subtitle?: string;
}

interface SearchableSelectProps<T extends SearchableSelectItem = SearchableSelectItem> {
  items: T[];
  value: string | null;
  onChange: (item: T) => void;
  placeholder?: string;
  filterPlaceholder?: string;
  isLoading?: boolean;
  /** What the button shows while `isLoading` — otherwise a generic "Loading…". */
  loadingLabel?: string;
  /** Rendered as the button's tooltip while the control is disabled. */
  disabledReason?: string;
  /** Selected item floats to the top of the list (default true). */
  currentFirst?: boolean;
  /** Extra ordering applied after the current-first float (e.g. MRU ranking).
   *  Re-evaluated on every open, so it can read freshly-persisted prefs. */
  sortItems?: (items: T[]) => T[];
  /** testid prefix for options: `${testId}-option-${value}`. */
  testId: string;
  /** Trigger button testid (default `${testId}-button`). */
  buttonTestId?: string;
  /** Filter input testid (default `${testId}-filter`). */
  filterTestId?: string;
  /**
   * Keeps an `sr-only` native `<select>` wired to `onChange` so Playwright
   * `selectOption()` keeps working (same convention as `NamespaceSelector`).
   * Rendered with this exact testid so existing specs don't move.
   */
  nativeSelectTestId?: string;
  /** Extra <option> rows for the native select (e.g. an empty "Select…" row). */
  nativeExtraOptions?: { value: string; label: string }[];
  listAriaLabel: string;
  /** Tailwind classes for the trigger button — pages pass their own min-width. */
  buttonClassName?: string;
  /** Popover width class (default "w-80"). */
  popoverClassName?: string;
}

/**
 * Shared single-select dropdown: trigger button + filterable popover list with
 * full keyboard support (ArrowUp/Down highlight, Enter picks, Escape closes),
 * outside-click close, and focus return to the trigger. Extracted from
 * `ContextSelector`; adopted by the other resource selectors so the app has one
 * dropdown behavior instead of five near-identical ones.
 */
export function SearchableSelect<T extends SearchableSelectItem = SearchableSelectItem>({
  items,
  value,
  onChange,
  placeholder = "Select...",
  filterPlaceholder = "Filter...",
  isLoading,
  loadingLabel,
  disabledReason,
  currentFirst = true,
  sortItems,
  testId,
  buttonTestId,
  filterTestId,
  nativeSelectTestId,
  nativeExtraOptions,
  listAriaLabel,
  buttonClassName = "min-w-[12rem]",
  popoverClassName = "w-80",
}: SearchableSelectProps<T>) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [highlight, setHighlight] = useState(0);
  const ref = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    function onDocClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, []);

  const filtered = useMemo(
    () =>
      items.filter(
        (item) =>
          item.label.toLowerCase().includes(search.toLowerCase()) ||
          (item.subtitle ?? "").toLowerCase().includes(search.toLowerCase()),
      ),
    [items, search],
  );

  const sortedFiltered = useMemo(() => {
    const sorted = [...filtered].sort((a, b) => {
      if (currentFirst) {
        if (a.value === value && b.value !== value) return -1;
        if (b.value === value && a.value !== value) return 1;
      }
      return a.label.localeCompare(b.label);
    });
    // sortItems runs after the current-first float so domain ordering (MRU…)
    // refines within the non-current tail.
    return sortItems ? sortItems(sorted) : sorted;
    // `open` is a dep so a fresh open re-reads persisted ordering prefs.
  }, [filtered, value, currentFirst, sortItems, open]);

  useEffect(() => {
    setHighlight(0);
  }, [search, open]);

  const close = () => {
    setOpen(false);
    setSearch("");
    buttonRef.current?.focus();
  };

  const pick = (item: T) => {
    onChange(item);
    close();
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (!open) return;
    if (e.key === "Escape") {
      e.preventDefault();
      close();
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      setHighlight((h) => Math.min(h + 1, sortedFiltered.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (e.key === "Enter" && sortedFiltered[highlight]) {
      e.preventDefault();
      pick(sortedFiltered[highlight]);
    }
  };

  const current = items.find((i) => i.value === value);
  const display = isLoading
    ? (loadingLabel ?? "Loading…")
    : current?.label || value || placeholder;
  const disabled = isLoading || !!disabledReason;

  return (
    <div ref={ref} className="relative" onKeyDown={onKeyDown}>
      {/*
        sr-only (not h-0/w-0) keeps a bounding box so Playwright selectOption()
        stays actionable — see NamespaceSelector for the original note.
      */}
      {nativeSelectTestId && (
        <select
          data-testid={nativeSelectTestId}
          value={value ?? ""}
          onChange={(e) => {
            const item = items.find((i) => i.value === e.target.value)
              ?? nativeExtraOptions?.find((o) => o.value === e.target.value);
            if (item) onChange(item as T);
          }}
          className="sr-only"
          aria-hidden="true"
          tabIndex={-1}
        >
          {nativeExtraOptions?.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
          {items.map((i) => (
            <option key={i.value} value={i.value}>
              {i.label}
            </option>
          ))}
        </select>
      )}

      <button
        ref={buttonRef}
        type="button"
        onClick={() => !disabled && setOpen((v) => !v)}
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        title={disabledReason ?? display}
        className={`flex items-center justify-between rounded-md border bg-card px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-70 ${buttonClassName}`}
        data-testid={buttonTestId ?? `${testId}-button`}
      >
        <span className="flex min-w-0 items-center gap-1.5">
          {isLoading && <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" />}
          <span className="truncate">{display}</span>
        </span>
        <span className="text-muted-foreground">{open ? "▲" : "▼"}</span>
      </button>

      {open && (
        <div
          className={`absolute z-50 mt-1 rounded-md border bg-popover shadow-md ${popoverClassName}`}
          role="listbox"
          aria-label={listAriaLabel}
        >
          <div className="border-b p-2">
            <input
              autoFocus
              role="combobox"
              aria-expanded="true"
              aria-activedescendant={`${testId}-option-${highlight}`}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={filterPlaceholder}
              className="w-full rounded border bg-background px-2 py-1 text-xs"
              data-testid={filterTestId ?? `${testId}-filter`}
            />
          </div>
          <div className="max-h-60 overflow-auto p-1">
            {sortedFiltered.length === 0 && (
              <div className="px-2 py-2 text-xs text-muted-foreground">No matches found</div>
            )}
            {sortedFiltered.map((item, i) => {
              const isCurrent = item.value === value;
              return (
                <button
                  key={item.value}
                  id={`${testId}-option-${i}`}
                  type="button"
                  role="option"
                  aria-selected={isCurrent}
                  onClick={() => pick(item)}
                  onMouseEnter={() => setHighlight(i)}
                  className={`w-full rounded px-2 py-1.5 text-left text-sm hover:bg-accent ${isCurrent ? "bg-accent/50 font-medium" : ""} ${i === highlight ? "bg-accent/40" : ""}`}
                  data-testid={`${testId}-option-${item.value}`}
                >
                  <div className="flex items-center gap-2">
                    {isCurrent ? <Check className="h-3.5 w-3.5 text-primary" /> : <span className="h-3.5 w-3.5" />}
                    <div className="min-w-0 flex-1">
                      <div className="truncate">{item.label}</div>
                      {item.subtitle && (
                        <div className="truncate text-xs text-muted-foreground">{item.subtitle}</div>
                      )}
                    </div>
                  </div>
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
