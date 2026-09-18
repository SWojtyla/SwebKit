import { useState, useEffect, useRef, useCallback, Fragment } from "react";
import { Save, Send, Eye, Sparkles } from "lucide-react";
import { GenerateApiRequestPanel } from "./GenerateApiRequestPanel";
import { JsonPathPicker } from "./JsonPathPicker";
import { RequestActionsPanel } from "./RequestActionsPanel";
import type { HttpRequestEntry, ApiRequestMethod, AuthType, AuthConfig, CaptureRule, ApiEnvironment } from "@/lib/types";
import { substituteVariables, previewVariables, isLikelySecret } from "@/lib/variable-utils";
import { unresolvedVariableNames } from "@/lib/variableHighlight";
import { authSubstitutedText } from "@/lib/auth-variables";
import { saveSecret, getSecret, deleteSecret } from "@/lib/tauri-bridge";
import { METHOD_META, methodMeta, toneTextStyle, CountBadge } from "./method-badge";
import { GraphQlPanel } from "./GraphQlPanel";
import { VariableInput } from "./VariableInput";
import { WebSocketPanel } from "./WebSocketPanel";
import { RequestNameHeading } from "./request-editor/RequestNameHeading";
import { ParamsPanel } from "./request-editor/ParamsPanel";
import { HeadersPanel } from "./request-editor/HeadersPanel";
import { BodyPanel } from "./request-editor/BodyPanel";
import { AuthPanel } from "./request-editor/AuthPanel";
import { CapturePanel } from "./request-editor/CapturePanel";

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

type Tab = "params" | "headers" | "body" | "auth" | "graphql" | "websocket" | "capture" | "actions";

