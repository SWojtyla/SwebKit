import { useMemo, useState } from "react";
import {
    X,
    Trash2,
    FileText,
    Plus,
    Copy,
    Save,
    Send,
    Pencil,
} from "lucide-react";
import {
    useSbTemplates,
    useSbDeleteTemplate,
    useSbSaveTemplate,
} from "@/lib/hooks";
import { tryReindentJson } from "@/lib/pretty-json";
import { useNotification } from "@/components/layout/NotificationSystem";
import { ConfirmBar } from "@/components/shared/ConfirmBar";
import { Dialog } from "@/components/shared/Dialog";
import type { SbMessageTemplate } from "@/lib/types";
import { MessageBodyEditor } from "./MessageBodyEditor";

interface Props {
    /**
     * Pick mode: when set, clicking a template applies it instead of opening it
     * for editing (composer quick-apply).
     */
    onPick?: (template: SbMessageTemplate) => void;
    /**
     * Manage mode only: when set, the editor shows a "Use in composer" action
     * that hands the open template to the composer.
     */
    onUseInComposer?: (template: SbMessageTemplate) => void;
    onClose: () => void;
}

interface DraftState {
    id: string;
    name: string;
    subject: string;
    correlationId: string;
    contentType: string;
    body: string;
    properties: { key: string; value: string }[];
    createdAt: string;
    /** True while the draft is a not-yet-saved "New" template. */
    isNew: boolean;
}

function blankDraft(): DraftState {
    return {
        id: crypto.randomUUID(),
        name: "",
        subject: "",
        correlationId: "",
        contentType: "application/json",
        body: "",
        properties: [{ key: "", value: "" }],
        createdAt: new Date().toISOString(),
        isNew: true,
    };
}

function draftFrom(t: SbMessageTemplate): DraftState {
    const props = Object.entries(t.properties ?? {});
    return {
        id: t.id,
        name: t.name,
        subject: t.subject ?? "",
        correlationId: t.correlationId ?? "",
        contentType: t.contentType ?? "application/json",
        // Minified JSON opens as one unreadable line — reindent for editing.
        // Only object/array roots are touched, so the payload is never altered.
        body: tryReindentJson(t.body) ?? t.body,
        properties:
            props.length > 0
                ? props.map(([key, value]) => ({ key, value }))
                : [{ key: "", value: "" }],
        createdAt: t.createdAt,
        isNew: false,
    };
}

/**
 * Full template management surface: list + search on the left, editor on the
 * right. Replaces the read-only picker — rename/edit/duplicate ride on the
 * existing upsert (`SaveMessageTemplate` replaces by id), so no new endpoints.
 */
