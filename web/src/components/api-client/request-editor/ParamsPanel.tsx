import type { HttpRequestEntry } from "@/lib/types";
import { VariableInput } from "../VariableInput";

function updateQueryParams(
    request: HttpRequestEntry,
    index: number,
    patch: Partial<{ key: string; value: string | null; isEnabled: boolean }>,
): HttpRequestEntry {
    const queryParams = request.queryParams.map((p, i) =>
        i === index ? { ...p, ...patch } : p,
    );
    return { ...request, queryParams };
}

interface ParamsPanelProps {
    request: HttpRequestEntry;
    onChange: (request: HttpRequestEntry) => void;
    variableScope: Record<string, string | null>;
}

export function ParamsPanel({
    request,
    onChange,
    variableScope,
}: ParamsPanelProps) {
    const addQueryParam = () =>
        onChange({
            ...request,
            queryParams: [
                ...request.queryParams,
                { key: "", value: "", isEnabled: true },
            ],
        });
    const removeQueryParam = (index: number) =>
        onChange({
            ...request,
            queryParams: request.queryParams.filter((_, i) => i !== index),
        });

    return (
        <div data-testid="params-tab">
            {request.queryParams.map((param, i) => (
                <div
                    key={i}
                    className="mb-1 flex items-center gap-2"
                    data-testid={`query-param-row-${i}`}
                >
                    <input
                        type="checkbox"
                        className="h-3.5 w-3.5 accent-primary"
                        checked={param.isEnabled}
                        onChange={(e) =>
                            onChange(
                                updateQueryParams(request, i, {
                                    isEnabled: e.target.checked,
                                }),
                            )
                        }
                    />
                    <input
                        type="text"
                        value={param.key}
                        onChange={(e) =>
                            onChange(
                                updateQueryParams(request, i, {
                                    key: e.target.value,
                                }),
                            )
                        }
                        placeholder="Key"
                        className="h-8 w-32 rounded-md border bg-surface-1 px-2 font-mono text-xs"
                    />
                    <VariableInput
                        testId={`query-param-value-${i}`}
                        ariaLabel={`Query parameter ${i + 1} value`}
                        value={param.value ?? ""}
                        onChange={(value) =>
                            onChange(updateQueryParams(request, i, { value }))
                        }
                        scope={variableScope}
                        placeholder="Value"
                        metricsClassName="px-2 py-1.5 font-mono text-xs"
                    />
                    <button
                        className="text-xs text-state-blocked-ink"
                        onClick={() => removeQueryParam(i)}
                    >
                        Remove
                    </button>
                </div>
            ))}
            <button
                data-testid="add-query-param-button"
                className="text-sm font-medium text-accent-ink hover:underline"
                onClick={addQueryParam}
            >
                + Add parameter
            </button>
        </div>
    );
}
