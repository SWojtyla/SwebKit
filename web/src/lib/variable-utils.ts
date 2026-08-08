import type { CollectionVariable, ApiEnvironment, EnvironmentVariable, VariableGeneratorDefinition } from "./types";

export function environmentVariableToListItem(v: EnvironmentVariable, id?: string): VariableListItem {
  let mode: VariableMode = "plain";
  if (v.secretSource === "Generated") mode = "generated";
  else if (v.secretSource === "WindowsCredentialStore") mode = "credential";
  else if (v.secretSource === "AzureKeyVault") mode = "keyvault";

  return {
    id: id ?? crypto.randomUUID(),
    key: v.key,
    isEnabled: v.isEnabled,
    mode,
    value: v.value,
    credentialKey: v.credentialKey,
    keyVaultName: v.keyVaultName,
    generator: v.generator,
  };
}

export function listItemToEnvironmentVariable(v: VariableListItem): EnvironmentVariable {
  let secretSource: EnvironmentVariable["secretSource"] = "Plain";
  if (v.mode === "generated") secretSource = "Generated";
  else if (v.mode === "credential") secretSource = "WindowsCredentialStore";
  else if (v.mode === "keyvault") secretSource = "AzureKeyVault";

  return {
    key: v.key,
    value: v.mode === "plain" ? v.value ?? "" : null,
    secretSource,
    generator: v.mode === "generated" ? v.generator : null,
    credentialKey: v.mode === "credential" || v.mode === "keyvault" ? (v.credentialKey ?? "") : null,
    keyVaultName: v.mode === "keyvault" ? v.keyVaultName ?? null : null,
    isEnabled: v.isEnabled,
  };
}

export function collectionVariableToListItem(v: CollectionVariable, id?: string): VariableListItem {
  return {
    id: id ?? crypto.randomUUID(),
    key: v.key,
    isEnabled: v.isEnabled,
    mode: v.generator ? "generated" : "plain",
    value: v.value,
    generator: v.generator,
  };
}

export function listItemToCollectionVariable(v: VariableListItem): CollectionVariable {
  return {
    key: v.key,
    value: v.mode === "plain" ? v.value ?? "" : null,
    generator: v.mode === "generated" ? v.generator : null,
    isEnabled: v.isEnabled,
  };
}

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

/** Returns a merged variable scope: collection variables first, then environment variables (env wins on conflict). */
export function buildVariableScope(
  collectionVariables: CollectionVariable[] = [],
  environment: ApiEnvironment | null = null,
): Record<string, string | null> {
  const scope: Record<string, string | null> = {};

  for (const v of collectionVariables) {
    if (v.isEnabled && v.key.trim()) {
      scope[v.key] = v.value ?? null;
    }
  }

  if (environment) {
    for (const v of environment.variables) {
      if (v.isEnabled && v.key.trim()) {
        scope[v.key] = resolveEnvironmentVariable(v);
      }
    }
  }

  return scope;
}

function resolveEnvironmentVariable(v: EnvironmentVariable): string | null {
  if (v.secretSource === "Plain") return v.value ?? null;
  // Credential store / key vault / generated values are not resolved in the UI preview
  return null;
}

const TokenPattern = /\{\{([^{}]+?)\}\}/g;

/** Substitutes {{variable}} tokens in the given text using the provided scope. */
export function substituteVariables(text: string, scope: Record<string, string | null>): string {
  if (!text || !text.includes("{{")) return text;
  return text.replace(TokenPattern, (_, name) => {
    const key = name.trim();
    const value = scope[key];
    return value != null ? value : `{{${key}}}`;
  });
}

/** Returns a preview map of token -> resolved (or null if unresolved) value for display. */
export function previewVariables(text: string, scope: Record<string, string | null>): Record<string, string | null> {
  if (!text || !text.includes("{{")) return {};
  const result: Record<string, string | null> = {};
  let match: RegExpExecArray | null;
  const regex = new RegExp(TokenPattern.source, TokenPattern.flags);
  while ((match = regex.exec(text)) !== null) {
    const key = match[1].trim();
    if (result[key] === undefined) {
      result[key] = scope[key] ?? null;
    }
  }
  return result;
}

export function isLikelySecret(key: string): boolean {
  const lower = key.toLowerCase();
  return (
    lower.includes("secret") ||
    lower.includes("password") ||
    lower.includes("passwd") ||
    lower.includes("token") ||
    lower.includes("apikey") ||
    lower.includes("api_key") ||
    lower.includes("credential") ||
    lower.includes("private")
  );
}
