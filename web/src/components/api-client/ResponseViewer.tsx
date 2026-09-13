import { useState, useEffect, useMemo } from "react";
import {
  Copy, Check, Terminal, History, Save, TrendingUp, AlertCircle, Download, WrapText, ArrowLeft,
} from "lucide-react";
import type { ApiClientExecutionResponse, HttpRequestEntry, ResponseExample } from "@/lib/types";
import { formatBytes, formatElapsed } from "@/lib/api-client-format";
import { statusTone, toneChipStyle, CountBadge } from "./method-badge";
import { selectBodyLanguage, downloadExtension } from "@/lib/response-body";
import { loadViewPreference, saveViewPreference } from "@/lib/stores/panel-preferences";
import { ResponseBodyViewer } from "./ResponseBodyViewer";
import { buildCurl } from "@/lib/curl";
import { tryPrettifyJson } from "@/lib/pretty-json";

export interface ResponseHistoryEntry {
  id: number;
  response: ApiClientExecutionResponse;
  timestamp: number;
}

interface ResponseViewerProps {
  response: ApiClientExecutionResponse | null;
  sending: boolean;
  request?: HttpRequestEntry | null;
  /** Owned by the page so it survives remount and stays per-tab. */
  history?: ResponseHistoryEntry[];
  onSaveExample?: (name: string, response: ApiClientExecutionResponse) => void;
  /** Used to resolve `{{tokens}}` in the cURL panel's body and headers. */
  variableScope?: Record<string, string | null>;
}

type Tab = "body" | "headers" | "history";

const WRAP_PREF_KEY = "api-client-response-wrap";
const PRETTY_PREF_KEY = "api-client-response-pretty";

function tryPrettyPrint(content: string, contentType: string | null): string {
  const trimmed = content.trimStart();
  if (contentType?.includes("json") || trimmed.startsWith("{") || trimmed.startsWith("[")) {
    // Falls back to the raw content when it does not parse, as before — but now also
    // handles a leading UTF-8 BOM, which .NET endpoints routinely emit.
    return tryPrettifyJson(content) ?? content;
  }
  // Only genuine XML, not "anything starting with `<`". Now that Pretty is the
  // default, the broader test would reflow every HTML page received, inserting
  // newlines between tags where whitespace can be significant.
  if (contentType?.includes("xml") || trimmed.startsWith("<?xml")) {
    return content.replace(/></g, ">\n<").replace(/^\s+$/gm, "");
  }
  return content;
}

