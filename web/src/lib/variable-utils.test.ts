import { describe, it, expect } from "vitest";
import { buildVariableScope, substituteVariables, isLikelySecret } from "./variable-utils";
import type { ApiEnvironment, CollectionVariable, EnvironmentVariable } from "./types";

function envVar(key: string, value: string, overrides: Partial<EnvironmentVariable> = {}): EnvironmentVariable {
  return {
    key,
    value,
    secretSource: "Plain",
    credentialKey: null,
    keyVaultName: null,
    isEnabled: true,
    ...overrides,
  };
}

function environment(id: string, ...variables: EnvironmentVariable[]): ApiEnvironment {
  return {
    id,
    name: id,
    collectionId: null,
    variables,
    createdAt: "",
    updatedAt: "",
  };
}

function collectionVar(key: string, value: string, isEnabled = true): CollectionVariable {
  return { key, value, generator: null, isEnabled };
}

describe("buildVariableScope", () => {
  it("takes collection variables when no environment layer defines the key", () => {
    expect(buildVariableScope([collectionVar("A", "collection")], [])).toEqual({ A: "collection" });
  });

  it("lets a later layer override an earlier one", () => {
    const scope = buildVariableScope(
      [],
      [environment("global", envVar("AUTH_SP", "shared")), environment("scoped", envVar("AUTH_SP", "project"))],
    );
    expect(scope.AUTH_SP).toBe("project");
  });

  it("fills gaps the later layer leaves from the earlier one", () => {
    // The point of a global layer: a value shared by a family of environments is
    // defined once rather than copied into each of them.
    const scope = buildVariableScope(
      [],
      [
        environment("global", envVar("AUTH_SP", "shared"), envVar("TIMEOUT", "30")),
        environment("scoped", envVar("AUTH_SP", "project")),
      ],
    );
    expect(scope).toEqual({ AUTH_SP: "project", TIMEOUT: "30" });
  });

  it("lets every environment layer override a collection variable", () => {
    expect(buildVariableScope([collectionVar("A", "collection")], [environment("g", envVar("A", "global"))]).A)
      .toBe("global");
    expect(buildVariableScope([collectionVar("A", "collection")], [null, environment("s", envVar("A", "scoped"))]).A)
      .toBe("scoped");
  });

  it("skips null layers so a caller need not filter an empty slot", () => {
    const scope = buildVariableScope([], [null, environment("s", envVar("A", "1")), undefined]);
    expect(scope).toEqual({ A: "1" });
  });

  it("excludes disabled variables from either source", () => {
    const scope = buildVariableScope(
      [collectionVar("A", "collection", false)],
      [environment("s", envVar("B", "x", { isEnabled: false }))],
    );
    expect(scope).toEqual({});
  });

  it("excludes a variable whose key is only whitespace", () => {
    expect(buildVariableScope([collectionVar("   ", "x")], [])).toEqual({});
  });

  it("maps a value only known at send time to null rather than dropping it", () => {
    // null means deferred, not missing — the difference between an amber token and
    // a red one in the editor.
    const scope = buildVariableScope(
      [],
      [environment("s", envVar("SECRET", "", { secretSource: "AzureKeyVault", credentialKey: "k" }))],
    );
    expect(scope).toEqual({ SECRET: null });
    expect("SECRET" in scope).toBe(true);
  });

  it("lets a later plain value replace an earlier deferred one", () => {
    const scope = buildVariableScope(
      [],
      [
        environment("global", envVar("API_KEY", "", { secretSource: "AzureKeyVault", credentialKey: "k" })),
        environment("scoped", envVar("API_KEY", "plain")),
      ],
    );
    expect(scope.API_KEY).toBe("plain");
  });
});

describe("substituteVariables", () => {
  const scope = { A: "1", DEFERRED: null };

  it("leaves an undefined token as its literal text", () => {
    // This is why an undefined variable reaches the server as `{{NOPE}}` and comes
    // back a 400 — the substitution is deliberately non-destructive.
    expect(substituteVariables("{{NOPE}}", scope)).toBe("{{NOPE}}");
  });

  it("leaves a deferred token alone as well", () => {
    expect(substituteVariables("{{DEFERRED}}", scope)).toBe("{{DEFERRED}}");
  });

  it("replaces every occurrence and tolerates padding", () => {
    expect(substituteVariables("{{A}}-{{ A }}", scope)).toBe("1-1");
  });

  it("returns text with no tokens untouched", () => {
    expect(substituteVariables("plain", scope)).toBe("plain");
  });
});

describe("isLikelySecret", () => {
  it("matches on a substring, case-insensitively", () => {
    expect(isLikelySecret("AUTH_TOKEN")).toBe(true);
    expect(isLikelySecret("myPassword")).toBe(true);
    expect(isLikelySecret("AUTH_OFFICE_ID")).toBe(false);
  });
});
