import type { HttpRequestEntry } from "@/lib/types";
import { VariableInput } from "../VariableInput";

function updateHeaders(
  request: HttpRequestEntry,
  index: number,
  patch: Partial<{ key: string; value: string | null; isEnabled: boolean }>,
): HttpRequestEntry {
  const headers = request.headers.map((h, i) => (i === index ? { ...h, ...patch } : h));
  return { ...request, headers };
}

interface HeadersPanelProps {
  request: HttpRequestEntry;
  onChange: (request: HttpRequestEntry) => void;
  variableScope: Record<string, string | null>;
}

export function HeadersPanel({ request, onChange, variableScope }: HeadersPanelProps) {
  const addHeader = () =>
    onChange({
      ...request,
      headers: [...request.headers, { key: "", value: "", isEnabled: true }],
    });
  const removeHeader = (index: number) =>
    onChange({ ...request, headers: request.headers.filter((_, i) => i !== index) });

  return (
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
          <VariableInput
            testId={`request-header-value-${i}`}
            ariaLabel={`Header ${i + 1} value`}
            value={header.value ?? ""}
            onChange={(value) => onChange(updateHeaders(request, i, { value }))}
            scope={variableScope}
            placeholder="Value"
            metricsClassName="px-2 py-1 text-sm"
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
  );
}
