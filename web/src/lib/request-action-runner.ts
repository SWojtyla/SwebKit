import type { RequestAction, HttpRequestEntry, ApiClientExecutionResponse } from "./types";
import { evaluateJsonPath } from "./api";
import { writeClipboard } from "./tauri-bridge";

export interface ActionRuntimeContext {
  request: HttpRequestEntry;
  response?: ApiClientExecutionResponse | null;
}

export function delay(ms: number): Promise<void> {
  const safe = Math.max(0, ms);
  return new Promise((resolve) => setTimeout(resolve, safe));
}

function headerValue(response: ApiClientExecutionResponse, name: string): string | null {
  const match = response.headers.find(
    (h) => h.name.localeCompare(name, undefined, { sensitivity: "base" }) === 0,
  );
  return match?.value ?? null;
}

async function evaluateJsonPathValue(body: string, path: string): Promise<string | null> {
  const trimmed = path.trim();
  if (!trimmed) return body;
  try {
    const result = await evaluateJsonPath(body, trimmed);
    if (result.error || result.value === null) return null;
    return result.value;
  } catch {
    return null;
  }
}

async function selectValue(action: RequestAction, ctx: ActionRuntimeContext): Promise<string | null> {
  const { request, response } = ctx;
  switch (action.source) {
    case "RequestUrl":
      return request.url ?? "";
    case "RequestMethod":
      return request.method;
    case "RequestBody": {
      const body = request.body.rawContent ?? "";
      return action.selector ? evaluateJsonPathValue(body, action.selector) : body;
    }
    case "ResponseStatusCode":
      return response ? String(response.statusCode) : null;
    case "ResponseStatusText":
      return response?.statusText ?? null;
    case "ResponseBody": {
      const body = response?.responseBody ?? "";
      return action.selector ? evaluateJsonPathValue(body, action.selector) : body;
    }
    case "ResponseHeader":
      return response && action.selector ? headerValue(response, action.selector) : null;
    default:
      return null;
  }
}

export async function runRequestActions(
  actions: RequestAction[],
  ctx: ActionRuntimeContext,
  onNotify: (type: "success" | "info", title: string, message: string) => void,
): Promise<void> {
  for (const action of actions) {
    if (!action.isEnabled) continue;
    try {
      if (action.kind === "Delay") {
        await delay(action.delayMs);
        continue;
      }

      if (action.kind === "CopyToClipboard") {
        const value = await selectValue(action, ctx);
        if (value === null || value === undefined) {
          onNotify("info", `${action.name}: nothing to copy`, `Source "${action.source}" produced no value.`);
          continue;
        }
        await writeClipboard(value);
        onNotify("success", "Copied", `${action.name} copied to clipboard.`);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      onNotify("info", `${action.name}: skipped`, message);
    }
  }
}
