import { useMemo, useState } from "react";
import {
    X,
    FileText,
    Pencil,
    Save,
    Eye,
    Loader2,
    Check,
    AlertCircle,
} from "lucide-react";
import {
    useAksResourceYaml,
    useAksApplyYaml,
    useAksValidateYaml,
} from "@/lib/hooks";
import { useNotification } from "@/components/layout/notification-context";
import { highlightYaml } from "@/lib/yamlHighlight";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { filterGeneratedAnnotations } from "@/lib/yaml-noise";

interface YamlViewerProps {
    ns: string;
    kind: string;
    name: string;
    /** Cluster the resource lives in — read and apply both route there. */
    context?: string | null;
    onClose: () => void;
}

export function YamlViewer({
    ns,
    kind,
    name,
    context,
    onClose,
}: YamlViewerProps) {
    const {
        data: yaml,
        isLoading,
        error,
    } = useAksResourceYaml(ns, kind, name, context ?? undefined);
    const applyMutation = useAksApplyYaml();
    const validateMutation = useAksValidateYaml();
    const { notify } = useNotification();
    const [copied, setCopied] = useState(false);
    const [editMode, setEditMode] = useState(false);
    const [editedYaml, setEditedYaml] = useState("");
    const [validationError, setValidationError] = useState<string | null>(null);
    const [showApplyConfirm, setShowApplyConfirm] = useState(false);
    const [hideGeneratedAnnotations, setHideGeneratedAnnotations] =
        useState(true);
    const filteredYaml = useMemo(
        () => filterGeneratedAnnotations(yaml ?? ""),
        [yaml],
    );
    const displayedYaml = hideGeneratedAnnotations
        ? filteredYaml.yaml
        : (yaml ?? "");

    const handleCopy = () => {
        navigator.clipboard.writeText(yaml ?? "");
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
    };

    const toggleEdit = () => {
        if (editMode) {
            setEditedYaml("");
            setEditMode(false);
            setValidationError(null);
        } else {
            setEditedYaml(yaml ?? "");
            setEditMode(true);
            setValidationError(null);
        }
    };

    const handleValidate = () => {
        setValidationError(null);
        validateMutation.mutate(
            { ns, yaml: editedYaml, context: context ?? undefined },
            {
                onSuccess: (result) => {
                    if (result.error) {
                        setValidationError(result.error);
                        notify(
                            "error",
                            `YAML validation failed: ${result.error}`,
                        );
                    } else {
                        notify("success", "YAML is valid");
                    }
                },
                onError: (err) => {
                    const message =
                        err instanceof Error ? err.message : String(err);
                    setValidationError(message);
                    notify("error", `YAML validation failed: ${message}`);
                },
            },
        );
    };

    const handleApply = () => {
        setShowApplyConfirm(true);
    };

    const runApply = () => {
        setShowApplyConfirm(false);
        setValidationError(null);
        validateMutation.mutate(
            { ns, yaml: editedYaml, context: context ?? undefined },
            {
                onSuccess: (result) => {
                    if (result.error) {
                        setValidationError(result.error);
                        notify(
                            "error",
                            `YAML validation failed: ${result.error}`,
                        );
                        return;
                    }
                    applyMutation.mutate(
                        {
                            ns,
                            kind,
                            name,
                            yaml: editedYaml,
                            context: context ?? undefined,
                        },
                        {
                            onSuccess: () => {
                                notify(
                                    "success",
                                    `${kind}/${name} applied successfully`,
                                );
                                setEditMode(false);
                                setEditedYaml("");
                            },
                            onError: (err) => {
                                const message =
                                    err instanceof Error
                                        ? err.message
                                        : String(err);
                                setValidationError(message);
                                notify(
                                    "error",
                                    `Failed to apply ${kind}/${name}: ${message}`,
                                );
                            },
                        },
                    );
                },
                onError: (err) => {
                    const message =
                        err instanceof Error ? err.message : String(err);
                    setValidationError(message);
                    notify("error", `YAML validation failed: ${message}`);
                },
            },
        );
    };

    const isBusy = applyMutation.isPending || validateMutation.isPending;

    return (
        <div className="flex h-full flex-col" data-testid="yaml-viewer">
            <div className="flex items-center gap-2 border-b px-4 py-2">
                <FileText className="h-4 w-4 text-fg-muted" />
                <span className="truncate font-mono text-sm font-semibold">
                    {kind}/{name}
                </span>
                <span className="text-xs text-fg-muted">YAML</span>
                {!editMode && filteredYaml.hidden > 0 && (
                    <button
                        onClick={() =>
                            setHideGeneratedAnnotations((hidden) => !hidden)
                        }
                        className="h-7 rounded-md border bg-surface-1 px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg"
                        aria-pressed={hideGeneratedAnnotations}
                        data-testid="yaml-generated-annotations-toggle"
                    >
                        {hideGeneratedAnnotations
                            ? `${filteredYaml.hidden} generated annotations hidden · show`
                            : "Hide generated annotations"}
                    </button>
                )}
                <div className="ml-auto flex items-center gap-2">
                    {editMode && (
                        <>
                            <button
                                onClick={handleValidate}
                                disabled={isBusy || !editedYaml.trim()}
                                title={
                                    isBusy
                                        ? "A YAML operation is in progress"
                                        : !editedYaml.trim()
                                          ? "YAML is empty"
                                          : undefined
                                }
                                className="flex h-7 items-center gap-1 rounded-md border bg-surface-1 px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg disabled:opacity-50"
                                data-testid="yaml-validate"
                            >
                                {validateMutation.isPending ? (
                                    <Loader2 className="h-3 w-3 animate-spin" />
                                ) : (
                                    <Check className="h-3 w-3" />
                                )}
                                Validate
                            </button>
                            <button
                                onClick={handleApply}
                                disabled={isBusy || !editedYaml.trim()}
                                title={
                                    isBusy
                                        ? "A YAML operation is in progress"
                                        : !editedYaml.trim()
                                          ? "YAML is empty"
                                          : undefined
                                }
                                className="flex h-7 items-center gap-1 rounded-md border border-primary bg-primary px-2 text-xs text-primary-foreground hover:bg-accent-hover disabled:opacity-50"
                                data-testid="yaml-apply"
                            >
                                {applyMutation.isPending ? (
                                    <Loader2 className="h-3 w-3 animate-spin" />
                                ) : (
                                    <Save className="h-3 w-3" />
                                )}
                                Apply
                            </button>
                        </>
                    )}
                    <button
                        onClick={toggleEdit}
                        className={`flex h-7 items-center gap-1 rounded-md border bg-surface-1 px-2 text-xs hover:bg-surface-2 hover:text-fg ${editMode ? "border-primary text-accent-ink" : "text-fg-muted"}`}
                        data-testid="yaml-edit-toggle"
                    >
                        {editMode ? (
                            <Eye className="h-3 w-3" />
                        ) : (
                            <Pencil className="h-3 w-3" />
                        )}
                        {editMode ? "View" : "Edit"}
                    </button>
                    <button
                        onClick={handleCopy}
                        className="h-7 rounded-md border bg-surface-1 px-2 text-xs text-fg-muted hover:bg-surface-2 hover:text-fg"
                        data-testid="yaml-copy"
                    >
                        {copied ? "Copied!" : "Copy"}
                    </button>
                    <button
                        onClick={onClose}
                        className="inline-flex h-7 w-7 items-center justify-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>
            </div>
            {showApplyConfirm && (
                <ConfirmBar
                    message={`Apply ${kind}/${name} in ${ns}${context ? ` (${context})` : ""}? This will update the cluster resource.`}
                    confirmLabel="Apply"
                    onConfirm={runApply}
                    onCancel={() => setShowApplyConfirm(false)}
                    testId="yaml-apply-confirm"
                />
            )}
            <div className="flex-1 overflow-auto bg-sunken p-3">
                {isLoading ? (
                    <div className="text-primary text-xs font-mono">
                        Loading YAML...
                    </div>
                ) : error ? (
                    <div
                        className="text-state-blocked-ink text-xs font-mono"
                        data-testid="yaml-error"
                    >
                        Error: {error.message}
                    </div>
                ) : editMode ? (
                    <div className="flex h-full flex-col gap-2">
                        {validationError && (
                            <div
                                className="flex items-start gap-2 rounded-md border border-state-blocked/30 bg-state-blocked-soft p-2 text-xs text-state-blocked-ink"
                                data-testid="yaml-validation-error"
                            >
                                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                                <pre className="whitespace-pre-wrap font-mono">
                                    {validationError}
                                </pre>
                            </div>
                        )}
                        <textarea
                            value={editedYaml}
                            onChange={(e) => setEditedYaml(e.target.value)}
                            className="h-full w-full flex-1 bg-surface-1 text-fg text-xs font-mono resize-none border-none outline-none"
                            data-testid="yaml-editor"
                            spellCheck={false}
                        />
                    </div>
                ) : (
                    <pre
                        className="yml-viewer whitespace-pre-wrap break-all text-xs font-mono text-fg"
                        data-testid="yaml-content"
                        dangerouslySetInnerHTML={{
                            __html: highlightYaml(displayedYaml, true),
                        }}
                    />
                )}
            </div>
        </div>
    );
}
