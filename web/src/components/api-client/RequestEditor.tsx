import { useState, useEffect } from "react";
import { Save, Send, Wand2, Minimize2 } from "lucide-react";
import type { HttpRequestEntry, ApiRequestMethod, RequestBodyMode, AuthType, AuthConfig } from "@/lib/types";

interface RequestEditorProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
  onSend: () => void;
  onSave: () => void;
  sending: boolean;
}

const methods: ApiRequestMethod[] = [
  "Get", "Post", "Put", "Patch", "Delete", "Head", "Options",
];

const bodyModes: RequestBodyMode[] = ["None", "Json", "Xml", "Text", "FormData"];

const authTypes: { value: AuthType; label: string }[] = [
  { value: "None", label: "None" },
  { value: "Inherited", label: "Inherited" },
  { value: "BearerToken", label: "Bearer Token" },
  { value: "Basic", label: "Basic" },
  { value: "ApiKey", label: "API Key" },
  { value: "OAuth2", label: "OAuth 2.0" },
];

const methodColors: Record<string, string> = {
  Get: "text-blue-500",
  Post: "text-green-500",
  Put: "text-yellow-500",
  Patch: "text-orange-500",
  Delete: "text-red-500",
  Head: "text-purple-500",
  Options: "text-gray-500",
};

type Tab = "params" | "headers" | "body" | "auth";

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

function tryPrettyPrintJson(content: string): string {
  try {
    return JSON.stringify(JSON.parse(content), null, 2);
  } catch {
    return content;
  }
}

function tryMinifyJson(content: string): string {
  try {
    return JSON.stringify(JSON.parse(content));
  } catch {
    return content;
  }
}

