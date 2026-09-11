import { Fragment, memo, type JSX } from "react";
import { tokenizeLogLine } from "@/lib/logHighlight";

/**
 * Renders one log line with token-level syntax highlighting.
 *
 * Memoized per line: `PodLogView` re-renders on every 10 fps buffer flush, but a
 * given line's text never changes once received, so tokenizing happens once per
 * line rather than once per frame. Plain runs render as bare text so the common
 * case adds no element at all.
 */
export const LogLineText = memo(function LogLineText({ line }: { line: string }): JSX.Element {
  const tokens = tokenizeLogLine(line);
  return (
    <>
      {tokens.map((token, i) =>
        token.cls === "plain" ? (
          <Fragment key={i}>{token.text}</Fragment>
        ) : (
          <span key={i} className={`log-tok-${token.cls}`}>
            {token.text}
          </span>
        ),
      )}
    </>
  );
});
