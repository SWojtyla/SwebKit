import { useState } from "react";
import { X, Eye, EyeOff, Copy, Check } from "lucide-react";
import type { SecretInfo } from "@/lib/types";
import { apiFetch } from "@/lib/api";

interface Props {
  secret: SecretInfo;
  onClose: () => void;
}

export function SecretDetailPanel({ secret, onClose }: Props) {
  const [showValues, setShowValues] = useState<Record<string, boolean>>({});
  const [copiedKey, setCopiedKey] = useState<string | null>(null);
  // Decoded values are only ever kept in this component's local state — never
  // in react-query's cache or localStorage — and are gone once the panel
  // unmounts. Fetched lazily (only when a value is actually revealed/copied),
  // not eagerly for every secret opened.
  const [values, setValues] = useState<Record<string, string> | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const ensureValuesLoaded = async (): Promise<Record<string, string> | null> => {
    if (values) return values;
    setLoading(true);
    setLoadError(null);
    try {
      const fetched = await apiFetch<Record<string, string>>(
        `/api/aks/${secret.namespace}/secrets/${secret.name}/values`,
      );
      setValues(fetched);
      return fetched;
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : "Failed to load secret values");
      return null;
    } finally {
      setLoading(false);
    }
  };

  const toggleKey = async (key: string) => {
    if (!showValues[key]) {
      await ensureValuesLoaded();
    }
    setShowValues((prev) => ({ ...prev, [key]: !prev[key] }));
  };

  const copyValue = async (key: string) => {
    try {
      const fetched = await ensureValuesLoaded();
      const value = fetched?.[key];
      if (value === undefined) return;
      await navigator.clipboard.writeText(value);
      setCopiedKey(key);
      setTimeout(() => setCopiedKey(null), 2000);
    } catch {}
  };

  return (
    <div className="flex h-full flex-col" data-testid="secret-detail-panel">
      <div className="flex items-center justify-between border-b px-4 py-3">
        <div>
          <h2 className="text-lg font-semibold" data-testid="secret-detail-name">{secret.name}</h2>
          <p className="text-xs text-muted-foreground">{secret.namespace} · {secret.type}</p>
        </div>
        <button onClick={onClose} className="text-muted-foreground hover:text-foreground" data-testid="secret-detail-close">
          <X className="h-4 w-4" />
        </button>
      </div>
      <div className="flex-1 overflow-auto p-4">
        <div className="rounded-lg border">
          {secret.keys.map((key) => (
            <div key={key} className="border-b px-3 py-2 last:border-0" data-testid={`secret-data-${key}`}>
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium">{key}</span>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => toggleKey(key)}
                    className="rounded p-1 text-xs hover:bg-accent"
                    data-testid={`secret-toggle-${key}`}
                  >
                    {showValues[key] ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
                  </button>
                  <button
                    onClick={() => copyValue(key)}
                    className="rounded p-1 text-xs hover:bg-accent"
                    data-testid={`secret-copy-${key}`}
                  >
                    {copiedKey === key ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                  </button>
                </div>
              </div>
              <div className="mt-1 text-xs font-mono break-all text-muted-foreground" data-testid={`secret-value-${key}`}>
                {showValues[key]
                  ? (values?.[key] ?? (loading ? "Loading…" : loadError ?? "(unavailable)"))
                  : "••••••••"}
              </div>
            </div>
          ))}
          {secret.keys.length === 0 && (
            <div className="px-3 py-4 text-sm text-muted-foreground">No data keys in this secret</div>
          )}
        </div>
      </div>
    </div>
  );
}