export function RequestEditor({ request, onChange, onSend, onSave, sending }: RequestEditorProps) {
  const [activeTab, setActiveTab] = useState<Tab>("params");
  const [dirty, setDirty] = useState(false);

  // Track dirty state by comparing to a saved snapshot
  useEffect(() => {
    setDirty(true);
  }, [request]);

  // Keyboard shortcuts
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "s") {
        e.preventDefault();
        onSave();
        setDirty(false);
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
        e.preventDefault();
        onSend();
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onSave, onSend]);

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
    onChange({ ...request, headers: request.headers.filter((_, i) => i !== index) });

  const addQueryParam = () =>
    onChange({
      ...request,
      queryParams: [...request.queryParams, { key: "", value: "", isEnabled: true }],
    });
  const removeQueryParam = (index: number) =>
    onChange({ ...request, queryParams: request.queryParams.filter((_, i) => i !== index) });

  const setAuthType = (type: AuthType) =>
    onChange({ ...request, auth: { ...(request.auth ?? {}), type } as AuthConfig });
  const updateAuth = (patch: Partial<AuthConfig>) =>
    onChange({ ...request, auth: { ...(request.auth ?? { type: "None" }), ...patch } as AuthConfig });

  const auth = request.auth ?? { type: "None" };

  const prettyPrint = () => {
    if (request.body.rawContent) {
      setBodyContent(tryPrettyPrintJson(request.body.rawContent));
    }
  };

  const minify = () => {
    if (request.body.rawContent) {
      setBodyContent(tryMinifyJson(request.body.rawContent));
    }
  };

  return (
    <div className="flex h-full flex-col border-r bg-card" data-testid="request-editor">
      {/* URL bar */}
      <div className="flex items-center gap-2 border-b p-3">
        <select
          data-testid="request-method-select"
          value={request.method}
          onChange={(e) => setMethod(e.target.value as ApiRequestMethod)}
          className={`rounded border bg-background px-2 py-1.5 text-sm font-semibold ${methodColors[request.method] ?? ""}`}
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
          onClick={() => { onSave(); setDirty(false); }}
        >
          <Save className="h-4 w-4" />
          {dirty ? "Save*" : "Save"}
        </button>
      </div>

      {/* Request name */}
      <div className="border-b px-3 py-1.5">
        <input
          type="text"
          data-testid="request-name-input"
          value={request.name}
          onChange={(e) => onChange({ ...request, name: e.target.value })}
          className="w-full rounded border bg-background px-2 py-1 text-sm"
          placeholder="Request name"
        />
      </div>

      {/* Tabs */}
      <div className="flex border-b">
        {(["params", "headers", "body", "auth"] as Tab[]).map((tab) => (
          <button
            key={tab}
            data-testid={`request-tab-${tab}`}
            className={`px-4 py-2 text-sm font-medium capitalize ${
              activeTab === tab ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
            }`}
            onClick={() => setActiveTab(tab)}
          >
            {tab === "params" ? "Params" : tab === "headers" ? "Headers" : tab === "body" ? "Body" : "Auth"}
            {tab === "params" && request.queryParams.length > 0 && (
              <span className="ml-1 text-xs text-muted-foreground">({request.queryParams.length})</span>
            )}
            {tab === "headers" && request.headers.length > 0 && (
              <span className="ml-1 text-xs text-muted-foreground">({request.headers.length})</span>
            )}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-auto p-4">
        {/* Params tab */}
        {activeTab === "params" && (
          <div data-testid="params-tab">
            {request.queryParams.map((param, i) => (
              <div key={i} className="mb-1 flex items-center gap-2" data-testid={`query-param-row-${i}`}>
                <input
                  type="checkbox"
                  checked={param.isEnabled}
                  onChange={(e) => onChange(updateQueryParams(request, i, { isEnabled: e.target.checked }))}
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
                <button className="text-xs text-destructive" onClick={() => removeQueryParam(i)}>
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
        )}

        {/* Headers tab */}
        {activeTab === "headers" && (
          <div data-testid="headers-tab">
            {request.headers.map((header, i) => (
              <div key={i} className="mb-1 flex items-center gap-2" data-testid={`request-header-row-${i}`}>
                <input
                  type="checkbox"
                  checked={header.isEnabled}
                  onChange={(e) => onChange(updateHeaders(request, i, { isEnabled: e.target.checked }))}
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
                <button className="text-xs text-destructive" onClick={() => removeHeader(i)}>
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
        )}

        {/* Body tab */}
        {activeTab === "body" && (
          <div data-testid="body-tab">
            <div className="mb-2 flex items-center gap-2">
              <span className="text-sm font-medium">Body</span>
              <select
                data-testid="request-body-mode-select"
                value={request.body.mode}
                onChange={(e) => setBodyMode(e.target.value as RequestBodyMode)}
                className="rounded border bg-background px-2 py-1 text-xs"
              >
                {bodyModes.map((m) => (
                  <option key={m} value={m}>{m}</option>
                ))}
              </select>
              {request.body.mode === "Json" && request.body.rawContent && (
                <>
                  <button
                    onClick={prettyPrint}
                    title="Pretty print JSON"
                    className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                    data-testid="body-pretty-print"
                  >
                    <Wand2 className="h-3 w-3" /> Format
                  </button>
                  <button
                    onClick={minify}
                    title="Minify JSON"
                    className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                    data-testid="body-minify"
                  >
                    <Minimize2 className="h-3 w-3" /> Minify
                  </button>
                </>
              )}
            </div>
            {request.body.mode !== "None" && (
              <textarea
                data-testid="request-body-editor"
                value={request.body.rawContent ?? ""}
                onChange={(e) => setBodyContent(e.target.value)}
                placeholder='{"key":"value"}'
                className="h-48 w-full rounded border bg-background p-2 font-mono text-sm"
              />
            )}
          </div>
        )}

        {/* Auth tab */}
        {activeTab === "auth" && (
          <div data-testid="auth-tab">
            <select
              data-testid="auth-type-select"
              value={auth.type}
              onChange={(e) => setAuthType(e.target.value as AuthType)}
              className="mb-2 rounded border bg-background px-2 py-1 text-sm"
            >
              {authTypes.map((t) => (
                <option key={t.value} value={t.value}>{t.label}</option>
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
                  onChange={(e) => updateAuth({ apiKeyLocation: e.target.value as "Header" | "QueryParam" })}
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

            {auth.type === "OAuth2" && (
              <div className="space-y-2">
                <div>
                  <label className="mb-1 block text-xs font-medium text-muted-foreground">Grant Type</label>
                  <select
                    data-testid="auth-oauth2-grant"
                    value={auth.oAuth2GrantType}
                    onChange={(e) => updateAuth({ oAuth2GrantType: e.target.value as "ClientCredentials" | "AuthorizationCode" })}
                    className="w-full rounded border bg-background px-2 py-1 text-sm"
                  >
                    <option value="ClientCredentials">Client Credentials</option>
                    <option value="AuthorizationCode">Authorization Code</option>
                  </select>
                </div>
                <input
                  data-testid="auth-oauth2-client-id"
                  type="text"
                  value={auth.oAuth2ClientId ?? ""}
                  onChange={(e) => updateAuth({ oAuth2ClientId: e.target.value })}
                  placeholder="Client ID"
                  className="w-full rounded border bg-background px-2 py-1 text-sm"
                />
                <input
                  data-testid="auth-oauth2-token-url"
                  type="text"
                  value={auth.oAuth2TokenUrl ?? ""}
                  onChange={(e) => updateAuth({ oAuth2TokenUrl: e.target.value })}
                  placeholder="Token URL"
                  className="w-full rounded border bg-background px-2 py-1 text-sm"
                />
                {auth.oAuth2GrantType === "AuthorizationCode" && (
                  <input
                    data-testid="auth-oauth2-auth-url"
                    type="text"
                    value={auth.oAuth2AuthUrl ?? ""}
                    onChange={(e) => updateAuth({ oAuth2AuthUrl: e.target.value })}
                    placeholder="Authorization URL"
                    className="w-full rounded border bg-background px-2 py-1 text-sm"
                  />
                )}
                <input
                  data-testid="auth-oauth2-scopes"
                  type="text"
                  value={auth.oAuth2Scopes ?? ""}
                  onChange={(e) => updateAuth({ oAuth2Scopes: e.target.value })}
                  placeholder="Scopes (space-separated)"
                  className="w-full rounded border bg-background px-2 py-1 text-sm"
                />
                <input
                  data-testid="auth-oauth2-secret"
                  type="password"
                  value={auth.credentialKey ?? ""}
                  onChange={(e) => updateAuth({ credentialKey: e.target.value })}
                  placeholder="Client Secret"
                  className="w-full rounded border bg-background px-2 py-1 text-sm"
                />
              </div>
            )}

            {auth.type === "Inherited" && (
              <div className="text-xs text-muted-foreground">
                This request inherits authentication from its parent collection.
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
