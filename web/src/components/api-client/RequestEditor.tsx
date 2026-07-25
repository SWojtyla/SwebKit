import { Save, Send } from "lucide-react";
import type { HttpRequestEntry, ApiRequestMethod, RequestBodyMode, AuthType, AuthConfig } from "@/lib/types";

interface RequestEditorProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
  onSend: () => void;
  onSave: () => void;
  sending: boolean;
}

const methods: ApiRequestMethod[] = [
  "Get",
  "Post",
  "Put",
  "Patch",
  "Delete",
  "Head",
  "Options",
];

const bodyModes: RequestBodyMode[] = ["None", "Json", "Xml", "Text", "FormData"];

const authTypes: { value: AuthType; label: string }[] = [
  { value: "None", label: "None" },
  { value: "BearerToken", label: "Bearer Token" },
  { value: "Basic", label: "Basic" },
  { value: "ApiKey", label: "API Key" },
];

function updateHeaders(
  request: HttpRequestEntry,
  index: number,
  patch: Partial<{ key: string; value: string | null; isEnabled: boolean }>,
): HttpRequestEntry {
  const headers = request.headers.map((h, i) => (i === index ? { ...h, ...patch } : h));
  return { ...request, headers };
}

function updateQueryParams(
  request: HttpRequestEntry,
  index: number,
  patch: Partial<{ key: string; value: string | null; isEnabled: boolean }>,
): HttpRequestEntry {
  const queryParams = request.queryParams.map((p, i) => (i === index ? { ...p, ...patch } : p));
  return { ...request, queryParams };
}

