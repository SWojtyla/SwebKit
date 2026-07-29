import { useState, useEffect, useRef, useCallback } from "react";
import { Save, Send, Wand2, Minimize2, Eye, Crosshair } from "lucide-react";
import { EditorState, Compartment } from "@codemirror/state";
import { defaultKeymap, history, historyKeymap, indentWithTab } from "@codemirror/commands";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { defaultHighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { EditorView, keymap, lineNumbers } from "@codemirror/view";
import type { HttpRequestEntry, ApiRequestMethod, RequestBodyMode, AuthType, AuthConfig, CaptureRule, ApiEnvironment } from "@/lib/types";
import { substituteVariables, previewVariables, isLikelySecret } from "@/lib/variable-utils";
import { saveSecret, getSecret, deleteSecret } from "@/lib/tauri-bridge";
import { GraphQlPanel } from "./GraphQlPanel";
import { WebSocketPanel } from "./WebSocketPanel";

interface RequestEditorProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
  onSend: () => void;
  onSave: () => void | Promise<unknown>;
  sending: boolean;
  variableScope?: Record<string, string | null>;
  environments?: ApiEnvironment[];
  captureWarnings?: string[];
}

const methods: ApiRequestMethod[] = [
  "Get", "Post", "Put", "Patch", "Delete", "Head", "Options", "GraphQl", "WebSocket",
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
  GraphQl: "text-pink-500",
  WebSocket: "text-cyan-500",
};

type Tab = "params" | "headers" | "body" | "auth" | "graphql" | "websocket" | "capture";

function bodyLanguage(mode: RequestBodyMode) {
  if (mode === "Json") return json();
  if (mode === "Xml") return xml();
  return [];
}

interface BodyCodeEditorProps {
  value: string;
  mode: RequestBodyMode;
  onChange: (value: string) => void;
}

function BodyCodeEditor({ value, mode, onChange }: BodyCodeEditorProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const viewRef = useRef<EditorView | null>(null);
  const languageRef = useRef(new Compartment());
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  useEffect(() => {
    if (!containerRef.current) return;
    const view = new EditorView({
      state: EditorState.create({
        doc: value,
        extensions: [
          lineNumbers(),
          history(),
          keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
          syntaxHighlighting(defaultHighlightStyle),
          languageRef.current.of(bodyLanguage(mode)),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) onChangeRef.current(update.state.doc.toString());
          }),
          EditorView.theme({
            "&": { height: "12rem", fontSize: "0.875rem", backgroundColor: "transparent" },
            ".cm-scroller": { overflow: "auto", fontFamily: "ui-monospace, SFMono-Regular, Menlo, monospace" },
            ".cm-gutters": { backgroundColor: "transparent", border: "none" },
          }),
        ],
      }),
      parent: containerRef.current,
    });
    viewRef.current = view;
    return () => {
      view.destroy();
      viewRef.current = null;
    };
  }, []);

  useEffect(() => {
    viewRef.current?.dispatch({ effects: languageRef.current.reconfigure(bodyLanguage(mode)) });
  }, [mode]);

  useEffect(() => {
    const view = viewRef.current;
    if (!view || view.state.doc.toString() === value) return;
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: value },
    });
  }, [value]);

  return (
    <div className="relative rounded border bg-background" data-testid="request-body-codemirror">
      <div ref={containerRef} />
      <textarea
        data-testid="request-body-editor"
        aria-hidden="true"
        tabIndex={-1}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="absolute left-0 top-0 z-20 h-4 w-4 opacity-0"
      />
    </div>
  );
}

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

