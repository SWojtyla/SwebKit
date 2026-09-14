import { useCallback, useLayoutEffect, useRef, type JSX, type KeyboardEvent } from "react";
import { describeVariableToken, tokenizeVariables } from "@/lib/variableHighlight";

interface VariableInputProps {
  value: string;
  onChange: (value: string) => void;
  /** Merged collection + environment variables, from `buildVariableScope`. */
  scope: Record<string, string | null>;
  placeholder?: string;
  /** Border / background / radius, applied to the wrapper that frames the field. */
  chromeClassName?: string;
  /** Padding, font size and family. Applied to the input and the overlay alike. */
  metricsClassName?: string;
  /** Extra classes for the wrapper (sizing within its row). */
  wrapperClassName?: string;
  testId?: string;
  ariaLabel?: string;
  onKeyDown?: (e: KeyboardEvent<HTMLInputElement>) => void;
  onBlur?: () => void;
}

/**
 * A single-line text input that colours `{{variable}}` tokens by whether they
 * will resolve — green for a value the UI can preview, amber for one only known
 * at send time (generated / credential store / Key Vault), red for a name that is
 * not in scope at all. Hovering the field lists each token's resolution.
 *
 * Native inputs cannot style their own content, so this renders an `aria-hidden`
 * overlay holding the same string with per-token spans and makes the real input's
 * text transparent. The two layers must share exact text metrics or the caret
 * drifts from the glyphs, which is why `metricsClassName` goes to both while the
 * chrome lives on the wrapper — a `bg-*`/`border-*` class on the input itself
 * would either paint over the overlay or race the overlay's own utilities for
 * precedence. The overlay's horizontal scroll is mirrored from the input rather
 * than recomputed, so a URL longer than the field stays aligned while caret
 * movement scrolls it.
 */
export function VariableInput({
  value,
  onChange,
  scope,
  placeholder,
  chromeClassName = "rounded border bg-background",
  metricsClassName = "px-3 py-1.5 text-sm",
  wrapperClassName = "min-w-0 flex-1",
  testId,
  ariaLabel,
  onKeyDown,
  onBlur,
}: VariableInputProps): JSX.Element {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const overlayRef = useRef<HTMLDivElement | null>(null);

  const syncScroll = useCallback(() => {
    if (inputRef.current && overlayRef.current) {
      overlayRef.current.scrollLeft = inputRef.current.scrollLeft;
    }
  }, []);

  // Typing at the end of a long value scrolls the input after paint, so mirror in
  // a layout effect as well as on `scroll` — the latter does not fire for every
  // caret-driven adjustment.
  useLayoutEffect(syncScroll, [value, syncScroll]);

  const tokens = tokenizeVariables(value, scope);

  // Shared with the CodeMirror body editor so the same variable cannot be worded
  // one way in the URL bar and another in the body.
  const summary = tokens
    .map(describeVariableToken)
    .filter((line): line is string => line !== null);

  return (
    <div
      className={`relative ${wrapperClassName} ${chromeClassName} focus-within:border-primary`}
      data-testid={testId ? `${testId}-wrapper` : undefined}
    >
      <div
        ref={overlayRef}
        aria-hidden="true"
        className={`pointer-events-none absolute inset-0 overflow-hidden whitespace-pre text-foreground ${metricsClassName}`}
        data-testid={testId ? `${testId}-highlight` : undefined}
      >
        {tokens.map((token, i) =>
          token.kind === "text" ? (
            <span key={i}>{token.text}</span>
          ) : (
            <span key={i} className={`var-tok-${token.kind}`}>
              {token.text}
            </span>
          ),
        )}
      </div>
      <input
        ref={inputRef}
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onScroll={syncScroll}
        onKeyDown={onKeyDown}
        onBlur={onBlur}
        placeholder={placeholder}
        aria-label={ariaLabel}
        title={summary.length > 0 ? summary.join("\n") : undefined}
        spellCheck={false}
        data-testid={testId}
        // Transparent text rather than a hidden input: the native caret,
        // selection, autofill and IME behaviour all survive, and the overlay
        // supplies the glyphs. The placeholder still needs a visible colour.
        className={`relative w-full border-0 bg-transparent text-transparent caret-foreground outline-none placeholder:text-muted-foreground ${metricsClassName}`}
      />
    </div>
  );
}
