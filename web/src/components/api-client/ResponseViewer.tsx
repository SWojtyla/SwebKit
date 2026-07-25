import { useState, useEffect } from "react";
import { Copy, Check, Terminal } from "lucide-react";
import type { ApiClientExecutionResponse } from "@/lib/types";

interface ResponseViewerProps {
  response: ApiClientExecutionResponse | null;
  sending: boolean;
  request?: { method: string; url: string } | null;
}

type Tab = "body" | "headers";

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

function buildCurl(request: { method: string; url: string }, response: ApiClientExecutionResponse): string {
  const parts = [`curl -X ${request.method.toUpperCase()}`];
  // Add resolved URL
  parts.push(`"${response.resolvedUrl}"`);
  // Add headers
  for (const h of response.headers) {
    parts.push(`-H "${h.name}: ${h.value}"`);
  }
  return parts.join(" \\\n  ");
}

export function ResponseViewer({ response, sending, request }: ResponseViewerProps) {
  const [activeTab, setActiveTab] = useState<Tab>("body");
  const [copied, setCopied] = useState(false);
  const [prettyPrinted, setPrettyPrinted] = useState(false);
  const [showCurl, setShowCurl] = useState(false);
  const [copiedCurl, setCopiedCurl] = useState(false);

  useEffect(() => {
    setPrettyPrinted(false);
    setCopied(false);
    setShowCurl(false);
  }, [response]);

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
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-auto p-3">
        {activeTab === "body" && (
          <div data-testid="response-body-container">
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
            </div>
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
      </div>
    </div>
  );
}
