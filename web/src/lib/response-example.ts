/// Builds a persistable `ResponseExample` from a live response.
///
/// Examples are written into `collections.json` and, for repository-backed
/// collections, can end up committed to Git — so anything credential-shaped must
/// be scrubbed before it is stored, mirroring the documented Blazor behaviour.

import type { ApiClientExecutionResponse, ResponseExample } from "@/lib/types";
import { isLikelySecret } from "@/lib/variable-utils";

/** Header names never persisted, regardless of value. */
const BLOCKED_HEADERS = new Set([
  "authorization",
  "proxy-authorization",
  "set-cookie",
  "cookie",
  "www-authenticate",
  "x-api-key",
]);

const REDACTED = "«redacted»";

/**
 * Drops or masks credential-bearing headers.
 *
 * Blocked headers are removed outright; headers whose *name* looks secret are
 * kept with a redacted value so the example still records that the header was
 * present.
 */
export function scrubHeaders(
  headers: ApiClientExecutionResponse["headers"],
): ResponseExample["headers"] {
  return headers
    .filter((h) => !BLOCKED_HEADERS.has(h.name.toLowerCase()))
    .map((h) => ({
      key: h.name,
      value: isLikelySecret(h.name) ? REDACTED : h.value,
      isEnabled: true,
    }));
}

export function buildResponseExample(
  id: string,
  name: string,
  response: ApiClientExecutionResponse,
  capturedAt: string,
  environmentName: string | null = null,
): ResponseExample {
  return {
    id,
    name,
    statusCode: response.statusCode,
    statusText: response.statusText,
    contentType: response.contentType,
    body: response.responseBody,
    headers: scrubHeaders(response.headers),
    capturedAt,
    environmentName,
  };
}