export function ResponseViewer({
  response,
  sending,
  request,
  history = [],
  onSaveExample,
  variableScope = {},
}: ResponseViewerProps) {
  const [activeTab, setActiveTab] = useState<Tab>("body");
  const [copied, setCopied] = useState(false);
  // Pretty by default. A minified JSON payload on one 4000-character line is not
  // a readable response, and every operator reached for the Pretty toggle on every
  // single send. Persisted, so choosing Raw sticks.
  const [prettyPrinted, setPrettyPrinted] = useState<boolean>(() =>
    loadViewPreference<boolean>(PRETTY_PREF_KEY, true),
  );
  const [showCurl, setShowCurl] = useState(false);
  const [revealCurlSecrets, setRevealCurlSecrets] = useState(false);
  const [copiedCurl, setCopiedCurl] = useState(false);
  const [showSaveExample, setShowSaveExample] = useState(false);
  const [exampleName, setExampleName] = useState("");
  /** `null` = viewing the live response; otherwise a saved example id. */
  const [viewingExampleId, setViewingExampleId] = useState<string | null>(null);
  const [wrap, setWrap] = useState<boolean>(() => loadViewPreference<boolean>(WRAP_PREF_KEY, true));

  const savedExamples: ResponseExample[] = request?.responseExamples ?? [];

  useEffect(() => {
    // Deliberately does not reset `prettyPrinted`: it is a persisted view
    // preference, not per-response state, and resetting it here is what made the
    // Pretty toggle feel like it never stuck.
    setCopied(false);
    setShowCurl(false);
    setRevealCurlSecrets(false);
    setViewingExampleId(null);
  }, [response]);

  const setPretty = (next: boolean) => {
    setPrettyPrinted(next);
    saveViewPreference(PRETTY_PREF_KEY, next);
  };

  const toggleWrap = () => {
    // Computed outside the updater: React may invoke an updater twice in
    // StrictMode, and updaters must stay free of side effects.
    const next = !wrap;
    setWrap(next);
    saveViewPreference(WRAP_PREF_KEY, next);
  };

  // Derived above the early returns below, because it uses a hook: this component
  // returns early while sending and before the first response, so a `useMemo`
  // placed after those returns changes the hook count the moment a response
  // arrives, and React throws instead of rendering it.
  const viewingExample = viewingExampleId
    ? savedExamples.find((e) => e.id === viewingExampleId) ?? null
    : null;
  const liveBody = response
    ? (response.errorMessage ? response.errorMessage : response.responseBody ?? "")
    : "";
  const rawBody = viewingExample ? viewingExample.body ?? "" : liveBody;
  const bodyContentType = viewingExample ? viewingExample.contentType : response?.contentType ?? null;
  // Memoized because Pretty is now the default: without it a 512 kB body would be
  // reformatted on every render, including every toolbar interaction.
  const displayBody = useMemo(
    () => (prettyPrinted ? tryPrettyPrint(rawBody, bodyContentType) : rawBody),
    [prettyPrinted, rawBody, bodyContentType],
  );

  // Only the *first* send (no previous response to compare against) replaces
  // the whole panel — once a response exists, sending again dims it in place
  // instead of unmounting it, so the "tweak and resend to compare" workflow
  // keeps its comparison point visible while the new request is in flight.
  if (sending && !response) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="response-viewer">
        Sending request...
      </div>
    );
  }

  if (!response) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground" data-testid="response-viewer">
        Click Send to see the response
      </div>
    );
  }

  const isError = !!response.errorMessage;

  const isGraphQlError = !isError && response.contentType?.includes("json") && liveBody.includes("errors");
  let graphQlErrors: string[] = [];
  if (isGraphQlError) {
    try {
      const parsed = JSON.parse(liveBody);
      if (parsed.errors && Array.isArray(parsed.errors)) {
        graphQlErrors = parsed.errors.map((e: { message?: string }) => e.message || String(e));
      }
    } catch {
      // Body is not the GraphQL error envelope after all; leave the list empty.
    }
  }

  const copyBody = async () => {
    await navigator.clipboard.writeText(rawBody);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const copyCurl = async () => {
    if (request) {
      const curl = buildCurl(request, response.resolvedUrl, variableScope, response.sentHeaders ?? null, revealCurlSecrets);
      await navigator.clipboard.writeText(curl);
      setCopiedCurl(true);
      setTimeout(() => setCopiedCurl(false), 2000);
    }
  };

  const downloadBody = () => {
    const ext = downloadExtension(selectBodyLanguage(bodyContentType, rawBody));
    const blob = new Blob([rawBody], { type: bodyContentType ?? "text/plain" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `response.${ext}`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  const submitExample = () => {
    const name = exampleName.trim();
    if (!name || !onSaveExample) return;
    onSaveExample(name, response);
    setShowSaveExample(false);
    setExampleName("");
  };

  return (
    <div
      className={`flex h-full min-w-0 flex-col bg-card ${sending ? "opacity-60" : ""}`}
      data-testid="response-viewer"
    >
      {/* Resend-in-flight banner: the response below is the *previous* one,
          kept visible on purpose so a "tweak and resend to compare" workflow
          does not lose its comparison point mid-request. */}
      {sending && (
        <div
          className="flex items-center gap-2 border-b bg-muted/30 px-3 py-1.5 text-xs text-muted-foreground"
          data-testid="response-resending-indicator"
        >
          <span
            className="h-3 w-3 shrink-0 animate-spin rounded-full border-2 border-muted-foreground border-t-transparent"
            aria-hidden="true"
          />
          Sending new request… showing the previous response below.
        </div>
      )}
      {/* Status bar */}
      <div className="flex items-center gap-3 border-b p-3">
        <span
          data-testid="response-status"
          className="rounded px-2 py-1 text-sm font-semibold"
          style={toneChipStyle(isError ? "destructive" : statusTone(response.statusCode))}
        >
          {isError ? "ERROR" : (response.statusText || response.statusCode.toString())}
        </span>
        <span data-testid="response-elapsed" className="text-xs text-muted-foreground">
          {formatElapsed(response.elapsedMs)}
        </span>
        <span data-testid="response-size" className="text-xs text-muted-foreground">
          {formatBytes(response.contentLength)}
        </span>
        {response.responseBodyTruncated && (
          <span className="text-xs" style={{ color: "var(--warning)" }}>(truncated)</span>
        )}
        <div className="flex-1" />
        {request && !isError && (
          <button
            onClick={() => setShowCurl(!showCurl)}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="response-curl-toggle"
          >
            <Terminal className="h-3 w-3" /> cURL
          </button>
        )}
      </div>

      {/* Capture warnings */}
      {response.captureWarnings && response.captureWarnings.length > 0 && (
        <div
          className="border-b p-3"
          style={{ backgroundColor: "color-mix(in oklch, var(--warning) 10%, transparent)" }}
          data-testid="response-capture-warnings"
        >
          <div className="flex items-center gap-2 text-xs font-medium" style={{ color: "var(--warning)" }}>
            <AlertCircle className="h-4 w-4" />
            Capture warnings
          </div>
          <ul className="mt-1 list-inside list-disc text-xs" style={{ color: "var(--warning)" }}>
            {response.captureWarnings.map((w, i) => (
              <li key={i}>{w}</li>
            ))}
          </ul>
        </div>
      )}

      {/* cURL preview */}
      {showCurl && request && !isError && (
        <div className="border-b bg-muted/30 p-3" data-testid="response-curl-panel">
          <div className="mb-1 flex items-center justify-between">
            <span className="text-xs font-medium text-muted-foreground">
              cURL command
              {response.sentHeaders && response.sentHeaders.length > 0 && " (as sent)"}
            </span>
            <div className="flex items-center gap-3">
              <button
                onClick={() => setRevealCurlSecrets(!revealCurlSecrets)}
                className="text-xs text-primary hover:underline"
                data-testid="response-curl-reveal"
              >
                {revealCurlSecrets ? "Hide secrets" : "Reveal secrets"}
              </button>
              <button
                onClick={copyCurl}
                className="flex items-center gap-1 text-xs text-primary hover:underline"
                data-testid="response-copy-curl"
              >
                {copiedCurl ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
                {copiedCurl ? "Copied!" : "Copy"}
              </button>
            </div>
          </div>
          <pre className="overflow-auto whitespace-pre-wrap break-all font-mono text-xs">
            {buildCurl(request, response.resolvedUrl, variableScope, response.sentHeaders ?? null, revealCurlSecrets)}
          </pre>
        </div>
      )}

      {/* Tabs */}
      <div className="flex border-b">
        <button
          data-testid="response-tab-body"
          className={`px-4 py-2 text-sm font-medium ${
            activeTab === "body" ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
          }`}
          onClick={() => setActiveTab("body")}
        >
          Body
        </button>
        <button
          data-testid="response-tab-headers"
          className={`flex items-center gap-1.5 px-4 py-2 text-sm font-medium ${
            activeTab === "headers" ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
          }`}
          onClick={() => setActiveTab("headers")}
        >
          Headers
          <CountBadge count={response.headers.length} />
        </button>
        <button
          data-testid="response-tab-history"
          className={`flex items-center gap-1.5 px-4 py-2 text-sm font-medium ${
            activeTab === "history" ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
          }`}
          onClick={() => setActiveTab("history")}
        >
          <History className="h-3.5 w-3.5" />
          History
          <CountBadge count={history.length} />
        </button>
      </div>

      {/* Tab content */}
      <div className="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden p-3">
        {activeTab === "body" && (
          <div className="flex min-h-0 min-w-0 flex-1 flex-col" data-testid="response-body-container">
            {/* GraphQL errors */}
            {graphQlErrors.length > 0 && (
              <div
                className="mb-3 rounded-md border p-3"
                style={{
                  borderColor: "color-mix(in oklch, var(--warning) 30%, transparent)",
                  backgroundColor: "color-mix(in oklch, var(--warning) 10%, transparent)",
                }}
                data-testid="graphql-errors"
              >
                <div className="mb-1 flex items-center gap-2">
                  <AlertCircle className="h-4 w-4" style={{ color: "var(--warning)" }} />
                  <span className="text-sm font-medium" style={{ color: "var(--warning)" }}>GraphQL Errors</span>
                </div>
                <ul className="list-inside list-disc space-y-1 text-xs">
                  {graphQlErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            )}

            {/* Body toolbar */}
            <div className="mb-2 flex flex-wrap items-center gap-2">
              <div className="flex overflow-hidden rounded border" role="group" aria-label="Body formatting">
                <button
                  onClick={() => setPretty(true)}
                  aria-pressed={prettyPrinted}
                  className={`px-2 py-0.5 text-xs ${prettyPrinted ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                  data-testid="response-pretty-toggle"
                >
                  Pretty
                </button>
                <button
                  onClick={() => setPretty(false)}
                  aria-pressed={!prettyPrinted}
                  className={`border-l px-2 py-0.5 text-xs ${!prettyPrinted ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                  data-testid="response-raw-toggle"
                >
                  Raw
                </button>
              </div>
              <button
                onClick={toggleWrap}
                aria-pressed={wrap}
                className={`flex items-center gap-1 rounded border px-2 py-0.5 text-xs ${wrap ? "border-primary text-primary" : "hover:bg-accent"}`}
                data-testid="response-wrap-toggle"
              >
                <WrapText className="h-3 w-3" /> Wrap
              </button>
              <button
                onClick={copyBody}
                className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                data-testid="response-copy-body"
              >
                {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
                {copied ? "Copied!" : "Copy"}
              </button>
              <button
                onClick={downloadBody}
                className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                data-testid="response-download-body"
              >
                <Download className="h-3 w-3" /> Download
              </button>
              {onSaveExample && !viewingExample && (
                <button
                  onClick={() => setShowSaveExample(!showSaveExample)}
                  className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                  data-testid="response-save-example"
                >
                  <Save className="h-3 w-3" /> Save Example
                </button>
              )}
            </div>

            {showSaveExample && (
              <div className="mb-2 flex items-center gap-2" data-testid="save-example-form">
                <input
                  type="text"
                  value={exampleName}
                  onChange={(e) => setExampleName(e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Enter") submitExample(); }}
                  placeholder="Example name..."
                  className="flex-1 rounded border bg-background px-2 py-1 text-xs"
                  autoFocus
                />
                <button
                  onClick={submitExample}
                  className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground"
                  data-testid="save-example-confirm"
                >
                  Save
                </button>
                <button onClick={() => setShowSaveExample(false)} className="rounded border px-2 py-1 text-xs">Cancel</button>
              </div>
            )}

            {savedExamples.length > 0 && (
              <div className="mb-2 flex flex-wrap items-center gap-1" data-testid="saved-examples">
                <span className="text-xs text-muted-foreground">Saved examples:</span>
                {savedExamples.map((ex) => (
                  <button
                    key={ex.id}
                    onClick={() => setViewingExampleId(ex.id)}
                    className={`rounded border px-1.5 py-0.5 text-xs hover:bg-accent ${
                      viewingExampleId === ex.id ? "border-primary text-primary" : ""
                    }`}
                    data-testid={`saved-example-${ex.name}`}
                  >
                    {ex.name}
                  </button>
                ))}
              </div>
            )}

            {viewingExample && (
              <div
                className="mb-2 flex items-center gap-2 rounded border px-2 py-1 text-xs"
                style={{ borderColor: "var(--primary)", color: "var(--primary)" }}
                data-testid="viewing-example-banner"
              >
                <span className="flex-1">
                  Viewing saved example “{viewingExample.name}” ({viewingExample.statusCode})
                </span>
                <button
                  onClick={() => setViewingExampleId(null)}
                  className="flex items-center gap-1 rounded border px-1.5 py-0.5 hover:bg-accent"
                  data-testid="viewing-example-return"
                >
                  <ArrowLeft className="h-3 w-3" /> Back to live response
                </button>
              </div>
            )}

            <ResponseBodyViewer body={displayBody} contentType={bodyContentType} wrap={wrap} />
          </div>
        )}

        {activeTab === "headers" && (
          <div className="min-h-0 flex-1 overflow-auto">
            <table className="w-full min-w-0 text-sm" data-testid="response-headers-table">
              <tbody>
                {response.headers.map((h, i) => (
                  <tr key={i} data-testid={`response-header-row-${i}`}>
                    <td className="py-1 pr-4 align-top font-medium text-muted-foreground">{h.name}</td>
                    <td className="break-all py-1">{h.value}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {activeTab === "history" && (
          <div className="min-h-0 w-full min-w-0 flex-1 overflow-auto" data-testid="response-history-panel">
            {/* Response time sparkline */}
            {history.length > 1 && (
              <div className="mb-4" data-testid="response-time-sparkline">
                <div className="mb-1 flex items-center gap-2">
                  <TrendingUp className="h-3.5 w-3.5 text-muted-foreground" />
                  <span className="text-xs font-medium text-muted-foreground">Response times</span>
                </div>
                <SparkLine history={history} />
              </div>
            )}
            {/* History list */}
            {history.length === 0 ? (
              <div className="text-sm text-muted-foreground">No response history yet</div>
            ) : (
              <div className="space-y-1">
                {history.map((h) => (
                  <div key={h.id} className="flex items-center gap-3 rounded border px-3 py-2 text-xs" data-testid={`response-history-item-${h.id}`}>
                    <span
                      className="rounded px-1.5 py-0.5 font-semibold"
                      style={toneChipStyle(statusTone(h.response.statusCode))}
                    >
                      {h.response.statusCode}
                    </span>
                    <span className="text-muted-foreground">{formatElapsed(h.response.elapsedMs)}</span>
                    <span className="text-muted-foreground">{new Date(h.timestamp).toLocaleTimeString()}</span>
                    <span className="flex-1 truncate text-muted-foreground">{h.response.resolvedUrl}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function SparkLine({ history }: { history: ResponseHistoryEntry[] }) {
  const bars = useMemo(() => {
    const recent = history.slice(0, 20);
    const maxMs = Math.max(...recent.map((x) => x.response.elapsedMs), 1);
    return [...recent].reverse().map((h) => ({
      id: h.id,
      elapsedMs: h.response.elapsedMs,
      height: Math.max(2, (h.response.elapsedMs / maxMs) * 100),
    }));
  }, [history]);

  return (
    <div className="flex h-12 items-end gap-1">
      {bars.map((bar) => (
        <div
          key={bar.id}
          className="flex-1 rounded-sm bg-primary/30"
          style={{ height: `${bar.height}%` }}
          title={formatElapsed(bar.elapsedMs)}
        />
      ))}
    </div>
  );
}