export function RequestEditor({ request, onChange, onSend, onSave, sending, variableScope = {}, environments = [], captureWarnings = [] }: RequestEditorProps) {
  const [activeTab, setActiveTab] = useState<Tab>("params");
  const [dirty, setDirty] = useState(false);
  const [showVarPreview, setShowVarPreview] = useState(false);
  const autoSaveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const savedSnapshotRef = useRef<HttpRequestEntry>(request);
  const onSaveRef = useRef(onSave);
  onSaveRef.current = onSave;

  const handleSave = useCallback(async () => {
    if (secretSaveTimer.current) {
      clearTimeout(secretSaveTimer.current);
      secretSaveTimer.current = null;
    }
    await persistSecret();
    await onSaveRef.current();
    savedSnapshotRef.current = request;
    setDirty(false);
  }, [request]);

  // Track dirty state by comparing to the last saved snapshot
  useEffect(() => {
    setDirty(JSON.stringify(request) !== JSON.stringify(savedSnapshotRef.current));
  }, [request]);

  // Auto-save with debounce
  useEffect(() => {
    if (dirty && !sending) {
      if (autoSaveTimer.current) clearTimeout(autoSaveTimer.current);
      autoSaveTimer.current = setTimeout(() => {
        handleSave();
      }, 2000);
    }
    return () => {
      if (autoSaveTimer.current) clearTimeout(autoSaveTimer.current);
    };
  }, [request, dirty, sending, handleSave]);

  // Keyboard shortcuts
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "s") {
        e.preventDefault();
        handleSave();
      }
      if ((e.ctrlKey || e.metaKey) && e.key === "Enter") {
        e.preventDefault();
        onSend();
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onSend, handleSave]);

  const setMethod = (method: ApiRequestMethod) => onChange({ ...request, method });
  const setUrl = (url: string) => onChange({ ...request, url });
  const setBodyMode = (mode: RequestBodyMode) => {
    const contentType =
      mode === "Json" ? "application/json" :
      mode === "Xml" ? "application/xml" :
      mode === "Text" ? (request.body.contentType ?? "text/plain") :
      request.body.contentType;
    onChange({ ...request, body: { ...request.body, mode, contentType } });
  };
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

  const setCaptureRules = (rules: CaptureRule[]) => onChange({ ...request, captureRules: rules });
  const updateCaptureRule = (index: number, patch: Partial<CaptureRule>) => {
    const next = request.captureRules.map((r, i) => (i === index ? { ...r, ...patch } : r));
    setCaptureRules(next);
  };
  const addCaptureRule = () =>
    setCaptureRules([
      ...request.captureRules,
      {
        id: crypto.randomUUID(),
        targetVariable: "",
        targetScope: "collection",
        source: "BodyJsonPath",
        jsonPath: "",
        headerName: "",
        isEnabled: true,
      },
    ]);
  const removeCaptureRule = (index: number) => setCaptureRules(request.captureRules.filter((_, i) => i !== index));

  const CREDENTIAL_KEY_PREFIX = "sw-secret:";
  const isGeneratedCredentialKey = (key: string | null | undefined) =>
    !!key && key.startsWith(CREDENTIAL_KEY_PREFIX);
  const generateCredentialKey = () => `${CREDENTIAL_KEY_PREFIX}${crypto.randomUUID()}`;

  const setAuthType = (type: AuthType) =>
    onChange({
      ...request,
      auth: { ...(request.auth ?? {}), type, credentialKey: null, credentialSecret: null } as AuthConfig,
    });
  const updateAuth = (patch: Partial<AuthConfig>) =>
    onChange({ ...request, auth: { ...(request.auth ?? { type: "None" }), ...patch } as AuthConfig });

  const auth = (request.auth ?? ({ type: "None" } as AuthConfig));

  const [authSecretInput, setAuthSecretInput] = useState("");
  const secretSaveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const authSecretRef = useRef(authSecretInput);
  useEffect(() => {
    authSecretRef.current = authSecretInput;
  }, [authSecretInput]);

  // Load the secret from the persisted store when the credential key changes.
  useEffect(() => {
    let active = true;
    if (isGeneratedCredentialKey(auth.credentialKey)) {
      getSecret(auth.credentialKey!).then((value) => {
        if (!active) return;
        // If the user has already started typing, don't overwrite the input with the loaded value.
        if (authSecretRef.current !== "") return;
        setAuthSecretInput(value ?? auth.credentialSecret ?? "");
      });
    } else if (auth.credentialKey) {
      // Legacy: the collections.json value itself is the secret.
      setAuthSecretInput(auth.credentialKey);
    } else {
      setAuthSecretInput(auth.credentialSecret ?? "");
    }
    return () => {
      active = false;
    };
  }, [auth.credentialKey]);

  const persistSecret = async () => {
    const key = auth.credentialKey;
    if (!key || !isGeneratedCredentialKey(key)) return;
    const value = authSecretRef.current;
    if (value.trim() === "") {
      await deleteSecret(key);
      updateAuth({ credentialKey: null, credentialSecret: null });
      setAuthSecretInput("");
    } else {
      await saveSecret(key, value);
    }
  };

  const handleSecretChange = (value: string) => {
    setAuthSecretInput(value);
    const key = isGeneratedCredentialKey(auth.credentialKey)
      ? auth.credentialKey!
      : generateCredentialKey();
    updateAuth({ credentialKey: key, credentialSecret: value });
    if (secretSaveTimer.current) clearTimeout(secretSaveTimer.current);
    secretSaveTimer.current = setTimeout(() => {
      void persistSecret();
    }, 1000);
  };

  const handleSecretBlur = () => {
    if (secretSaveTimer.current) {
      clearTimeout(secretSaveTimer.current);
      secretSaveTimer.current = null;
    }
    void persistSecret();
  };

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
    <div className="flex h-full min-w-0 flex-col border-r bg-card" data-testid="request-editor">
      {/* URL bar */}
      <div className="flex min-w-0 items-center gap-2 border-b p-3">
        <select
          data-testid="request-method-select"
          value={request.method}
          onChange={(e) => setMethod(e.target.value as ApiRequestMethod)}
          className={`shrink-0 rounded border bg-background px-2 py-1.5 text-sm font-semibold ${methodColors[request.method] ?? ""}`}
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
          className="min-w-0 flex-1 rounded border bg-background px-3 py-1.5 text-sm"
        />
        <button
          data-testid="request-var-preview"
          onClick={() => setShowVarPreview(!showVarPreview)}
          title="Preview variable substitution"
          className={`shrink-0 flex items-center gap-1 rounded border px-2 py-1.5 text-xs ${showVarPreview ? "border-primary bg-primary/10 text-primary" : "hover:bg-accent"}`}
        >
          <Eye className="h-3.5 w-3.5" />
        </button>
        <button
          data-testid="request-send-button"
          className="shrink-0 flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          onClick={onSend}
          disabled={sending || !request.url.trim()}
        >
          <Send className="h-4 w-4" />
          {sending ? "Sending..." : "Send"}
        </button>
        <button
          data-testid="request-save-button"
          className="shrink-0 flex items-center gap-1 rounded border px-3 py-1.5 text-sm font-medium hover:bg-accent"
          onClick={() => { onSave(); setDirty(false); }}
        >
          <Save className="h-4 w-4" />
          {dirty ? "Save*" : "Save"}
        </button>
      </div>

      {/* Variable preview */}
      {showVarPreview && (
        <div className="border-b bg-muted/30 px-3 py-2" data-testid="variable-preview">
          <div className="text-xs text-muted-foreground">Resolved URL:</div>
          <div className="break-all font-mono text-xs">
            {substituteVariables(request.url, variableScope)}
          </div>
          {Object.keys(previewVariables(request.url, variableScope)).length > 0 && (
            <div className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
              {Object.entries(previewVariables(request.url, variableScope)).map(([key, value]) => (
                <>
                  <span key={`k-${key}`} className="text-muted-foreground">{key}</span>
                  <span key={`v-${key}`} className="font-mono">
                    {value === null ? "<unresolved>" : isLikelySecret(key) ? "••••••••" : value}
                  </span>
                </>
              ))}
            </div>
          )}
        </div>
      )}

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
        {(() => {
          const tabs: Tab[] = request.method === "GraphQl"
            ? ["params", "headers", "graphql", "auth", "capture"]
            : request.method === "WebSocket"
            ? ["params", "headers", "websocket", "auth", "capture"]
            : ["params", "headers", "body", "auth", "capture"];
          return tabs.map((tab) => (
            <button
              key={tab}
              data-testid={`request-tab-${tab}`}
              className={`px-4 py-2 text-sm font-medium capitalize ${
                activeTab === tab ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
              }`}
              onClick={() => setActiveTab(tab)}
            >
              {tab === "params" ? "Params" : tab === "headers" ? "Headers" : tab === "body" ? "Body" : tab === "auth" ? "Auth" : tab === "graphql" ? "GraphQL" : tab === "capture" ? "Capture" : "WebSocket"}
              {tab === "params" && request.queryParams.length > 0 && (
                <span className="ml-1 text-xs text-muted-foreground">({request.queryParams.length})</span>
              )}
              {tab === "headers" && request.headers.length > 0 && (
                <span className="ml-1 text-xs text-muted-foreground">({request.headers.length})</span>
              )}
            </button>
          ));
        })()}
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
            <div className="mb-2 flex flex-wrap items-center gap-2">
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
              {request.body.mode === "Text" && (
                <input
                  data-testid="request-body-content-type"
                  type="text"
                  value={request.body.contentType ?? "text/plain"}
                  onChange={(e) => onChange({ ...request, body: { ...request.body, contentType: e.target.value } })}
                  placeholder="text/plain"
                  className="w-40 rounded border bg-background px-2 py-1 text-xs font-mono"
                />
              )}
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
              <BodyCodeEditor
                value={request.body.rawContent ?? ""}
                mode={request.body.mode}
                onChange={setBodyContent}
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
                value={authSecretInput}
                onChange={(e) => handleSecretChange(e.target.value)}
                onBlur={handleSecretBlur}
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
                  value={authSecretInput}
                  onChange={(e) => handleSecretChange(e.target.value)}
                  onBlur={handleSecretBlur}
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
                  value={authSecretInput}
                  onChange={(e) => handleSecretChange(e.target.value)}
                  onBlur={handleSecretBlur}
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
                  value={authSecretInput}
                  onChange={(e) => handleSecretChange(e.target.value)}
                  onBlur={handleSecretBlur}
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

        {/* GraphQL tab */}
        {activeTab === "graphql" && (
          <GraphQlPanel request={request} onChange={onChange} />
        )}

        {/* Capture rules tab */}
        {activeTab === "capture" && (
          <div data-testid="capture-tab">
            <div className="mb-2 flex items-center gap-2">
              <Crosshair className="h-4 w-4 text-muted-foreground" />
              <span className="text-sm font-medium">Capture Rules</span>
            </div>
            <p className="mb-3 text-xs text-muted-foreground">Extract values from responses and save them to variables automatically.</p>
            {request.captureRules.map((rule, i) => (
              <div key={rule.id} className="mb-2 flex flex-wrap items-center gap-2" data-testid={`capture-rule-row-${i}`}>
                <input
                  type="checkbox"
                  checked={rule.isEnabled}
                  onChange={(e) => updateCaptureRule(i, { isEnabled: e.target.checked })}
                  data-testid={`capture-rule-enabled-${i}`}
                />
                <select
                  value={rule.source}
                  onChange={(e) => updateCaptureRule(i, { source: e.target.value as CaptureRule["source"] })}
                  className="rounded border bg-background px-2 py-1 text-xs"
                  data-testid={`capture-rule-source-${i}`}
                >
                  <option value="BodyJsonPath">Body (JSONPath)</option>
                  <option value="ResponseHeader">Header</option>
                  <option value="StatusCode">Status Code</option>
                </select>
                {rule.source === "BodyJsonPath" && (
                  <input
                    type="text"
                    value={rule.jsonPath ?? ""}
                    onChange={(e) => updateCaptureRule(i, { jsonPath: e.target.value || null })}
                    placeholder="JSONPath (e.g. $.data.id)"
                    className="flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
                    data-testid={`capture-rule-path-${i}`}
                  />
                )}
                {rule.source === "ResponseHeader" && (
                  <input
                    type="text"
                    value={rule.headerName ?? ""}
                    onChange={(e) => updateCaptureRule(i, { headerName: e.target.value || null })}
                    placeholder="Header name (e.g. X-Request-Id)"
                    className="flex-1 rounded border bg-background px-2 py-1 text-sm"
                    data-testid={`capture-rule-header-${i}`}
                  />
                )}
                {rule.source === "StatusCode" && (
                  <span className="flex-1 rounded border bg-background px-2 py-1 text-sm text-muted-foreground" data-testid={`capture-rule-static-${i}`}>
                    status code
                  </span>
                )}
                <span className="text-sm text-muted-foreground">→</span>
                <input
                  type="text"
                  value={rule.targetVariable}
                  onChange={(e) => updateCaptureRule(i, { targetVariable: e.target.value })}
                  placeholder="Variable name"
                  className="w-32 rounded border bg-background px-2 py-1 text-sm"
                  data-testid={`capture-rule-target-${i}`}
                />
                <select
                  value={rule.targetScope}
                  onChange={(e) => updateCaptureRule(i, { targetScope: e.target.value })}
                  className="rounded border bg-background px-2 py-1 text-xs"
                  data-testid={`capture-rule-scope-${i}`}
                >
                  <option value="collection">Collection</option>
                  {environments.map((env) => (
                    <option key={env.id} value={env.id}>{env.name}</option>
                  ))}
                </select>
                <button
                  className="text-xs text-destructive"
                  onClick={() => removeCaptureRule(i)}
                  data-testid={`capture-rule-remove-${i}`}
                >
                  Remove
                </button>
              </div>
            ))}
            <button
              className="text-sm text-primary hover:underline"
              onClick={addCaptureRule}
              data-testid="add-capture-rule"
            >
              + Add capture rule
            </button>
            {captureWarnings.length > 0 && (
              <div className="mt-3 border-t pt-2" data-testid="capture-warnings">
                <div className="mb-1 text-xs font-medium text-amber-600">Capture warnings</div>
                {captureWarnings.map((w, i) => (
                  <div key={i} className="text-xs text-amber-700">{w}</div>
                ))}
              </div>
            )}
          </div>
        )}

        {/* WebSocket tab */}
        {activeTab === "websocket" && (
          <WebSocketPanel request={request} onChange={onChange} />
        )}
      </div>
    </div>
  );
}
