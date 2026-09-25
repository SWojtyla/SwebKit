import { useEffect, useRef, useState } from "react";
import { Plus, Trash2, Wand2 } from "lucide-react";
import { GeneratorConfig } from "./GeneratorConfig";
import { previewCredential, previewKeyVaultSecret, saveCredential, deleteCredential } from "@/lib/api";
import { useNotification } from "@/components/layout/notification-context";
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
      const result = variable.mode === "credential"
        ? await previewCredential(variable.credentialKey)
        : await previewKeyVaultSecret(variable.keyVaultName ?? null, variable.credentialKey);
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
            {/* Grows with the dialog instead of the old fixed `w-32`, which
                truncated ordinary names — `AUTH_API_ADDRESS` rendered as
                `AUTH_API_ADDRE`. The floor keeps the previous width so a narrow
                dialog is no worse than before, and `title` covers the overflow
                that remains at the floor. */}
            <input
              type="text"
              value={v.key}
              onChange={(e) => updateVariable(v.id, { key: e.target.value })}
              placeholder="Key"
              title={v.key || undefined}
              className="min-w-32 flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
              data-testid={`${testIdPrefix}-key-${index}`}
            />

            {/* Single-field modes share the key's row, so a variable costs one
                line rather than two — the reason only four fit on screen. Key Vault
                and generators keep a row of their own: several controls side by
                side is the horizontal overflow this layout was stacked to fix. */}
            {v.mode === "plain" && (
              <input
                type="text"
                value={v.value ?? ""}
                onChange={(e) => updateVariable(v.id, { value: e.target.value })}
                placeholder="Value"
                className="min-w-0 flex-[2] rounded border bg-background px-2 py-1 text-sm font-mono"
                data-testid={`${testIdPrefix}-value-${index}`}
              />
            )}

            {v.mode === "credential" && (
              <input
                type="text"
                value={v.credentialKey ?? ""}
                onChange={(e) => updateVariable(v.id, { credentialKey: e.target.value })}
                placeholder="Credential key"
                className="min-w-0 flex-[2] rounded border bg-background px-2 py-1 text-sm font-mono"
                data-testid={`${testIdPrefix}-value-${index}`}
              />
            )}

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

          {(v.mode === "keyvault" || v.mode === "credential" || (v.mode === "generated" && v.generator)) && (
            <div className="mt-2 flex flex-wrap items-center gap-2">
              {v.mode === "credential" && (
                <CredentialField
                  variable={v}
                  index={index}
                  onChange={(patch) => updateVariable(v.id, patch)}
                  onPreview={() => handlePreview(v)}
                  preview={previews[v.id]}
                  testIdPrefix={testIdPrefix}
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
          )}

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
        title={isLoading ? "Loading…" : !variable.credentialKey ? "Link a Key Vault credential first" : keyVaults.length === 0 ? "No Key Vaults configured" : undefined}
        className="rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
        data-testid={`${testIdPrefix}-preview-btn-${index}`}
      >
        {isLoading ? "…" : "Preview"}
      </button>
    </div>
  );
}

interface CredentialFieldProps {
  variable: VariableListItem;
  index: number;
  onChange: (patch: Partial<VariableListItem>) => void;
  onPreview: () => void;
  preview?: PreviewState;
  testIdPrefix: string;
}

const CREDENTIAL_KEY_PREFIX = "sw-secret:";

// Secret Store variables resolve through the sidecar's ICredentialStore at send time —
// the same store these helpers write to. The key stays editable so a pre-existing OS
// credential can still be referenced; the value field is the missing half that lets a
// user actually put a secret behind it (saved debounced, like the auth panel's secret).
function CredentialField({ variable, index, onChange, onPreview, preview, testIdPrefix }: CredentialFieldProps) {
  const { notify } = useNotification();
  const [secretInput, setSecretInput] = useState("");
  const [saved, setSaved] = useState(false);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inputRef = useRef(secretInput);
  useEffect(() => {
    inputRef.current = secretInput;
  }, [secretInput]);

  const key = variable.credentialKey ?? "";
  // A freshly generated key lands via onChange on the *next* render — the debounced
  // persist reads it through the ref so the first save isn't silently skipped.
  const keyRef = useRef(key);
  useEffect(() => {
    keyRef.current = key;
  }, [key]);

  async function persist() {
    const value = inputRef.current;
    const k = keyRef.current;
    if (!k) return;
    try {
      if (value === "") {
        await deleteCredential(k);
        setSaved(false);
      } else {
        await saveCredential(k, value);
        setSaved(true);
      }
    } catch (ex) {
      notify("error", "Couldn't store secret", ex instanceof Error ? ex.message : "Save failed");
    }
  }

  const handleSecretChange = (value: string) => {
    setSecretInput(value);
    // The key is what gets persisted with the environment; generate one lazily so the
    // user only ever types a secret and never has to invent a storage key themselves.
    if (!key) {
      const generated = `${CREDENTIAL_KEY_PREFIX}${crypto.randomUUID()}`;
      keyRef.current = generated;
      onChange({ credentialKey: generated });
    }
    if (saveTimer.current) clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => void persist(), 1000);
  };

  const handleBlur = () => {
    if (saveTimer.current) {
      clearTimeout(saveTimer.current);
      saveTimer.current = null;
    }
    void persist();
  };

  const isLoading = preview?.status === "loading";

  return (
    <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
      <input
        type="password"
        value={secretInput}
        onChange={(e) => handleSecretChange(e.target.value)}
        onBlur={handleBlur}
        placeholder={saved || key ? "Saved — type to replace" : "Secret value"}
        autoComplete="new-password"
        className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-sm font-mono"
        data-testid={`${testIdPrefix}-secret-${index}`}
      />
      <button
        onClick={onPreview}
        disabled={!key || isLoading}
        title={isLoading ? "Loading…" : !key ? "No credential key yet" : "Check the key exists in the credential store"}
        className="rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
        data-testid={`${testIdPrefix}-preview-btn-${index}`}
      >
        {isLoading ? "…" : "Preview"}
      </button>
      <span
        className={`text-xs ${saved ? "text-success" : "text-muted-foreground"}`}
        data-testid={`${testIdPrefix}-secret-state-${index}`}
      >
        {saved ? "Stored in credential store" : "Stored in your OS credential store under this key"}
      </span>
    </div>
  );
}
