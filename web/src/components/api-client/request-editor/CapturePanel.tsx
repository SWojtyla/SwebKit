import { Crosshair, Search } from "lucide-react";
import type { CaptureRule, ApiEnvironment } from "@/lib/types";

interface CapturePanelProps {
    rules: CaptureRule[];
    environments: ApiEnvironment[];
    captureWarnings: string[];
    onUpdateRule: (index: number, patch: Partial<CaptureRule>) => void;
    onAddRule: () => void;
    onRemoveRule: (index: number) => void;
    onPickJsonPath: (index: number, path: string) => void;
}

export function CapturePanel({
    rules,
    environments,
    captureWarnings,
    onUpdateRule,
    onAddRule,
    onRemoveRule,
    onPickJsonPath,
}: CapturePanelProps) {
    return (
        <div data-testid="capture-tab">
            <div className="mb-2 flex items-center gap-2">
                <Crosshair className="h-4 w-4 text-fg-muted" />
                <span className="text-xs font-medium uppercase tracking-wide text-fg-muted">
                    Capture Rules
                </span>
            </div>
            <p className="mb-3 text-xs text-fg-muted">
                Extract values from responses and save them to variables
                automatically.
            </p>
            {rules.map((rule, i) => (
                <div
                    key={rule.id}
                    className="mb-2 flex flex-wrap items-center gap-2"
                    data-testid={`capture-rule-row-${i}`}
                >
                    <input
                        type="checkbox"
                        className="h-3.5 w-3.5 accent-primary"
                        checked={rule.isEnabled}
                        onChange={(e) =>
                            onUpdateRule(i, { isEnabled: e.target.checked })
                        }
                        data-testid={`capture-rule-enabled-${i}`}
                    />
                    <select
                        value={rule.source}
                        onChange={(e) =>
                            onUpdateRule(i, {
                                source: e.target.value as CaptureRule["source"],
                            })
                        }
                        className="h-8 rounded-md border bg-surface-1 px-2 text-xs"
                        data-testid={`capture-rule-source-${i}`}
                    >
                        <option value="BodyJsonPath">Body (JSONPath)</option>
                        <option value="ResponseHeader">Header</option>
                        <option value="StatusCode">Status Code</option>
                    </select>
                    {rule.source === "BodyJsonPath" && (
                        <div className="flex flex-1 items-center gap-1">
                            <input
                                type="text"
                                value={rule.jsonPath ?? ""}
                                onChange={(e) =>
                                    onUpdateRule(i, {
                                        jsonPath: e.target.value || null,
                                    })
                                }
                                placeholder="JSONPath (e.g. $.data.id)"
                                className="h-8 min-w-0 flex-1 rounded-md border bg-surface-1 px-2 font-mono text-xs"
                                data-testid={`capture-rule-path-${i}`}
                            />
                            <button
                                onClick={() =>
                                    onPickJsonPath(i, rule.jsonPath ?? "")
                                }
                                className="flex h-8 items-center rounded-md border px-1.5 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg"
                                title="Pick JSONPath"
                                data-testid={`capture-rule-picker-${i}`}
                            >
                                <Search className="h-3 w-3" />
                            </button>
                        </div>
                    )}
                    {rule.source === "ResponseHeader" && (
                        <input
                            type="text"
                            value={rule.headerName ?? ""}
                            onChange={(e) =>
                                onUpdateRule(i, {
                                    headerName: e.target.value || null,
                                })
                            }
                            placeholder="Header name (e.g. X-Request-Id)"
                            className="h-8 flex-1 rounded-md border bg-surface-1 px-2 font-mono text-xs"
                            data-testid={`capture-rule-header-${i}`}
                        />
                    )}
                    {rule.source === "StatusCode" && (
                        <span
                            className="flex h-8 flex-1 items-center rounded-md border bg-surface-1 px-2 text-xs text-fg-muted"
                            data-testid={`capture-rule-static-${i}`}
                        >
                            status code
                        </span>
                    )}
                    <span className="text-sm text-fg-muted">→</span>
                    <input
                        type="text"
                        value={rule.targetVariable}
                        onChange={(e) =>
                            onUpdateRule(i, { targetVariable: e.target.value })
                        }
                        placeholder="Variable name"
                        className="h-8 w-32 rounded-md border bg-surface-1 px-2 text-sm"
                        data-testid={`capture-rule-target-${i}`}
                    />
                    <select
                        value={rule.targetScope}
                        onChange={(e) =>
                            onUpdateRule(i, { targetScope: e.target.value })
                        }
                        className="h-8 rounded-md border bg-surface-1 px-2 text-xs"
                        data-testid={`capture-rule-scope-${i}`}
                    >
                        <option value="collection">Collection</option>
                        {environments.map((env) => (
                            <option key={env.id} value={env.id}>
                                {env.name}
                            </option>
                        ))}
                    </select>
                    <button
                        className="text-xs text-state-blocked-ink"
                        onClick={() => onRemoveRule(i)}
                        data-testid={`capture-rule-remove-${i}`}
                    >
                        Remove
                    </button>
                </div>
            ))}
            <button
                className="text-sm font-medium text-accent-ink hover:underline"
                onClick={onAddRule}
                data-testid="add-capture-rule"
            >
                + Add capture rule
            </button>
            {captureWarnings.length > 0 && (
                <div
                    className="mt-3 border-t pt-2"
                    data-testid="capture-warnings"
                >
                    <div className="mb-1 text-xs font-medium text-state-needs-ink">
                        Warnings
                    </div>
                    {captureWarnings.map((w, i) => (
                        <div key={i} className="text-xs text-state-needs-ink">
                            {w}
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}
