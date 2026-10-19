import { FolderOpen, Wand2, Minimize2 } from "lucide-react";
import type {
    FormDataField,
    HttpRequestEntry,
    RequestBodyMode,
} from "@/lib/types";
import { tryPrettifyJson } from "@/lib/pretty-json";
import { pickFilePath } from "@/lib/tauri-bridge";
import { BodyCodeEditor } from "./BodyCodeEditor";
import { VariableInput } from "../VariableInput";

function updateFormField(
    request: HttpRequestEntry,
    index: number,
    patch: Partial<FormDataField>,
): HttpRequestEntry {
    const formData = request.body.formData.map((f, i) =>
        i === index ? { ...f, ...patch } : f,
    );
    return { ...request, body: { ...request.body, formData } };
}

const bodyModes: RequestBodyMode[] = [
    "None",
    "Json",
    "Xml",
    "Text",
    "FormData",
];

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

export function BodyPanel({
    request,
    onChange,
    variableScope,
}: BodyPanelProps) {
    const setBodyMode = (mode: RequestBodyMode) => {
        const contentType =
            mode === "Json"
                ? "application/json"
                : mode === "Xml"
                  ? "application/xml"
                  : mode === "Text"
                    ? (request.body.contentType ?? "text/plain")
                    : request.body.contentType;
        onChange({ ...request, body: { ...request.body, mode, contentType } });
    };
    const setBodyContent = (rawContent: string) =>
        onChange({ ...request, body: { ...request.body, rawContent } });

    const prettyPrint = () => {
        if (request.body.rawContent) {
            setBodyContent(
                tryPrettifyJson(request.body.rawContent) ??
                    request.body.rawContent,
            );
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
                <span className="text-xs font-medium uppercase tracking-wide text-fg-muted">
                    Body
                </span>
                <select
                    data-testid="request-body-mode-select"
                    value={request.body.mode}
                    onChange={(e) =>
                        setBodyMode(e.target.value as RequestBodyMode)
                    }
                    className="h-7 rounded-md border bg-surface-1 px-2 text-xs"
                >
                    {bodyModes.map((m) => (
                        <option key={m} value={m}>
                            {m}
                        </option>
                    ))}
                </select>
                {request.body.mode === "Text" && (
                    <input
                        data-testid="request-body-content-type"
                        type="text"
                        value={request.body.contentType ?? "text/plain"}
                        onChange={(e) =>
                            onChange({
                                ...request,
                                body: {
                                    ...request.body,
                                    contentType: e.target.value,
                                },
                            })
                        }
                        placeholder="text/plain"
                        className="h-7 w-40 rounded-md border bg-surface-1 px-2 text-xs font-mono"
                    />
                )}
                {request.body.mode === "Json" && request.body.rawContent && (
                    <>
                        <button
                            onClick={prettyPrint}
                            title="Pretty print JSON"
                            className="flex h-7 items-center gap-1 rounded-md border px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg"
                            data-testid="body-pretty-print"
                        >
                            <Wand2 className="h-3 w-3" /> Format
                        </button>
                        <button
                            onClick={minify}
                            title="Minify JSON"
                            className="flex h-7 items-center gap-1 rounded-md border px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg"
                            data-testid="body-minify"
                        >
                            <Minimize2 className="h-3 w-3" /> Minify
                        </button>
                    </>
                )}
            </div>
            {request.body.mode === "FormData" && (
                <div data-testid="body-formdata-rows">
                    {request.body.formData.map((field, i) => (
                        <div
                            key={i}
                            className="mb-1 flex items-center gap-2"
                            data-testid={`formdata-row-${i}`}
                        >
                            <input
                                type="checkbox"
                                className="h-3.5 w-3.5 accent-primary"
                                checked={field.isEnabled}
                                onChange={(e) =>
                                    onChange(
                                        updateFormField(request, i, {
                                            isEnabled: e.target.checked,
                                        }),
                                    )
                                }
                            />
                            <input
                                type="text"
                                value={field.key}
                                onChange={(e) =>
                                    onChange(
                                        updateFormField(request, i, {
                                            key: e.target.value,
                                        }),
                                    )
                                }
                                placeholder="Field"
                                className="h-8 w-32 rounded-md border bg-surface-1 px-2 font-mono text-xs"
                                data-testid={`formdata-key-${i}`}
                            />
                            <select
                                value={field.isFile ? "file" : "text"}
                                onChange={(e) =>
                                    onChange(
                                        updateFormField(request, i, {
                                            isFile: e.target.value === "file",
                                        }),
                                    )
                                }
                                className="h-8 rounded-md border bg-surface-1 px-1 text-xs"
                                data-testid={`formdata-type-${i}`}
                                title="text = plain value · file = local file uploaded as a multipart part"
                            >
                                <option value="text">text</option>
                                <option value="file">file</option>
                            </select>
                            <VariableInput
                                testId={`formdata-value-${i}`}
                                ariaLabel={`Form field ${i + 1} ${field.isFile ? "file path" : "value"}`}
                                value={field.value ?? ""}
                                onChange={(value) =>
                                    onChange(
                                        updateFormField(request, i, {
                                            value,
                                        }),
                                    )
                                }
                                scope={variableScope}
                                placeholder={
                                    field.isFile
                                        ? "Path to file (e.g. C:\\docs\\test.pdf)"
                                        : "Value"
                                }
                                metricsClassName="px-2 py-1.5 font-mono text-xs"
                            />
                            {field.isFile && (
                                <button
                                    type="button"
                                    title="Browse for a file"
                                    aria-label={`Browse file for form field ${i + 1}`}
                                    data-testid={`formdata-browse-${i}`}
                                    className="flex h-8 items-center rounded-md border px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg"
                                    onClick={async () => {
                                        const path = await pickFilePath(
                                            "Select file to upload",
                                        );
                                        if (path) {
                                            onChange(
                                                updateFormField(request, i, {
                                                    value: path,
                                                }),
                                            );
                                        }
                                    }}
                                >
                                    <FolderOpen className="h-3.5 w-3.5" />
                                </button>
                            )}
                            <button
                                className="text-xs text-state-blocked-ink"
                                onClick={() =>
                                    onChange({
                                        ...request,
                                        body: {
                                            ...request.body,
                                            formData:
                                                request.body.formData.filter(
                                                    (_, j) => j !== i,
                                                ),
                                        },
                                    })
                                }
                            >
                                Remove
                            </button>
                        </div>
                    ))}
                    <button
                        data-testid="add-formdata-field-button"
                        className="text-sm font-medium text-accent-ink hover:underline"
                        onClick={() =>
                            onChange({
                                ...request,
                                body: {
                                    ...request.body,
                                    formData: [
                                        ...request.body.formData,
                                        {
                                            key: "",
                                            value: "",
                                            isEnabled: true,
                                        },
                                    ],
                                },
                            })
                        }
                    >
                        + Add field
                    </button>
                </div>
            )}
            {request.body.mode !== "None" &&
                request.body.mode !== "FormData" && (
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
