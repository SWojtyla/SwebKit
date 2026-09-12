import type { HttpRequestEntry } from "@/lib/types";
import { VariableInput } from "../VariableInput";

function updateQueryParams(
  request: HttpRequestEntry,
  index: number,
  patch: Partial<{ key: string; value: string | null; isEnabled: boolean }>,
): HttpRequestEntry {
  const queryParams = request.queryParams.map((p, i) => (i === index ? { ...p, ...patch } : p));
  return { ...request, queryParams };
}

interface ParamsPanelProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
  variableScope: Record<string, string | null>;
}

export function ParamsPanel({ request, onChange, variableScope }: ParamsPanelProps) {
  const addQueryParam = () =>
    onChange({
      ...request,
      queryParams: [...request.queryParams, { key: "", value: "", isEnabled: true }],
    });
  const removeQueryParam = (index: number) =>
    onChange({ ...request, queryParams: request.queryParams.filter((_, i) => i !== index) });

  return (
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
          <VariableInput
            testId={`query-param-value-${i}`}
            ariaLabel={`Query parameter ${i + 1} value`}
            value={param.value ?? ""}
            onChange={(value) => onChange(updateQueryParams(request, i, { value }))}
            scope={variableScope}
            placeholder="Value"
            metricsClassName="px-2 py-1 text-sm"
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
  );
}
