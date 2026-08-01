import { useState } from "react";
import { X, History, Settings2, RotateCcw, FileText, Package } from "lucide-react";
import { useAksHelmHistory, useAksHelmValues, useAksHelmNotes, useAksHelmManifest, useAksHelmRollback } from "@/lib/hooks";
import { highlightYaml } from "@/lib/yamlHighlight";

interface HelmDetailPanelProps {
  ns: string;
  release: string;
  onClose: () => void;
  onRequestConfirm: (opts: { message: string; resourceName: string; onConfirm: () => void }) => void;
  onError?: (message: string) => void;
}

type HelmTab = "history" | "values" | "notes" | "manifest";

export function HelmDetailPanel({ ns, release, onClose, onRequestConfirm, onError }: HelmDetailPanelProps) {
  const [tab, setTab] = useState<HelmTab>("history");
  const { data: history, isLoading: historyLoading } = useAksHelmHistory(ns, release);
  const { data: values, isLoading: valuesLoading } = useAksHelmValues(ns, release);
  const { data: notes, isLoading: notesLoading } = useAksHelmNotes(ns, release);
  const { data: manifest, isLoading: manifestLoading } = useAksHelmManifest(ns, release);
  const [valuesTab, setValuesTab] = useState<"user" | "computed">("user");
  const rollback = useAksHelmRollback();

  const requestRollback = (targetRevision: number) => {
    onRequestConfirm({
      message: `Rollback "${release}" to revision ${targetRevision}? This will re-deploy that revision's chart and values.`,
      resourceName: release,
      onConfirm: () => {
        rollback.mutate(
          { ns, release, targetRevision },
          { onError: (err) => onError?.(err instanceof Error ? err.message : String(err)) },
        );
      },
    });
  };

  const tabButton = (id: HelmTab, label: string, icon: React.ReactNode) => (
    <button
      key={id}
      onClick={() => setTab(id)}
      data-testid={`helm-tab-${id}`}
      className={`flex items-center gap-1 px-4 py-2 text-sm font-medium ${
        tab === id ? "border-b-2 border-primary text-foreground" : "text-muted-foreground hover:text-foreground"
      }`}
    >
      {icon} {label}
    </button>
  );

  return (
    <div className="flex h-full flex-col" data-testid="helm-detail-panel">
      <div className="flex items-center gap-2 border-b px-4 py-2">
        <span className="text-sm font-medium">{release}</span>
        <span className="text-xs text-muted-foreground">Helm Release</span>
        <button onClick={onClose} className="ml-auto rounded p-1 hover:bg-accent">
          <X className="h-4 w-4" />
        </button>
      </div>

      <div className="flex border-b">
        {tabButton("history", "History", <History className="h-3.5 w-3.5" />)}
        {tabButton("values", "Values", <Settings2 className="h-3.5 w-3.5" />)}
        {tabButton("notes", "Notes", <FileText className="h-3.5 w-3.5" />)}
        {tabButton("manifest", "Manifest", <Package className="h-3.5 w-3.5" />)}
      </div>

      <div className="flex-1 overflow-auto">
        {tab === "history" && (
          <div className="p-4">
            {historyLoading ? (
              <div className="text-sm text-muted-foreground">Loading history...</div>
            ) : !history || history.length === 0 ? (
              <div className="text-sm text-muted-foreground">No history available</div>
            ) : (
              <table className="w-full text-sm" data-testid="helm-history-table">
                <thead>
                  <tr className="border-b text-left text-xs text-muted-foreground">
                    <th className="py-2 pr-4">Revision</th>
                    <th className="py-2 pr-4">Status</th>
                    <th className="py-2 pr-4">Chart</th>
                    <th className="py-2 pr-4">App Version</th>
                    <th className="py-2 pr-4">Description</th>
                    <th className="py-2 pr-4">Updated</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((h) => (
                    <tr key={h.revision} className="border-b last:border-0">
                      <td className="py-2 pr-4 font-medium">{h.revision}</td>
                      <td className="py-2 pr-4">
                        <span className={h.status === "deployed" ? "text-success" : "text-warning"}>
                          {h.status}
                        </span>
                      </td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">{h.chart}</td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">{h.appVersion}</td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">{h.description}</td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">
                        {h.updated ? new Date(h.updated).toLocaleDateString() : "—"}
                      </td>
                      <td className="py-2 pr-4">
                        {h.status !== "deployed" && (
                          <button
                            onClick={() => requestRollback(h.revision)}
                            disabled={rollback.isPending}
                            title={`Rollback to revision ${h.revision}`}
                            className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs hover:bg-accent disabled:cursor-not-allowed disabled:opacity-50"
                            data-testid={`helm-rollback-rev-${h.revision}`}
                          >
                            <RotateCcw className="h-3 w-3" />
                            {rollback.isPending && rollback.variables?.targetRevision === h.revision ? "Rolling back…" : "Rollback"}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
        {tab === "values" && (
          <div className="p-4">
            {valuesLoading ? (
              <div className="text-sm text-muted-foreground">Loading values...</div>
            ) : !values ? (
              <div className="text-sm text-muted-foreground">No values available</div>
            ) : (
              <div>
                <div className="mb-2 flex gap-2">
                  <button
                    onClick={() => setValuesTab("user")}
                    className={`rounded border px-3 py-1 text-xs ${valuesTab === "user" ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                  >
                    User Values
                  </button>
                  <button
                    onClick={() => setValuesTab("computed")}
                    className={`rounded border px-3 py-1 text-xs ${valuesTab === "computed" ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
                  >
                    Computed Values
                  </button>
                </div>
                <pre
                  className="overflow-auto rounded border bg-card p-3 text-xs font-mono"
                  data-testid="helm-values-content"
                  dangerouslySetInnerHTML={{
                    __html: highlightYaml(valuesTab === "user" ? values.userValues : values.computedValues),
                  }}
                />
              </div>
            )}
          </div>
        )}
        {tab === "notes" && (
          <div className="p-4">
            {notesLoading ? (
              <div className="text-sm text-muted-foreground">Loading notes...</div>
            ) : !notes ? (
              <div className="text-sm text-muted-foreground">No notes available</div>
            ) : (
              <pre className="overflow-auto rounded border bg-card p-3 text-xs font-mono whitespace-pre-wrap" data-testid="helm-notes-content">
                {notes.notes || "No release notes provided."}
              </pre>
            )}
          </div>
        )}
        {tab === "manifest" && (
          <div className="p-4">
            {manifestLoading ? (
              <div className="text-sm text-muted-foreground">Loading manifest...</div>
            ) : !manifest ? (
              <div className="text-sm text-muted-foreground">No manifest available</div>
            ) : (
              <pre
                className="overflow-auto rounded border bg-card p-3 text-xs font-mono"
                data-testid="helm-manifest-content"
                dangerouslySetInnerHTML={{
                  __html: highlightYaml(manifest.manifest),
                }}
              />
            )}
          </div>
        )}
      </div>
    </div>
  );
}
