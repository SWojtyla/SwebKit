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

import type { ApiRequestMethod, AuthConfig, HttpRequestEntry, ResponseHeaderDto } from "./types";
import { substituteVariables } from "./variable-utils";

/**
 * Headers whose value is masked until the user asks to see it. Everything else in an
 * echoed request is shown verbatim — the point of the panel is to be reproducible.
 */
const SENSITIVE_HEADERS = ["authorization", "proxy-authorization", "cookie", "set-cookie"];

function isSensitiveHeader(name: string): boolean {
  const lower = name.toLowerCase();
  return (
    SENSITIVE_HEADERS.includes(lower) ||
    lower.includes("api-key") ||
    lower.includes("apikey") ||
    lower.includes("token") ||
    lower.includes("secret")
  );
}

function shellQuote(value: string): string {
  return value.replace(/'/g, "'\\''");
}

/** Quoted `-H` argument. Single-quoted like the body — a value containing `"`, `$` or a
 *  backtick inside double quotes is not merely mis-rendered, the pasted command can
 *  run different text than the panel displayed. */
function headerArg(name: string, value: string): string {
  return `-H '${shellQuote(`${name}: ${value}`)}'`;
}

/**
 * The HTTP transport method the backend actually sends. `GraphQl` and `WebSocket` are
 * editor concepts — `MapMethod` in HttpRequestExecutor maps them to POST and GET, and a
 * `-X GRAPHQL` flag would produce a command that does not reproduce the request.
 */
export function transportMethod(method: ApiRequestMethod): string {
  switch (method) {
    case "GraphQl":
      return "POST";
    case "WebSocket":
      return "GET";
    default:
      return method.toUpperCase();
  }
}

function defaultContentType(mode: HttpRequestEntry["body"]["mode"]): string {
  if (mode === "Json") return "application/json";
  if (mode === "Xml") return "application/xml";
  return "text/plain";
}

// Same masking literal used everywhere else a secret is displayed without being revealed
// (RequestEditor.tsx, variableHighlight.ts, SecretDetailPanel.tsx, ContainerDetailPanel.tsx).
const MASK = "••••••••";

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Masks one query parameter's value in a URL. The resolved URL echoes back what the
 * server received — for API-key-in-query auth that is the real key, and this panel is
 * a copy-to-clipboard surface, so the value is masked exactly like a sensitive header.
 */
function maskQueryParamValue(url: string, paramName: string): string {
  if (!paramName) return url;
  const re = new RegExp(`([?&]${escapeRegExp(encodeURIComponent(paramName))}=)[^&#]*`);
  return url.replace(re, `$1${MASK}`);
}

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
function buildAuthParts(auth: AuthConfig | null, scope: Record<string, string | null>): AuthParts {
  if (!auth || auth.type === "None") return { parts: [], urlQuerySuffix: "" };

  // The non-secret auth fields are substituted at send time like any other part of the
  // request, so a header name or username written as `{{KEY_HEADER}}` must not be shown
  // here as its token — that is the same half-resolved command this module exists to avoid.
  const basicUsername = substituteVariables(auth.basicUsername ?? "", scope);
  const apiKeyParamName = substituteVariables(auth.apiKeyParamName ?? "", scope);

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
      return { parts: [`-H 'Authorization: Bearer ${MASK}'`], urlQuerySuffix: "" };

    case "Basic":
      return { parts: [`-u '${shellQuote(`${basicUsername}:${MASK}`)}'`], urlQuerySuffix: "" };

    case "ApiKey": {
      if (!apiKeyParamName) return { parts: [], urlQuerySuffix: "" };
      if (auth.apiKeyLocation === "Header") {
        return { parts: [`-H '${shellQuote(`${apiKeyParamName}: ${MASK}`)}'`], urlQuerySuffix: "" };
      }
      return {
        parts: [],
        urlQuerySuffix: `${encodeURIComponent(apiKeyParamName)}=${MASK}`,
      };
    }

    default:
      return { parts: [], urlQuerySuffix: "" };
  }
}

/**
 * @param resolvedUrl The URL as executed, from `ApiClientExecutionResponse.resolvedUrl`.
 *   Taken from the response rather than rebuilt here because the backend also folds
 *   in enabled query parameters and auth's query-string additions.
 * @param sentBody The body as sent, from `ApiClientExecutionResponse.sentBody` — for
 *   GraphQL this is the serialized `{query, variables}` payload, not the editor fields.
 */
export function buildCurl(
  request: HttpRequestEntry,
  resolvedUrl: string,
  scope: Record<string, string | null> = {},
  sentHeaders: ResponseHeaderDto[] | null = null,
  revealSecrets = false,
  sentBody: string | null = null,
): string {
  const parts = [`curl -X ${transportMethod(request.method)}`];

  // When the sidecar echoed the headers it actually put on the wire, render those and
  // nothing else. Reconstructing them here could only ever approximate the real request —
  // it emitted the body mode's Content-Type alongside any the user had set, and it could
  // not see auth resolved through the folder/collection chain at all.
  if (sentHeaders && sentHeaders.length > 0) {
    for (const h of sentHeaders) {
      const value = revealSecrets || !isSensitiveHeader(h.name) ? h.value : MASK;
      parts.push(headerArg(h.name, value));
    }

    if (sentBody) {
      parts.push(`-d '${shellQuote(sentBody)}'`);
    }

    // The echoed URL is post-auth, so an API key applied as a query param is already in
    // it — mask that value the same way a sensitive header is masked.
    const url =
      !revealSecrets &&
      request.auth?.type === "ApiKey" &&
      request.auth.apiKeyLocation !== "Header" &&
      request.auth.apiKeyParamName
        ? maskQueryParamValue(resolvedUrl, substituteVariables(request.auth.apiKeyParamName, scope))
        : resolvedUrl;
    parts.push(`'${shellQuote(url)}'`);
    return parts.join(" \\\n  ");
  }

  for (const h of request.headers) {
    if (h.isEnabled && h.key) {
      parts.push(headerArg(h.key, substituteVariables(h.value ?? "", scope)));
    }
  }

  const { mode, rawContent, contentType } = request.body;
  if (mode === "Json" || mode === "Xml" || mode === "Text") {
    parts.push(headerArg("Content-Type", contentType ?? defaultContentType(mode)));
    if (rawContent) {
      parts.push(`-d '${shellQuote(substituteVariables(rawContent, scope))}'`);
    }
  }

  const { parts: authParts, urlQuerySuffix, leadingComment } = buildAuthParts(request.auth, scope);
  parts.push(...authParts);

  const url = urlQuerySuffix
    ? `${resolvedUrl}${resolvedUrl.includes("?") ? "&" : "?"}${urlQuerySuffix}`
    : resolvedUrl;
  parts.push(`'${shellQuote(url)}'`);
  const command = parts.join(" \\\n  ");
  return leadingComment ? `${leadingComment}\n${command}` : command;
}
