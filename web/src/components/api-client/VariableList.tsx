import { useState } from "react";
import { Plus, Trash2, Wand2 } from "lucide-react";
import { GeneratorConfig } from "./GeneratorConfig";
import { previewKeyVaultSecret } from "@/lib/api";
import { useNotification } from "@/components/layout/NotificationSystem";
import type { VariableGeneratorDefinition, KeyVaultEntry } from "@/lib/types";

export type VariableMode = "plain" | "generated" | "credential" | "keyvault";

export interface VariableListItem {
  id: string;
  key: string;
  isEnabled: boolean;
  mode: VariableMode;
  value?: string | null;
  credentialKey?: string | null;
  keyVaultName?: string | null;
  generator?: VariableGeneratorDefinition | null;
}

interface VariableListProps {
  variables: VariableListItem[];
  keyVaults: KeyVaultEntry[];
  onChange: (variables: VariableListItem[]) => void;
  supportsKeyVault?: boolean;
  supportsCredentialStore?: boolean;
  emptyMessage?: string;
  testIdPrefix: string;
  addButtonTestId?: string;
}

interface PreviewState {
  status: "loading" | "ok" | "error";
  message: string;
}

const sourceOptions: { value: string; label: string; mode: VariableMode }[] = [
  { value: "Plain", label: "Value", mode: "plain" },
  { value: "Generated", label: "Generated", mode: "generated" },
  { value: "WindowsCredentialStore", label: "Secret Store", mode: "credential" },
  { value: "AzureKeyVault", label: "Key Vault", mode: "keyvault" },
];

function newVariable(): VariableListItem {
  return {
    id: crypto.randomUUID(),
    key: "",
    isEnabled: true,
    mode: "plain",
    value: "",
  };
}