export function TemplateManager({ onPick, onUseInComposer, onClose }: Props) {
    const { data: templates, isLoading } = useSbTemplates();
    const saveMutation = useSbSaveTemplate();
    const deleteMutation = useSbDeleteTemplate();
    const { notify } = useNotification();

    const pickMode = !!onPick;
    const [search, setSearch] = useState("");
    const [draft, setDraft] = useState<DraftState | null>(null);
    const [pendingDelete, setPendingDelete] =
        useState<SbMessageTemplate | null>(null);
    const [draftError, setDraftError] = useState<string | null>(null);

    const filtered = useMemo(
        () =>
            (templates ?? []).filter((t) =>
                t.name.toLowerCase().includes(search.toLowerCase()),
            ),
        [templates, search],
    );

    const updateProp = (index: number, field: "key" | "value", value: string) =>
        setDraft(
            (d) =>
                d && {
                    ...d,
                    properties: d.properties.map((p, i) =>
                        i === index ? { ...p, [field]: value } : p,
                    ),
                },
        );

    const saveDraft = () => {
        if (!draft) return;
        if (!draft.name.trim()) {
            setDraftError("Name the template first");
            return;
        }
        const properties: Record<string, string> = {};
        for (const p of draft.properties) {
            if (p.key.trim()) properties[p.key.trim()] = p.value;
        }
        const template: SbMessageTemplate = {
            id: draft.id,
            name: draft.name.trim(),
            body: draft.body,
            contentType: draft.contentType || null,
            subject: draft.subject || null,
            correlationId: draft.correlationId || null,
            properties,
            createdAt: draft.createdAt,
        };
        saveMutation.mutate(template, {
            onSuccess: () => {
                notify("success", `Template "${template.name}" saved`);
                setDraftError(null);
                setDraft((d) => (d ? { ...d, isNew: false } : d));
            },
        });
    };

    const duplicate = (t: SbMessageTemplate) => {
        saveMutation.mutate(
            {
                ...t,
                id: crypto.randomUUID(),
                name: `${t.name} (copy)`,
                createdAt: new Date().toISOString(),
            },
            { onSuccess: () => notify("success", `Duplicated "${t.name}"`) },
        );
    };

    const formatDraftBody = () => {
        if (!draft) return;
        const pretty = tryReindentJson(draft.body);
        if (pretty == null) {
            notify("error", "Body is not valid JSON");
            return;
        }
        if (pretty !== draft.body) setDraft({ ...draft, body: pretty });
    };

    const listContent = (
        <>
            <div className="border-b px-4 py-2">
                <input
                    type="text"
                    data-testid="template-search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Search templates..."
                    className="w-full rounded-md border bg-background px-3 py-1.5 text-sm"
                />
            </div>
            <div className="flex-1 overflow-auto">
                {isLoading ? (
                    <div className="p-4 text-sm text-muted-foreground">
                        Loading templates...
                    </div>
                ) : filtered.length === 0 ? (
                    <div
                        className="p-4 text-sm text-muted-foreground"
                        data-testid="template-picker-empty"
                    >
                        {templates && templates.length === 0
                            ? "No templates saved yet. Create one here or use 'Save as Template' from a message."
                            : "No templates match your search."}
                    </div>
                ) : (
                    filtered.map((template) => (
                        <div
                            key={template.id}
                            data-testid={`template-item-${template.id}`}
                            className={`group flex items-center gap-3 border-b px-4 py-2.5 hover:bg-accent ${
                                draft?.id === template.id ? "bg-accent" : ""
                            }`}
                        >
                            <button
                                onClick={() =>
                                    pickMode
                                        ? onPick(template)
                                        : setDraft(draftFrom(template))
                                }
                                className="min-w-0 flex-1 text-left"
                                data-testid={`template-select-${template.id}`}
                                title={
                                    pickMode
                                        ? "Apply this template"
                                        : "Edit this template"
                                }
                            >
                                <div
                                    className="truncate text-sm font-medium"
                                    title={template.name}
                                >
                                    {template.name}
                                </div>
                                {template.subject && (
                                    <div
                                        className="truncate text-xs text-muted-foreground"
                                        title={template.subject}
                                    >
                                        {template.subject}
                                    </div>
                                )}
                                <div className="mt-0.5 truncate text-xs text-muted-foreground">
                                    {template.contentType ?? "application/json"}
                                    {Object.keys(template.properties ?? {})
                                        .length > 0 && (
                                        <span>
                                            {" "}
                                            ·{" "}
                                            {
                                                Object.keys(template.properties)
                                                    .length
                                            }{" "}
                                            properties
                                        </span>
                                    )}
                                </div>
                            </button>
                            {!pickMode && (
                                <>
                                    <button
                                        onClick={() =>
                                            setDraft(draftFrom(template))
                                        }
                                        className="rounded p-1 text-muted-foreground opacity-0 hover:bg-accent hover:text-foreground group-hover:opacity-100 focus-visible:opacity-100"
                                        title="Edit template"
                                        data-testid={`template-edit-${template.id}`}
                                    >
                                        <Pencil className="h-3.5 w-3.5" />
                                    </button>
                                    <button
                                        onClick={() => duplicate(template)}
                                        className="rounded p-1 text-muted-foreground opacity-0 hover:bg-accent hover:text-foreground group-hover:opacity-100 focus-visible:opacity-100"
                                        title="Duplicate template"
                                        data-testid={`template-duplicate-${template.id}`}
                                    >
                                        <Copy className="h-3.5 w-3.5" />
                                    </button>
                                </>
                            )}
                            <button
                                onClick={() => setPendingDelete(template)}
                                className="rounded p-1 text-muted-foreground opacity-0 hover:bg-destructive/10 hover:text-destructive group-hover:opacity-100 focus-within:opacity-100 focus-visible:opacity-100"
                                title="Delete template"
                                data-testid={`template-delete-${template.id}`}
                            >
                                <Trash2 className="h-3.5 w-3.5" />
                            </button>
                        </div>
                    ))
                )}
            </div>
        </>
    );

    const editor = draft && !pickMode && (
        <div
            className="flex min-w-0 flex-1 flex-col"
            data-testid="template-editor"
        >
            <div className="flex items-center justify-between gap-2 border-b px-4 py-2">
                <span
                    className="min-w-0 truncate text-xs font-medium text-muted-foreground"
                    title={draft.isNew ? undefined : draft.name || undefined}
                >
                    {draft.isNew
                        ? "New template"
                        : `Editing: ${draft.name || "untitled"}`}
                </span>
                <div className="flex items-center gap-2">
                    {onUseInComposer && !draft.isNew && (
                        <button
                            onClick={() =>
                                onUseInComposer({
                                    id: draft.id,
                                    name: draft.name,
                                    body: draft.body,
                                    contentType: draft.contentType || null,
                                    subject: draft.subject || null,
                                    correlationId: draft.correlationId || null,
                                    properties: Object.fromEntries(
                                        draft.properties
                                            .filter((p) => p.key.trim())
                                            .map((p) => [
                                                p.key.trim(),
                                                p.value,
                                            ]),
                                    ),
                                    createdAt: draft.createdAt,
                                })
                            }
                            className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
                            data-testid="template-use-in-composer"
                        >
                            <Send className="h-3 w-3" /> Use in composer
                        </button>
                    )}
                    <button
                        onClick={saveDraft}
                        disabled={saveMutation.isPending}
                        title={
                            saveMutation.isPending
                                ? "Saving…"
                                : !draft.name.trim()
                                  ? "Name the template first"
                                  : undefined
                        }
                        className="flex items-center gap-1 rounded-md bg-primary px-3 py-1 text-xs text-primary-foreground hover:opacity-90 disabled:opacity-50"
                        data-testid="template-save"
                    >
                        <Save className="h-3 w-3" /> Save
                    </button>
                </div>
            </div>

            <div className="flex min-h-0 flex-1 flex-col p-4">
                {draftError && (
                    <div
                        className="mb-3 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive"
                        data-testid="template-editor-error"
                    >
                        {draftError}
                    </div>
                )}
                {/* Metadata scrolls in its own column so the body editor gets
                    the full dialog height instead of a cramped strip. */}
                <div className="grid min-h-0 flex-1 grid-cols-[minmax(0,2fr)_minmax(0,3fr)] gap-4">
                    <div className="min-h-0 space-y-3 overflow-y-auto pr-1">
                        <div>
                            <label className="mb-1 block text-xs font-medium text-muted-foreground">
                                Name
                            </label>
                            <input
                                type="text"
                                data-testid="template-edit-name"
                                value={draft.name}
                                onChange={(e) =>
                                    setDraft({ ...draft, name: e.target.value })
                                }
                                placeholder="Template name"
                                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
                            />
                        </div>
                        <div>
                            <label className="mb-1 block text-xs font-medium text-muted-foreground">
                                Subject
                            </label>
                            <input
                                type="text"
                                data-testid="template-edit-subject"
                                value={draft.subject}
                                onChange={(e) =>
                                    setDraft({
                                        ...draft,
                                        subject: e.target.value,
                                    })
                                }
                                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
                            />
                        </div>
                        <div>
                            <label className="mb-1 block text-xs font-medium text-muted-foreground">
                                Correlation ID
                            </label>
                            <input
                                type="text"
                                data-testid="template-edit-correlation-id"
                                value={draft.correlationId}
                                onChange={(e) =>
                                    setDraft({
                                        ...draft,
                                        correlationId: e.target.value,
                                    })
                                }
                                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
                            />
                        </div>
                        <div>
                            <label className="mb-1 block text-xs font-medium text-muted-foreground">
                                Content Type
                            </label>
                            <input
                                type="text"
                                data-testid="template-edit-content-type"
                                value={draft.contentType}
                                onChange={(e) =>
                                    setDraft({
                                        ...draft,
                                        contentType: e.target.value,
                                    })
                                }
                                className="w-full rounded-md border bg-background px-2 py-1.5 text-sm"
                            />
                        </div>
                        <div>
                            <div className="mb-1 flex items-center justify-between">
                                <label className="text-xs font-medium text-muted-foreground">
                                    Application Properties
                                </label>
                                <button
                                    type="button"
                                    onClick={() =>
                                        setDraft({
                                            ...draft,
                                            properties: [
                                                ...draft.properties,
                                                { key: "", value: "" },
                                            ],
                                        })
                                    }
                                    className="text-xs text-primary hover:underline"
                                    data-testid="template-add-property"
                                >
                                    + Add Property
                                </button>
                            </div>
                            <div className="space-y-1">
                                {draft.properties.map((prop, i) => (
                                    <div
                                        key={i}
                                        className="flex items-center gap-1.5"
                                        data-testid={`template-property-row-${i}`}
                                    >
                                        <input
                                            type="text"
                                            value={prop.key}
                                            onChange={(e) =>
                                                updateProp(
                                                    i,
                                                    "key",
                                                    e.target.value,
                                                )
                                            }
                                            placeholder="Key"
                                            className="w-32 rounded border bg-background px-2 py-1 text-xs"
                                            data-testid={`template-property-key-${i}`}
                                        />
                                        <input
                                            type="text"
                                            value={prop.value}
                                            onChange={(e) =>
                                                updateProp(
                                                    i,
                                                    "value",
                                                    e.target.value,
                                                )
                                            }
                                            placeholder="Value"
                                            className="flex-1 rounded border bg-background px-2 py-1 text-xs"
                                            data-testid={`template-property-value-${i}`}
                                        />
                                        <button
                                            type="button"
                                            onClick={() =>
                                                setDraft({
                                                    ...draft,
                                                    properties:
                                                        draft.properties.filter(
                                                            (_, j) => j !== i,
                                                        ),
                                                })
                                            }
                                            className="rounded px-1.5 py-0.5 text-xs text-muted-foreground hover:bg-accent"
                                            data-testid={`template-property-remove-${i}`}
                                        >
                                            ✕
                                        </button>
                                    </div>
                                ))}
                            </div>
                        </div>
                    </div>
                    <div className="flex min-h-0 flex-col">
                        <div className="mb-1 flex items-center justify-between">
                            <label className="text-xs font-medium text-muted-foreground">
                                Body
                            </label>
                            <button
                                type="button"
                                onClick={formatDraftBody}
                                data-testid="template-format-json"
                                className="text-xs text-primary hover:underline"
                            >
                                Format JSON
                            </button>
                        </div>
                        <MessageBodyEditor
                            value={draft.body}
                            contentType={draft.contentType}
                            onChange={(v) => setDraft({ ...draft, body: v })}
                            mirrorTestId="template-edit-body"
                            containerTestId="template-edit-body-codemirror"
                        />
                    </div>
                </div>
            </div>
        </div>
    );

    const confirmBar = pendingDelete && (
        <ConfirmBar
            message={
                <>
                    Delete template <strong>{pendingDelete.name}</strong>? This
                    cannot be undone.
                </>
            }
            confirmLabel="Delete"
            confirmDisabled={deleteMutation.isPending}
            onConfirm={() => {
                deleteMutation.mutate(pendingDelete.id);
                if (draft?.id === pendingDelete.id) setDraft(null);
                setPendingDelete(null);
            }}
            onCancel={() => setPendingDelete(null)}
            testId="template-delete-confirm"
        />
    );

    if (pickMode) {
        // Compact picker — the same list the composer used to get, with delete
        // confirm kept. Editing lives in manage mode.
        return (
            <Dialog
                onClose={onClose}
                label="Message Templates"
                testId="template-picker"
                widthClassName="flex max-h-[70vh] w-[min(560px,92vw)] flex-col"
            >
                <div className="flex items-center justify-between border-b px-4 py-3">
                    <div className="flex items-center gap-2">
                        <FileText className="h-4 w-4 text-primary" />
                        <h2 className="text-sm font-semibold">
                            Message Templates
                        </h2>
                    </div>
                    <button
                        onClick={onClose}
                        className="text-muted-foreground hover:text-foreground"
                        data-testid="template-picker-close"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>
                {listContent}
                {confirmBar}
            </Dialog>
        );
    }

    return (
        <Dialog
            onClose={onClose}
            label="Message Templates"
            testId="template-manager"
            widthClassName="flex h-[92vh] w-[min(1400px,96vw)] flex-col"
        >
            <div className="flex items-center justify-between border-b px-4 py-3">
                <div className="flex items-center gap-2">
                    <FileText className="h-4 w-4 text-primary" />
                    <h2 className="text-sm font-semibold">Message Templates</h2>
                </div>
                <div className="flex items-center gap-2">
                    <button
                        onClick={() => {
                            setDraft(blankDraft());
                            setDraftError(null);
                        }}
                        className="flex items-center gap-1 rounded-md border px-2 py-1 text-xs hover:bg-accent"
                        data-testid="template-new"
                    >
                        <Plus className="h-3 w-3" /> New
                    </button>
                    <button
                        onClick={onClose}
                        className="text-muted-foreground hover:text-foreground"
                        data-testid="template-manager-close"
                    >
                        <X className="h-4 w-4" />
                    </button>
                </div>
            </div>
            {confirmBar}
            <div className="flex min-h-0 flex-1">
                <div className="flex w-80 shrink-0 flex-col border-r">
                    {listContent}
                </div>
                {editor ?? (
                    <div
                        className="flex flex-1 items-center justify-center text-sm text-muted-foreground"
                        data-testid="template-editor-empty"
                    >
                        Select a template to edit, or create a new one
                    </div>
                )}
            </div>
        </Dialog>
    );
}
