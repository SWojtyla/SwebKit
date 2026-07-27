import { useState, useEffect, useRef } from "react";
import { Copy, Check, Terminal, History, Save, TrendingUp, AlertCircle } from "lucide-react";
import type { ApiClientExecutionResponse, HttpRequestEntry } from "@/lib/types";

interface ResponseHistoryEntry {
  id: number;
  response: ApiClientExecutionResponse;
  timestamp: number;
}

interface ResponseViewerProps {
  response: ApiClientExecutionResponse | null;
  sending: boolean;
  request?: HttpRequestEntry | null;
}

type Tab = "body" | "headers" | "history";

function statusColor(code: number): string {
  if (code === 0) return "bg-destructive/10 text-destructive";
  if (code >= 200 && code < 300) return "bg-green-500/10 text-green-500";
  if (code >= 300 && code < 400) return "bg-blue-500/10 text-blue-500";
  if (code >= 400 && code < 500) return "bg-yellow-500/10 text-yellow-500";
  return "bg-red-500/10 text-red-500";
}

function tryPrettyPrint(content: string, contentType: string | null): string {
  if (contentType?.includes("json") || content.trim().startsWith("{") || content.trim().startsWith("[")) {
    try {
      return JSON.stringify(JSON.parse(content), null, 2);
    } catch {
      return content;
    }
  }
  if (contentType?.includes("xml") || content.trim().startsWith("<")) {
    // Simple XML pretty-print: indent between tags
    return content.replace(/></g, ">\n<").replace(/^\s+$/gm, "");
  }
  return content;
}

function buildCurl(request: HttpRequestEntry, response: ApiClientExecutionResponse): string {
  const parts = [`curl -X ${request.method.toUpperCase()}`];

  // Request headers (enabled only)
  for (const h of request.headers) {
    if (h.isEnabled && h.key) {
      parts.push(`-H "${h.key}: ${h.value ?? ""}"`);
    }
  }

  // Body for raw modes
  if (request.body.mode === "Json" || request.body.mode === "Xml" || request.body.mode === "Text") {
    const contentType = request.body.contentType ?? (request.body.mode === "Json" ? "application/json" : request.body.mode === "Xml" ? "application/xml" : "text/plain");
    parts.push(`-H "Content-Type: ${contentType}"`);
    if (request.body.rawContent) {
      parts.push(`-d '${request.body.rawContent.replace(/'/g, "'\\''")}'`);
    }
  }

  // Add resolved URL
  parts.push(`"${response.resolvedUrl}"`);
  return parts.join(" \\\n  ");
}

