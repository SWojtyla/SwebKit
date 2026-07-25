import { useState } from "react";
import type { ApiClientExecutionResponse } from "@/lib/types";

interface ResponseViewerProps {
  response: ApiClientExecutionResponse | null;
  sending: boolean;
}

type Tab = "body" | "headers";

export function ResponseViewer({ response, sending }: ResponseViewerProps) {
  const [activeTab, setActiveTab] = useState<Tab>("body");

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

  return (
    <div className="flex h-full flex-col bg-card" data-testid="response-viewer">
      <div className="flex items-center gap-3 border-b p-3">
        <span
          data-testid="response-status"
          className={`rounded px-2 py-1 text-sm font-semibold ${
            isError
              ? "bg-destructive/10 text-destructive"
              : response.statusCode >= 200 && response.statusCode < 300
              ? "bg-green-500/10 text-green-500"
              : "bg-yellow-500/10 text-yellow-500"
          }`}
        >
          {isError ? "ERROR" : response.statusText}
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
      </div>

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

      <div className="flex-1 overflow-auto p-3">
        {activeTab === "body" && (
          <pre
            data-testid="response-body"
            className="whitespace-pre-wrap break-all font-mono text-sm"
          >
            {isError ? response.errorMessage : response.responseBody ?? ""}
          </pre>
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
