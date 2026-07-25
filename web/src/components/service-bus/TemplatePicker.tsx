import { useState } from "react";
import { X, Trash2, FileText } from "lucide-react";
import { useSbTemplates, useSbDeleteTemplate } from "@/lib/hooks";
import type { SbMessageTemplate } from "@/lib/types";

interface Props {
  onSelect: (template: SbMessageTemplate) => void;
  onClose: () => void;
}

export function TemplatePicker({ onSelect, onClose }: Props) {
  const { data: templates, isLoading } = useSbTemplates();
  const deleteMutation = useSbDeleteTemplate();
  const [search, setSearch] = useState("");

  const filtered = (templates ?? []).filter((t) =>
    t.name.toLowerCase().includes(search.toLowerCase()),
  );

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" data-testid="template-picker-overlay">
      <div className="flex max-h-[70vh] w-[500px] flex-col rounded-lg border bg-card shadow-lg" data-testid="template-picker">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div className="flex items-center gap-2">
            <FileText className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-semibold">Message Templates</h2>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="template-picker-close">
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* Search */}
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

        {/* List */}
        <div className="flex-1 overflow-auto">
          {isLoading ? (
            <div className="p-4 text-sm text-muted-foreground">Loading templates...</div>
          ) : filtered.length === 0 ? (
            <div className="p-4 text-sm text-muted-foreground" data-testid="template-picker-empty">
              {templates && templates.length === 0 ? "No templates saved. Use 'Save as Template' from a message detail." : "No templates match your search."}
            </div>
          ) : (
            filtered.map((template) => (
              <div
                key={template.id}
                data-testid={`template-item-${template.id}`}
                className="group flex items-center gap-3 border-b px-4 py-2.5 hover:bg-accent"
              >
                <button
                  onClick={() => onSelect(template)}
                  className="flex-1 text-left"
                  data-testid={`template-select-${template.id}`}
                >
                  <div className="text-sm font-medium">{template.name}</div>
                  {template.subject && (
                    <div className="text-xs text-muted-foreground">{template.subject}</div>
                  )}
                  <div className="mt-0.5 text-xs text-muted-foreground">
                    {template.contentType ?? "application/json"}
                    {Object.keys(template.properties ?? {}).length > 0 && (
                      <span> · {Object.keys(template.properties).length} properties</span>
                    )}
                  </div>
                </button>
                <button
                  onClick={() => deleteMutation.mutate(template.id)}
                  className="rounded p-1 text-muted-foreground opacity-0 hover:bg-destructive/10 hover:text-destructive group-hover:opacity-100"
                  title="Delete template"
                  data-testid={`template-delete-${template.id}`}
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
