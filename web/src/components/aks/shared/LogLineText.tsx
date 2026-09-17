import { Fragment, memo, type JSX, type ReactNode } from "react";
import { tokenizeLogLine } from "@/lib/logHighlight";

function highlightedText(text: string, term: string, keyPrefix: string): ReactNode[] {
  const needle = term.trim().toLowerCase();
  if (!needle) return [text];
  const result: ReactNode[] = [];
  const lower = text.toLowerCase();
  let from = 0;
  let match = lower.indexOf(needle);
  let index = 0;
  while (match >= 0) {
    if (match > from) result.push(text.slice(from, match));
    result.push(
      <mark key={`${keyPrefix}-${index++}`} className="rounded-sm bg-warning/40 text-inherit">
        {text.slice(match, match + needle.length)}
      </mark>,
    );
    from = match + needle.length;
    match = lower.indexOf(needle, from);
  }
  if (from < text.length) result.push(text.slice(from));
  return result;
}

export const LogLineText = memo(function LogLineText({
  line,
  highlightTerm = "",
}: {
  line: string;
  highlightTerm?: string;
}): JSX.Element {
  const tokens = tokenizeLogLine(line);
  return (
    <>
      {tokens.map((token, i) =>
        token.cls === "plain" ? (
          <Fragment key={i}>{highlightedText(token.text, highlightTerm, String(i))}</Fragment>
        ) : (
          <span key={i} className={`log-tok-${token.cls}`}>
            {highlightedText(token.text, highlightTerm, String(i))}
          </span>
        ),
      )}
    </>
  );
});
