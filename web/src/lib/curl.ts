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

import type { AuthConfig, HttpRequestEntry } from "./types";
import { substituteVariables } from "./variable-utils";

function shellQuote(value: string): string {
  return value.replace(/'/g, "'\\''");
}

function defaultContentType(mode: HttpRequestEntry["body"]["mode"]): string {
  if (mode === "Json") return "application/json";
  if (mode === "Xml") return "application/xml";
  return "text/plain";
}

// Same masking literal used everywhere else a secret is displayed without being revealed
// (RequestEditor.tsx, variableHighlight.ts, SecretDetailPanel.tsx, ContainerDetailPanel.tsx).
const MASK = "••••••••";

interface AuthParts {
  /** `-H`/`-u` flags, joined into the `\`-continued command like every other part. */
  parts: string[];
  /** Appended to the URL for an API key sent as a query param (curl has no flag for that). */
  urlQuerySuffix: string;
  /**
   * A standalone note rendered on its own line *before* the command, never inside the
   * `\`-continued chain — a `#` comment there would swallow the trailing `\` as part of the
   * comment text, silently breaking the continuation and orphaning every line after it.
   */
  leadingComment?: string;
}

/**
 * Auth lines to add to the curl command, mirroring exactly what `SidecarAuthHeaderBuilder.cs`
 * actually sends on the real request — masked, never the real secret, since this panel's whole
 * purpose is copy-to-clipboard.
 */
function buildAuthParts(auth: AuthConfig | null): AuthParts {
  if (!auth || auth.type === "None") return { parts: [], urlQuerySuffix: "" };

  switch (auth.type) {
    case "Inherited":
      // Resolving the actual request → folder → collection chain is backend-only
      // (IAuthInheritanceResolver) today; showing nothing here would be honest but indistinguishable
      // from "no auth is applied", which is exactly the confusion this exists to prevent.
      return {
        parts: [],
        urlQuerySuffix: "",
        leadingComment: "# Auth is inherited from a parent folder/collection — not resolved in this preview",
      };

    case "BearerToken":
    case "OAuth2":
      // OAuth2 client-credentials resolves its token via a separate call at send time, but a
      // Bearer header is genuinely what reaches the target — showing it masked confirms that.
      return { parts: [`-H "Authorization: Bearer ${MASK}"`], urlQuerySuffix: "" };

    case "Basic":
      return { parts: [`-u "${auth.basicUsername ?? ""}:${MASK}"`], urlQuerySuffix: "" };

    case "ApiKey": {
      if (!auth.apiKeyParamName) return { parts: [], urlQuerySuffix: "" };
      if (auth.apiKeyLocation === "Header") {
        return { parts: [`-H "${auth.apiKeyParamName}: ${MASK}"`], urlQuerySuffix: "" };
      }
      return {
        parts: [],
        urlQuerySuffix: `${encodeURIComponent(auth.apiKeyParamName)}=${MASK}`,
      };
    }

    default:
      return { parts: [], urlQuerySuffix: "" };
  }
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

  const { parts: authParts, urlQuerySuffix, leadingComment } = buildAuthParts(request.auth);
  parts.push(...authParts);

  const url = urlQuerySuffix
    ? `${resolvedUrl}${resolvedUrl.includes("?") ? "&" : "?"}${urlQuerySuffix}`
    : resolvedUrl;
  parts.push(`"${url}"`);
  const command = parts.join(" \\\n  ");
  return leadingComment ? `${leadingComment}\n${command}` : command;
}