export function RequestEditor({ request, onChange, onSend, onSave, sending }: RequestEditorProps) {
  const setMethod = (method: ApiRequestMethod) => onChange({ ...request, method });
  const setUrl = (url: string) => onChange({ ...request, url });

  const setBodyMode = (mode: RequestBodyMode) =>
    onChange({ ...request, body: { ...request.body, mode } });

  const setBodyContent = (rawContent: string) =>
    onChange({ ...request, body: { ...request.body, rawContent } });

  const addHeader = () =>
    onChange({
      ...request,
      headers: [...request.headers, { key: "", value: "", isEnabled: true }],
    });

  const removeHeader = (index: number) =>
    onChange({
      ...request,
      headers: request.headers.filter((_, i) => i !== index),
    });

  const addQueryParam = () =>
    onChange({
      ...request,
      queryParams: [...request.queryParams, { key: "", value: "", isEnabled: true }],
    });

  const removeQueryParam = (index: number) =>
    onChange({
      ...request,
      queryParams: request.queryParams.filter((_, i) => i !== index),
    });

  const setAuthType = (type: AuthType) =>
    onChange({
      ...request,
      auth: { ...(request.auth ?? {}), type } as AuthConfig,
    });

  const updateAuth = (patch: Partial<AuthConfig>) =>
    onChange({
      ...request,
      auth: { ...(request.auth ?? { type: "None" }), ...patch } as AuthConfig,
    });

  const auth = request.auth ?? { type: "None" };

  return (
    <div className="flex h-full flex-col border-r bg-card" data-testid="request-editor">
      <div className="flex items-center gap-2 border-b p-3">
        <select
          data-testid="request-method-select"
          value={request.method}
          onChange={(e) => setMethod(e.target.value as ApiRequestMethod)}
          className="rounded border bg-background px-2 py-1.5 text-sm font-semibold"
        >
          {methods.map((m) => (
            <option key={m} value={m}>
              {m.toUpperCase()}
            </option>
          ))}
        </select>
        <input
          data-testid="request-url-input"
          type="text"
          value={request.url}
          onChange={(e) => setUrl(e.target.value)}
          placeholder="https://api.example.com/resource"
          className="flex-1 rounded border bg-background px-3 py-1.5 text-sm"
        />
        <button
          data-testid="request-send-button"
          className="flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          onClick={onSend}
          disabled={sending || !request.url.trim()}
        >
          <Send className="h-4 w-4" />
          {sending ? "Sending..." : "Send"}
        </button>
        <button
          data-testid="request-save-button"
          className="flex items-center gap-1 rounded border px-3 py-1.5 text-sm font-medium hover:bg-accent"
          onClick={onSave}
        >
          <Save className="h-4 w-4" />
          Save
        </button>
      </div>

      <div className="flex-1 overflow-auto p-4">
        <div className="mb-4">
          <div className="mb-1 text-sm font-medium">Query Parameters</div>
          {request.queryParams.map((param, i) => (
            <div key={i} className="mb-1 flex items-center gap-2" data-testid={`query-param-row-${i}`}>
              <input
                type="checkbox"
                checked={param.isEnabled}
                onChange={(e) =>
                  onChange(updateQueryParams(request, i, { isEnabled: e.target.checked }))
                }
              />
              <input
                type="text"
                value={param.key}
                onChange={(e) => onChange(updateQueryParams(request, i, { key: e.target.value }))}
                placeholder="Key"
                className="w-32 rounded border bg-background px-2 py-1 text-sm"
              />
              <input
                type="text"
                value={param.value ?? ""}
                onChange={(e) => onChange(updateQueryParams(request, i, { value: e.target.value }))}
                placeholder="Value"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm"
              />
              <button
                className="text-xs text-destructive"
                onClick={() => removeQueryParam(i)}
              >
                Remove
              </button>
            </div>
          ))}
          <button
            data-testid="add-query-param-button"
            className="text-sm text-primary hover:underline"
            onClick={addQueryParam}
          >
            + Add parameter
          </button>
        </div>

        <div className="mb-4">
          <div className="mb-1 text-sm font-medium">Headers</div>
          {request.headers.map((header, i) => (
            <div key={i} className="mb-1 flex items-center gap-2" data-testid={`request-header-row-${i}`}>
              <input
                type="checkbox"
                checked={header.isEnabled}
                onChange={(e) =>
                  onChange(updateHeaders(request, i, { isEnabled: e.target.checked }))
                }
              />
              <input
                type="text"
                value={header.key}
                onChange={(e) => onChange(updateHeaders(request, i, { key: e.target.value }))}
                placeholder="Header"
                className="w-32 rounded border bg-background px-2 py-1 text-sm"
              />
              <input
                type="text"
                value={header.value ?? ""}
                onChange={(e) => onChange(updateHeaders(request, i, { value: e.target.value }))}
                placeholder="Value"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm"
              />
              <button
                className="text-xs text-destructive"
                onClick={() => removeHeader(i)}
              >
                Remove
              </button>
            </div>
          ))}
          <button
            data-testid="add-request-header-button"
            className="text-sm text-primary hover:underline"
            onClick={addHeader}
          >
            + Add header
          </button>
        </div>

        <div className="mb-4">
          <div className="mb-1 flex items-center gap-2 text-sm font-medium">
            <span>Body</span>
            <select
              data-testid="request-body-mode-select"
              value={request.body.mode}
              onChange={(e) => setBodyMode(e.target.value as RequestBodyMode)}
              className="rounded border bg-background px-2 py-1 text-xs"
            >
              {bodyModes.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </div>
          {request.body.mode !== "None" && (
            <textarea
              data-testid="request-body-editor"
              value={request.body.rawContent ?? ""}
              onChange={(e) => setBodyContent(e.target.value)}
              placeholder='{"key":"value"}'
              className="h-40 w-full rounded border bg-background p-2 font-mono text-sm"
            />
          )}
        </div>

        <div>
          <div className="mb-1 text-sm font-medium">Authentication</div>
          <select
            data-testid="auth-type-select"
            value={auth.type}
            onChange={(e) => setAuthType(e.target.value as AuthType)}
            className="mb-2 rounded border bg-background px-2 py-1 text-sm"
          >
            {authTypes.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>

          {auth.type === "BearerToken" && (
            <input
              data-testid="auth-bearer-input"
              type="password"
              value={auth.credentialKey ?? ""}
              onChange={(e) => updateAuth({ credentialKey: e.target.value })}
              placeholder="Bearer token"
              className="w-full rounded border bg-background px-2 py-1 text-sm"
            />
          )}

          {auth.type === "Basic" && (
            <div className="flex gap-2">
              <input
                data-testid="auth-basic-username"
                type="text"
                value={auth.basicUsername ?? ""}
                onChange={(e) => updateAuth({ basicUsername: e.target.value })}
                placeholder="Username"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm"
              />
              <input
                data-testid="auth-basic-password"
                type="password"
                value={auth.credentialKey ?? ""}
                onChange={(e) => updateAuth({ credentialKey: e.target.value })}
                placeholder="Password"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm"
              />
            </div>
          )}

          {auth.type === "ApiKey" && (
            <div className="flex gap-2">
              <input
                data-testid="auth-apikey-name"
                type="text"
                value={auth.apiKeyParamName ?? ""}
                onChange={(e) => updateAuth({ apiKeyParamName: e.target.value })}
                placeholder="Key name"
                className="w-32 rounded border bg-background px-2 py-1 text-sm"
              />
              <select
                data-testid="auth-apikey-location"
                value={auth.apiKeyLocation}
                onChange={(e) =>
                  updateAuth({ apiKeyLocation: e.target.value as "Header" | "QueryParam" })
                }
                className="rounded border bg-background px-2 py-1 text-sm"
              >
                <option value="Header">Header</option>
                <option value="QueryParam">Query</option>
              </select>
              <input
                data-testid="auth-apikey-value"
                type="password"
                value={auth.credentialKey ?? ""}
                onChange={(e) => updateAuth({ credentialKey: e.target.value })}
                placeholder="API key value"
                className="flex-1 rounded border bg-background px-2 py-1 text-sm"
              />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
