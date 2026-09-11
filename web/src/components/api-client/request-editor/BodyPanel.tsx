import { Wand2, Minimize2 } from "lucide-react";
import type { HttpRequestEntry, RequestBodyMode } from "@/lib/types";
import { tryPrettifyJson } from "@/lib/pretty-json";
import { BodyCodeEditor } from "./BodyCodeEditor";

const bodyModes: RequestBodyMode[] = ["None", "Json", "Xml", "Text", "FormData"];

function tryMinifyJson(content: string): string {
  try {
    return JSON.stringify(JSON.parse(content));
  } catch {
    return content;
  }
}

interface BodyPanelProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
  variableScope: Record<string, string | null>;
}

export function BodyPanel({ request, onChange, variableScope }: BodyPanelProps) {
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

  const prettyPrint = () => {
    if (request.body.rawContent) {
      setBodyContent(tryPrettifyJson(request.body.rawContent) ?? request.body.rawContent);
    }
  };

  const minify = () => {
    if (request.body.rawContent) {
      setBodyContent(tryMinifyJson(request.body.rawContent));
    }
  };

  return (
    <div className="flex min-h-0 flex-1 flex-col" data-testid="body-tab">
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
          scope={variableScope}
        />
      )}
    </div>
  );
}
