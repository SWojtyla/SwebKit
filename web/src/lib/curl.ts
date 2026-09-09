/// Renders a sent request as a runnable `curl` command.
///
/// Extracted from `ResponseViewer` so it can be unit-tested — and because it was
/// wrong in a way that only a test makes obvious. It used to take the URL from the
/// executed response (already substituted, server-side) but the body and headers
/// straight off the request entry (still full of `{{tokens}}`), so the panel showed
/// a command that was half-resolved and would not reproduce the request if pasted
/// into a shell.
///
/// Substitution happens here, client-side, against the same scope the editor
/// previews. Deliberately not resolved by the backend: Key Vault and Windows
/// credential-store values would then be expanded into a panel whose entire purpose
/// is copy-to-clipboard. Those stay `{{TOKEN}}`, which is the honest rendering —
/// `resolveEnvironmentVariable` leaves them `null` in the UI scope for the same
/// reason.

import type { HttpRequestEntry } from "./types";
import { substituteVariables } from "./variable-utils";

function shellQuote(value: string): string {
  return value.replace(/'/g, "'\\''");
}

function defaultContentType(mode: HttpRequestEntry["body"]["mode"]): string {
  if (mode === "Json") return "application/json";
  if (mode === "Xml") return "application/xml";
  return "text/plain";
}

/**
 * @param resolvedUrl The URL as executed, from `ApiClientExecutionResponse.resolvedUrl`.
 *   Taken from the response rather than rebuilt here because the backend also folds
 *   in enabled query parameters.
 */
export function buildCurl(
  request: HttpRequestEntry,
  resolvedUrl: string,
  scope: Record<string, string | null> = {},
): string {
  const parts = [`curl -X ${request.method.toUpperCase()}`];

  for (const h of request.headers) {
    if (h.isEnabled && h.key) {
      parts.push(`-H "${h.key}: ${substituteVariables(h.value ?? "", scope)}"`);
    }
  }

  const { mode, rawContent, contentType } = request.body;
  if (mode === "Json" || mode === "Xml" || mode === "Text") {
    parts.push(`-H "Content-Type: ${contentType ?? defaultContentType(mode)}"`);
    if (rawContent) {
      parts.push(`-d '${shellQuote(substituteVariables(rawContent, scope))}'`);
    }
  }

  parts.push(`"${resolvedUrl}"`);
  return parts.join(" \\\n  ");
}