export function VariableList({
  variables,
  keyVaults,
  onChange,
  supportsKeyVault,
  supportsCredentialStore,
  emptyMessage,
  testIdPrefix,
  addButtonTestId,
}: VariableListProps) {
  const { notify } = useNotification();
  const [previews, setPreviews] = useState<Record<string, PreviewState>>({});

  const updateVariable = (id: string, patch: Partial<VariableListItem>) => {
    onChange(variables.map((v) => (v.id === id ? { ...v, ...patch } : v)));
  };

  const applyMode = (id: string, mode: VariableMode) => {
    const variable = variables.find((v) => v.id === id);
    if (!variable) return;

    const patch: Partial<VariableListItem> = { mode };
    if (mode === "plain") {
      patch.value = "";
      patch.credentialKey = null;
      patch.keyVaultName = null;
      patch.generator = null;
    } else if (mode === "generated") {
      patch.value = null;
      patch.credentialKey = null;
      patch.keyVaultName = null;
      patch.generator = { kind: "Guid" };
    } else if (mode === "credential") {
      patch.value = null;
      patch.credentialKey = "";
      patch.keyVaultName = null;
      patch.generator = null;
    } else if (mode === "keyvault") {
      patch.value = null;
      patch.credentialKey = "";
      patch.keyVaultName = null;
      patch.generator = null;
    }
    updateVariable(id, patch);
  };

  const setMode = (id: string, value: string) => {
    const option = sourceOptions.find((o) => o.value === value);
    if (!option) return;
    applyMode(id, option.mode);
  };

  const handlePreview = async (variable: VariableListItem) => {
    if (!variable.credentialKey) return;
    setPreviews((prev) => ({ ...prev, [variable.id]: { status: "loading", message: "Checking…" } }));
    try {
      const result = await previewKeyVaultSecret(variable.keyVaultName ?? null, variable.credentialKey);
      setPreviews((prev) => ({
        ...prev,
        [variable.id]: {
          status: result.status === "ok" ? "ok" : "error",
          message: result.status === "ok" ? `Present ${result.maskedValue ?? ""}` : result.error ?? "Not found",
        },
      }));
    } catch (ex) {
      const message = ex instanceof Error ? ex.message : "Preview failed";
      setPreviews((prev) => ({ ...prev, [variable.id]: { status: "error", message } }));
      notify("error", "Key Vault preview failed", message);
    }
  };

  const addVariable = () => onChange([...variables, newVariable()]);
  const removeVariable = (id: string) => {
    onChange(variables.filter((v) => v.id !== id));
    setPreviews((prev) => {
      const next = { ...prev };
      delete next[id];
      return next;
    });
  };

  const availableOptions = sourceOptions.filter((o) => {
    if (o.mode === "credential" && !supportsCredentialStore) return false;
    if (o.mode === "keyvault" && !supportsKeyVault) return false;
    return true;
  });

  const optionValueForMode = (mode: VariableMode) =>
    sourceOptions.find((o) => o.mode === mode)?.value ?? mode;

  return (
    <div className="space-y-2" data-testid={`${testIdPrefix}-list`}>
      {variables.length === 0 && (
        <div className="rounded border p-3 text-xs text-muted-foreground" data-testid={`${testIdPrefix}-empty`}>
          {emptyMessage ?? "No variables defined."}
        </div>
      )}
      {variables.map((v, index) => (
        <div
          key={v.id}
          className="rounded border p-2"
          data-testid={`${testIdPrefix}-row-${index}`}
        >
          <div className="flex items-center gap-2">
            <input
              type="checkbox"
              checked={v.isEnabled}
              onChange={(e) => updateVariable(v.id, { isEnabled: e.target.checked })}
              data-testid={`${testIdPrefix}-enabled-${index}`}
            />
            <input
              type="text"
              value={v.key}
              onChange={(e) => updateVariable(v.id, { key: e.target.value })}
              placeholder="Key"
              className="w-32 rounded border bg-background px-2 py-1 text-sm font-mono"
              data-testid={`${testIdPrefix}-key-${index}`}
            />
            <select
              value={optionValueForMode(v.mode)}
              onChange={(e) => setMode(v.id, e.target.value)}
              className="rounded border bg-background px-2 py-1 text-xs"
              data-testid={`${testIdPrefix}-source-${index}`}
            >
              {availableOptions.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
            <button
              className="ml-auto p-1 text-destructive"
              onClick={() => removeVariable(v.id)}
              data-testid={`${testIdPrefix}-remove-${index}`}
            >
              <Trash2 className="h-3 w-3" />
            </button>
          </div>

          <div className="mt-2 flex flex-wrap items-center gap-2">
            {v.mode === "plain" && (
              <input
                type="text"
                value={v.value ?? ""}
                onChange={(e) => updateVariable(v.id, { value: e.target.value })}
                placeholder="Value"
                className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
                data-testid={`${testIdPrefix}-value-${index}`}
              />
            )}

            {v.mode === "credential" && (
              <input
                type="text"
                value={v.credentialKey ?? ""}
                onChange={(e) => updateVariable(v.id, { credentialKey: e.target.value })}
                placeholder="Credential key"
                className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
                data-testid={`${testIdPrefix}-value-${index}`}
              />
            )}

            {v.mode === "keyvault" && (
              <KeyVaultField
                variable={v}
                index={index}
                keyVaults={keyVaults}
                onChange={(patch) => updateVariable(v.id, patch)}
                onPreview={() => handlePreview(v)}
                preview={previews[v.id]}
                testIdPrefix={testIdPrefix}
              />
            )}

            {v.mode === "generated" && v.generator && (
              <GeneratorConfig
                generator={v.generator}
                onChange={(generator) => updateVariable(v.id, { generator })}
                testIdPrefix={`${testIdPrefix}-${index}`}
              />
            )}
          </div>

          {v.mode === "generated" && (
            <div className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
              <Wand2 className="h-3 w-3" />
              <span data-testid={`${testIdPrefix}-generated-hint-${index}`}>Value is generated on each request</span>
            </div>
          )}

          {previews[v.id] && (
            <div
              className={`mt-1 text-xs ${
                previews[v.id].status === "ok"
                  ? "text-success"
                  : previews[v.id].status === "error"
                    ? "text-destructive"
                    : "text-muted-foreground"
              }`}
              data-testid={`${testIdPrefix}-preview-${index}`}
            >
              {previews[v.id].message}
            </div>
          )}
        </div>
      ))}

      <button
        onClick={addVariable}
        className="text-xs text-primary hover:underline"
        data-testid={addButtonTestId ?? `${testIdPrefix}-add`}
      >
        <Plus className="inline h-3 w-3" /> Add variable
      </button>
    </div>
  );
}

interface KeyVaultFieldProps {
  variable: VariableListItem;
  index: number;
  keyVaults: KeyVaultEntry[];
  onChange: (patch: Partial<VariableListItem>) => void;
  onPreview: () => void;
  preview?: PreviewState;
  testIdPrefix: string;
}

function KeyVaultField({ variable, index, keyVaults, onChange, onPreview, preview, testIdPrefix }: KeyVaultFieldProps) {
  const isLoading = preview?.status === "loading";

  return (
    <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
      {keyVaults.length === 0 ? (
        <span className="text-xs text-muted-foreground" data-testid={`${testIdPrefix}-no-vaults-${index}`}>
          No vaults configured
        </span>
      ) : (
        <select
          value={variable.keyVaultName ?? ""}
          onChange={(e) => onChange({ keyVaultName: e.target.value || null })}
          className="w-36 rounded border bg-background px-2 py-1 text-xs"
          data-testid={`${testIdPrefix}-vault-${index}`}
        >
          <option value="">Default vault</option>
          {keyVaults.map((kv) => (
            <option key={kv.id} value={kv.name}>{kv.name}</option>
          ))}
        </select>
      )}
      <input
        type="text"
        value={variable.credentialKey ?? ""}
        onChange={(e) => onChange({ credentialKey: e.target.value })}
        placeholder="Secret name"
        className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
        data-testid={`${testIdPrefix}-value-${index}`}
      />
      <button
        onClick={onPreview}
        disabled={!variable.credentialKey || isLoading || keyVaults.length === 0}
        className="rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
        data-testid={`${testIdPrefix}-preview-btn-${index}`}
      >
        {isLoading ? "…" : "Preview"}
      </button>
    </div>
  );
}
