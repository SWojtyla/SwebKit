import { Wand2 } from "lucide-react";
import type { HttpRequestEntry } from "@/lib/types";

interface GraphQlPanelProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
}

function tryPrettyPrintJson(content: string): string {
  try {
    return JSON.stringify(JSON.parse(content), null, 2);
  } catch {
    return content;
  }
}

export function GraphQlPanel({ request, onChange }: GraphQlPanelProps) {
  const setQuery = (graphQlQuery: string) => onChange({ ...request, graphQlQuery });
  const setVariables = (graphQlVariables: string) => onChange({ ...request, graphQlVariables });
  const setOperation = (graphQlSelectedOperation: string) =>
    onChange({ ...request, graphQlSelectedOperation: graphQlSelectedOperation || null });

  const prettyPrintVariables = () => {
    if (request.graphQlVariables) {
      setVariables(tryPrettyPrintJson(request.graphQlVariables));
    }
  };

  // Extract operation names from query (simple regex)
  const operations = request.graphQlQuery
    ? [...request.graphQlQuery.matchAll(/(?:query|mutation|subscription)\s+(\w+)/g)].map((m) => m[1])
    : [];

  return (
    <div className="space-y-3" data-testid="graphql-panel">
      {/* Query editor */}
      <div>
        <div className="mb-1 flex items-center justify-between">
          <label className="text-xs font-medium text-muted-foreground">Query</label>
        </div>
        <textarea
          data-testid="graphql-query-input"
          value={request.graphQlQuery ?? ""}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="query MyQuery {\n  field\n}"
          className="h-48 w-full resize-y rounded border bg-background px-3 py-2 font-mono text-sm"
          spellCheck={false}
        />
      </div>

      {/* Operation selector */}
      {operations.length > 1 && (
        <div>
          <label className="mb-1 block text-xs font-medium text-muted-foreground">Operation</label>
          <select
            data-testid="graphql-operation-select"
            value={request.graphQlSelectedOperation ?? ""}
            onChange={(e) => setOperation(e.target.value)}
            className="w-full rounded border bg-background px-2 py-1.5 text-sm"
          >
            <option value="">Auto (first)</option>
            {operations.map((op) => (
              <option key={op} value={op}>{op}</option>
            ))}
          </select>
        </div>
      )}

      {/* Variables editor */}
      <div>
        <div className="mb-1 flex items-center justify-between">
          <label className="text-xs font-medium text-muted-foreground">Variables (JSON)</label>
          <button
            onClick={prettyPrintVariables}
            className="flex items-center gap-1 text-xs text-primary hover:underline"
            data-testid="graphql-pretty-variables"
          >
            <Wand2 className="h-3 w-3" /> Format
          </button>
        </div>
        <textarea
          data-testid="graphql-variables-input"
          value={request.graphQlVariables ?? ""}
          onChange={(e) => setVariables(e.target.value)}
          placeholder='{\n  "key": "value"\n}'
          className="h-32 w-full resize-y rounded border bg-background px-3 py-2 font-mono text-sm"
          spellCheck={false}
        />
      </div>
    </div>
  );
}