export function RequestEditor({ request, onChange, onSend, onSave, sending, variableScope = {}, environments = [], captureWarnings = [] }: RequestEditorProps) {
  const [activeTab, setActiveTab] = useState<Tab>("params");
  const [dirty, setDirty] = useState(false);
  const [showVarPreview, setShowVarPreview] = useState(false);
  const [showGeneratePanel, setShowGeneratePanel] = useState(false);
  const [jsonPathPicker, setJsonPathPicker] = useState<{ open: boolean; index: number; path: string } | null>(null);
  const autoSaveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const savedSnapshotRef = useRef<HttpRequestEntry>(request);
  const onSaveRef = useRef(onSave);
  onSaveRef.current = onSave;
  const persistSecretRef = useRef(persistSecret);
  persistSecretRef.current = persistSecret;

  const handleSave = useCallback(async () => {
    if (secretSaveTimer.current) {
      clearTimeout(secretSaveTimer.current);
      secretSaveTimer.current = null;
    }
    await persistSecretRef.current();
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
    // credentialSecret is read as a fallback while loading; depending on it would
    // re-run (and overwrite the input) on every keystroke that autosaves the secret.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth.credentialKey]);

  async function persistSecret() {
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
  }

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

  // Every place a `{{token}}` is substituted before sending, so the preview and the
  // warning cover the whole request rather than just the URL. The body was the gap
  // that mattered: `HttpRequestExecutor` substitutes it, but nothing showed it. Auth was
  // the next one — and the worst, because a bad token there fails with a 401/400 from the
  // server rather than anything the app could point at. Only names are collected, never
  // values, so listing the secret's tokens reveals nothing the environment doesn't.
  const substitutedText = [
    request.url,
    request.body.rawContent ?? "",
    ...request.headers.filter((h) => h.isEnabled).map((h) => h.value ?? ""),
    ...authSubstitutedText(auth, authSecretInput),
  ].join("\n");
  const previewedVariables = previewVariables(substitutedText, variableScope);
  const unresolvedNames = unresolvedVariableNames(substitutedText, variableScope);

  return (
    <div className="relative flex h-full min-w-0 flex-col border-r bg-card" data-testid="request-editor">
      {/* URL bar */}
      <div className="flex min-w-0 items-center gap-2 border-b p-3">
        <select
          data-testid="request-method-select"
          value={request.method}
          onChange={(e) => setMethod(e.target.value as ApiRequestMethod)}
          className="shrink-0 rounded border bg-background px-2 py-1.5 text-sm font-semibold"
          style={toneTextStyle(methodMeta(request.method).tone)}
        >
          {methods.map((m) => (
            <option key={m} value={m}>
              {METHOD_META[m].short}
            </option>
          ))}
        </select>
        {/* The variable-preview toggle belongs to the URL field, so they are
            grouped together rather than sitting as a peer of Send/Save. */}
        <div className="flex min-w-0 flex-1 items-center gap-1">
          <VariableInput
            testId="request-url-input"
            ariaLabel="Request URL"
            value={request.url}
            onChange={setUrl}
            scope={variableScope}
            placeholder="https://api.example.com/resource"
          />
          <button
            data-testid="request-var-preview"
            onClick={() => setShowVarPreview(!showVarPreview)}
            title="Preview variable substitution"
            aria-pressed={showVarPreview}
            className={`flex shrink-0 items-center gap-1 rounded border px-2 py-1.5 text-xs ${showVarPreview ? "border-primary bg-primary/10 text-primary" : "hover:bg-accent"}`}
          >
            <Eye className="h-3.5 w-3.5" />
          </button>
        </div>
        <button
          data-testid="request-ask-ai-button"
          className="shrink-0 flex items-center gap-1 rounded border px-2.5 py-1.5 text-sm hover:bg-accent"
          onClick={() => setShowGeneratePanel(true)}
          title="Ask AI to generate or edit this request"
        >
          <Sparkles className="h-4 w-4" />
        </button>
        <button
          data-testid="request-send-button"
          className="shrink-0 flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          onClick={onSend}
          disabled={sending || !request.url.trim()}
          title={sending ? "Sending…" : !request.url.trim() ? "Enter a URL first" : undefined}
        >
          <Send className="h-4 w-4" />
          {sending ? "Sending..." : "Send"}
        </button>
        <button
          data-testid="request-save-button"
          className="shrink-0 flex items-center gap-1.5 rounded border px-3 py-1.5 text-sm font-medium hover:bg-accent"
          onClick={() => { onSave(); setDirty(false); }}
          title={dirty ? "Unsaved changes" : "Saved"}
        >
          <Save className="h-4 w-4" />
          Save
          {dirty && (
            <span
              className="h-1.5 w-1.5 rounded-full"
              style={{ backgroundColor: "var(--warning)" }}
              data-testid="request-dirty-dot"
              aria-label="Unsaved changes"
            />
          )}
        </button>
      </div>

      {/* An undefined variable is substituted with its own literal text, so the
          request goes out containing `{{NAME}}` and the server rejects it. That was
          invisible until now: the preview below is behind a toggle nobody opens
          before a send that they expect to work. This banner is not. */}
      {unresolvedNames.length > 0 && (
        <div
          className="border-b px-3 py-1.5 text-xs"
          style={{
            color: "var(--destructive)",
            backgroundColor: "color-mix(in oklch, var(--destructive) 10%, transparent)",
          }}
          data-testid="unresolved-variable-warning"
        >
          {unresolvedNames.length === 1
            ? `1 variable is not defined in this scope and will be sent literally: `
            : `${unresolvedNames.length} variables are not defined in this scope and will be sent literally: `}
          <span className="font-mono">{unresolvedNames.join(", ")}</span>
        </div>
      )}

      {/* Variable preview */}
      {showVarPreview && (
        <div className="border-b bg-muted/30 px-3 py-2" data-testid="variable-preview">
          <div className="text-xs text-muted-foreground">Resolved URL:</div>
          <div className="break-all font-mono text-xs">
            {substituteVariables(request.url, variableScope)}
          </div>
          {Object.keys(previewedVariables).length > 0 && (
            <div className="mt-2 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
              {Object.entries(previewedVariables).map(([key, value]) => (
                <Fragment key={key}>
                  <span className="text-muted-foreground">{key}</span>
                  <span className="font-mono">
                    {value === null ? "<unresolved>" : isLikelySecret(key) ? "••••••••" : value}
                  </span>
                </Fragment>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Tabs, with the request name as an inline-editable heading on the same
          row — a full-width input in the middle of the pane read as a form field
          and made the name the third echo of the same string. */}
      <div className="flex items-center border-b">
        {/* The tab strip scrolls rather than overflowing, so a long request name
            can never end up rendered on top of the tab buttons. */}
        <div className="flex min-w-0 flex-1 overflow-x-auto">
        {(() => {
          const tabs: Tab[] = request.method === "GraphQl"
            ? ["params", "headers", "graphql", "auth", "capture", "actions"]
            : request.method === "WebSocket"
            ? ["params", "headers", "websocket", "auth", "capture", "actions"]
            : ["params", "headers", "body", "auth", "capture", "actions"];
          return tabs.map((tab) => (
            <button
              key={tab}
              data-testid={`request-tab-${tab}`}
              className={`flex shrink-0 items-center gap-1.5 whitespace-nowrap px-4 py-2 text-sm font-medium capitalize ${
                activeTab === tab ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
              }`}
              onClick={() => setActiveTab(tab)}
            >
              {tab === "params" ? "Params" : tab === "headers" ? "Headers" : tab === "body" ? "Body" : tab === "auth" ? "Auth" : tab === "graphql" ? "GraphQL" : tab === "capture" ? "Capture" : tab === "actions" ? "Actions" : "WebSocket"}
              {tab === "params" && <CountBadge count={request.queryParams.length} />}
              {tab === "headers" && <CountBadge count={request.headers.length} />}
              {tab === "capture" && <CountBadge count={request.captureRules.length} />}
              {tab === "actions" && (
                <CountBadge
                  count={(request.preRequestActions?.length ?? 0) + (request.postRequestActions?.length ?? 0)}
                />
              )}
            </button>
          ));
        })()}
        </div>
        <RequestNameHeading
          name={request.name}
          onRename={(name) => onChange({ ...request, name })}
        />
      </div>

      {/* Tab content */}
      <div className="flex min-h-0 flex-1 flex-col overflow-auto p-4">
        {/* Params tab */}
        {activeTab === "params" && (
          <ParamsPanel request={request} onChange={onChange} variableScope={variableScope} />
        )}

        {/* Headers tab */}
        {activeTab === "headers" && (
          <HeadersPanel request={request} onChange={onChange} variableScope={variableScope} />
        )}

        {/* Body tab */}
        {activeTab === "body" && (
          <BodyPanel request={request} onChange={onChange} variableScope={variableScope} />
        )}

        {/* Auth tab */}
        {activeTab === "auth" && (
          <AuthPanel
            auth={auth}
            secretInput={authSecretInput}
            variableScope={variableScope}
            onAuthTypeChange={setAuthType}
            onAuthPatch={updateAuth}
            onSecretChange={handleSecretChange}
            onSecretBlur={handleSecretBlur}
          />
        )}

        {/* GraphQL tab */}
        {activeTab === "graphql" && (
          <GraphQlPanel request={request} onChange={onChange} />
        )}

        {/* Capture rules tab */}
        {activeTab === "capture" && (
          <CapturePanel
            rules={request.captureRules}
            environments={environments}
            captureWarnings={captureWarnings}
            onUpdateRule={updateCaptureRule}
            onAddRule={addCaptureRule}
            onRemoveRule={removeCaptureRule}
            onPickJsonPath={(index, path) => setJsonPathPicker({ open: true, index, path })}
          />
        )}

        {/* WebSocket tab */}
        {activeTab === "websocket" && (
          <WebSocketPanel request={request} onChange={onChange} />
        )}

        {/* Actions tab */}
        {activeTab === "actions" && (
          <RequestActionsPanel request={request} onChange={onChange} />
        )}
      </div>
      {showGeneratePanel && (
        <GenerateApiRequestPanel requestId={request.id} onClose={() => setShowGeneratePanel(false)} />
      )}
      {jsonPathPicker?.open && (
        <JsonPathPicker
          initialBody={request.responseExamples[0]?.body ?? undefined}
          initialPath={jsonPathPicker.path}
          onSelect={(path) => updateCaptureRule(jsonPathPicker.index, { jsonPath: path })}
          onClose={() => setJsonPathPicker(null)}
        />
      )}
    </div>
  );
}