export function ResponseViewer({ response, sending, request }: ResponseViewerProps) {
  const [activeTab, setActiveTab] = useState<Tab>("body");
  const [copied, setCopied] = useState(false);
  const [prettyPrinted, setPrettyPrinted] = useState(false);
  const [showCurl, setShowCurl] = useState(false);
  const [copiedCurl, setCopiedCurl] = useState(false);
  const [history, setHistory] = useState<ResponseHistoryEntry[]>([]);
  const [savedExamples, setSavedExamples] = useState<{ name: string; body: string }[]>([]);
  const [showSaveExample, setShowSaveExample] = useState(false);
  const [exampleName, setExampleName] = useState("");
  const historyIdRef = useRef(0);

  useEffect(() => {
    setPrettyPrinted(false);
    setCopied(false);
    setShowCurl(false);
  }, [response]);

  useEffect(() => {
    if (response && !sending) {
      const id = ++historyIdRef.current;
      setHistory((prev) => [{ id, response, timestamp: Date.now() }, ...prev].slice(0, 20));
    }
  }, [response, sending]);

  if (sending) {
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
  const rawBody = isError ? response.errorMessage ?? "" : response.responseBody ?? "";
  const displayBody = prettyPrinted ? tryPrettyPrint(rawBody, response.contentType) : rawBody;

  const isGraphQlError = !isError && response.contentType?.includes("json") && rawBody.includes("errors");
  let graphQlErrors: string[] = [];
  if (isGraphQlError) {
    try {
      const parsed = JSON.parse(rawBody);
      if (parsed.errors && Array.isArray(parsed.errors)) {
        graphQlErrors = parsed.errors.map((e: any) => e.message || String(e));
      }
    } catch {}
  }

  const copyBody = async () => {
    await navigator.clipboard.writeText(rawBody);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const copyCurl = async () => {
    if (request) {
      const curl = buildCurl(request, response);
      await navigator.clipboard.writeText(curl);
      setCopiedCurl(true);
      setTimeout(() => setCopiedCurl(false), 2000);
    }
  };

  return (
    <div className="flex h-full flex-col bg-card" data-testid="response-viewer">
      {/* Status bar */}
      <div className="flex items-center gap-3 border-b p-3">
        <span
          data-testid="response-status"
          className={`rounded px-2 py-1 text-sm font-semibold ${statusColor(response.statusCode)}`}
        >
          {isError ? "ERROR" : `${response.statusCode} ${response.statusText}`}
        </span>
        <span data-testid="response-elapsed" className="text-xs text-muted-foreground">
          {response.elapsedMs.toFixed(0)} ms
        </span>
        <span data-testid="response-size" className="text-xs text-muted-foreground">
          {response.contentLength >= 0 ? `${response.contentLength} bytes` : "size unknown"}
        </span>
        {response.responseBodyTruncated && (
          <span className="text-xs text-yellow-500">(truncated)</span>
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
        <div className="border-b bg-yellow-500/10 p-3" data-testid="response-capture-warnings">
          <div className="flex items-center gap-2 text-xs font-medium text-yellow-500">
            <AlertCircle className="h-4 w-4" />
            Capture warnings
          </div>
          <ul className="mt-1 list-disc list-inside text-xs text-yellow-500/90">
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
            <span className="text-xs font-medium text-muted-foreground">cURL command</span>
            <button
              onClick={copyCurl}
              className="flex items-center gap-1 text-xs text-primary hover:underline"
              data-testid="response-copy-curl"
            >
              {copiedCurl ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
              {copiedCurl ? "Copied!" : "Copy"}
            </button>
          </div>
          <pre className="overflow-auto whitespace-pre-wrap break-all font-mono text-xs">
            {buildCurl(request, response)}
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
          className={`px-4 py-2 text-sm font-medium ${
            activeTab === "headers" ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
          }`}
          onClick={() => setActiveTab("headers")}
        >
          Headers
        </button>
        <button
          data-testid="response-tab-history"
          className={`flex items-center gap-1 px-4 py-2 text-sm font-medium ${
            activeTab === "history" ? "border-b-2 border-primary text-foreground" : "text-muted-foreground"
          }`}
          onClick={() => setActiveTab("history")}
        >
          <History className="h-3.5 w-3.5" />
          History ({history.length})
        </button>
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-auto p-3">
        {activeTab === "body" && (
          <div data-testid="response-body-container">
            {/* GraphQL errors */}
            {graphQlErrors.length > 0 && (
              <div className="mb-3 rounded-md border border-yellow-500/30 bg-yellow-500/10 p-3" data-testid="graphql-errors">
                <div className="flex items-center gap-2 mb-1">
                  <AlertCircle className="h-4 w-4 text-yellow-500" />
                  <span className="text-sm font-medium text-yellow-500">GraphQL Errors</span>
                </div>
                <ul className="list-disc list-inside text-xs space-y-1">
                  {graphQlErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            )}
            <div className="mb-2 flex items-center gap-2">
              <button
                onClick={() => setPrettyPrinted(!prettyPrinted)}
                className="rounded border px-2 py-0.5 text-xs hover:bg-accent"
                data-testid="response-pretty-toggle"
              >
                {prettyPrinted ? "Raw" : "Pretty"}
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
                onClick={() => setShowSaveExample(!showSaveExample)}
                className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent"
                data-testid="response-save-example"
              >
                <Save className="h-3 w-3" /> Save Example
              </button>
            </div>
            {showSaveExample && (
              <div className="mb-2 flex items-center gap-2" data-testid="save-example-form">
                <input
                  type="text"
                  value={exampleName}
                  onChange={(e) => setExampleName(e.target.value)}
                  placeholder="Example name..."
                  className="flex-1 rounded border bg-background px-2 py-1 text-xs"
                  autoFocus
                />
                <button
                  onClick={() => {
                    if (exampleName.trim()) {
                      setSavedExamples((prev) => [...prev, { name: exampleName, body: rawBody }]);
                      setShowSaveExample(false);
                      setExampleName("");
                    }
                  }}
                  className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground"
                >
                  Save
                </button>
                <button onClick={() => setShowSaveExample(false)} className="rounded border px-2 py-1 text-xs">Cancel</button>
              </div>
            )}
            {savedExamples.length > 0 && (
              <div className="mb-2" data-testid="saved-examples">
                <span className="text-xs text-muted-foreground">Saved examples: </span>
                {savedExamples.map((ex, i) => (
                  <button key={i} className="ml-1 text-xs text-primary hover:underline">{ex.name}</button>
                ))}
              </div>
            )}
            <pre
              data-testid="response-body"
              className="whitespace-pre-wrap break-all font-mono text-sm"
            >
              {displayBody}
            </pre>
          </div>
        )}
        {activeTab === "headers" && (
          <table className="w-full text-sm" data-testid="response-headers-table">
            <tbody>
              {response.headers.map((h, i) => (
                <tr key={i} data-testid={`response-header-row-${i}`}>
                  <td className="py-1 pr-4 font-medium text-muted-foreground align-top">{h.name}</td>
                  <td className="py-1 break-all">{h.value}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {activeTab === "history" && (
          <div data-testid="response-history-panel">
            {/* Response time sparkline */}
            {history.length > 1 && (
              <div className="mb-4" data-testid="response-time-sparkline">
                <div className="flex items-center gap-2 mb-1">
                  <TrendingUp className="h-3.5 w-3.5 text-muted-foreground" />
                  <span className="text-xs font-medium text-muted-foreground">Response times</span>
                </div>
                <div className="flex items-end gap-1 h-12">
                  {history.slice(0, 20).reverse().map((h) => {
                    const maxMs = Math.max(...history.map((x) => x.response.elapsedMs), 1);
                    const height = Math.max(2, (h.response.elapsedMs / maxMs) * 100);
                    return (
                      <div
                        key={h.id}
                        className="flex-1 bg-primary/30 rounded-sm"
                        style={{ height: `${height}%` }}
                        title={`${h.response.elapsedMs.toFixed(0)}ms`}
                      />
                    );
                  })}
                </div>
              </div>
            )}
            {/* History list */}
            {history.length === 0 ? (
              <div className="text-sm text-muted-foreground">No response history yet</div>
            ) : (
              <div className="space-y-1">
                {history.map((h) => (
                  <div key={h.id} className="flex items-center gap-3 rounded border px-3 py-2 text-xs" data-testid={`response-history-item-${h.id}`}>
                    <span className={`rounded px-1.5 py-0.5 font-semibold ${statusColor(h.response.statusCode)}`}>
                      {h.response.statusCode}
                    </span>
                    <span className="text-muted-foreground">{h.response.elapsedMs.toFixed(0)}ms</span>
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
