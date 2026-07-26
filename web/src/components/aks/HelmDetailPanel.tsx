import { useState } from "react";
import { X, History, Settings2, RotateCcw } from "lucide-react";
import { useAksHelmHistory, useAksHelmValues } from "@/lib/hooks";

interface HelmDetailPanelProps {
  ns: string;
  release: string;
  onClose: () => void;
}

export function HelmDetailPanel({ ns, release, onClose }: HelmDetailPanelProps) {
  const [tab, setTab] = useState<"history" | "values">("history");
  const { data: history, isLoading: historyLoading } = useAksHelmHistory(ns, release);
  const { data: values, isLoading: valuesLoading } = useAksHelmValues(ns, release);
  const [valuesTab, setValuesTab] = useState<"user" | "computed">("user");

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
        <button
          onClick={() => setTab("history")}
          data-testid="helm-tab-history"
          className={`flex items-center gap-1 px-4 py-2 text-sm font-medium ${
            tab === "history" ? "border-b-2 border-primary text-foreground" : "text-muted-foreground hover:text-foreground"
          }`}
        >
          <History className="h-3.5 w-3.5" /> History
        </button>
        <button
          onClick={() => setTab("values")}
          data-testid="helm-tab-values"
          className={`flex items-center gap-1 px-4 py-2 text-sm font-medium ${
            tab === "values" ? "border-b-2 border-primary text-foreground" : "text-muted-foreground hover:text-foreground"
          }`}
        >
          <Settings2 className="h-3.5 w-3.5" /> Values
        </button>
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
                    <th className="py-2 pr-4">Age</th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((h) => (
                    <tr key={h.revision} className="border-b last:border-0">
                      <td className="py-2 pr-4 font-medium">{h.revision}</td>
                      <td className="py-2 pr-4">
                        <span className={h.status === "deployed" ? "text-green-500" : "text-yellow-500"}>
                          {h.status}
                        </span>
                      </td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">{h.chart}</td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">{h.appVersion}</td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">{h.description}</td>
                      <td className="py-2 pr-4 text-xs text-muted-foreground">{h.age}</td>
                      <td className="py-2 pr-4">
                        {h.status !== "deployed" && (
                          <button
                            disabled
                            title="Coming soon — rolling back needs a sidecar POST endpoint"
                            className="flex items-center gap-1 rounded border px-2 py-0.5 text-xs opacity-50 cursor-not-allowed"
                            data-testid={`helm-rollback-rev-${h.revision}`}
                          >
                            <RotateCcw className="h-3 w-3" /> Rollback
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
                <pre className="overflow-auto rounded border bg-black p-3 text-xs font-mono text-green-400" data-testid="helm-values-content">
                  {valuesTab === "user" ? values.userValues : values.computedValues}
                </pre>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
