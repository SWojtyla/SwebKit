import type { CollectionVariable, ApiEnvironment, EnvironmentVariable } from "./types";

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
