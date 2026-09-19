import { describe, it, expect, vi, afterEach } from "vitest";
import { apiSubpathFor } from "./stores/git-repo-preferences";
import { apiSend, ConflictError } from "./api";

/// `apiSubpathFor` scopes the Git panel's stage/commit to the linked root's
/// `.swebkit-api/` folder. The paths it compares come from different ecosystems —
/// the sidecar reports .NET-flavoured paths, the user picks through Tauri — so
/// separator and casing mismatches are the norm, not edge cases.

describe("apiSubpathFor", () => {
  it("returns the repo-relative .swebkit-api path", () => {
    expect(apiSubpathFor("D:/repos/orders-api", "D:/repos/orders-api/.swebkit-api")).toBe(
      ".swebkit-api",
    );
  });

  it("normalizes Windows backslashes and trailing separators", () => {
    expect(
      apiSubpathFor("D:\\repos\\orders-api\\", "D:\\repos\\orders-api\\.swebkit-api\\"),
    ).toBe(".swebkit-api");
  });

  it("matches case-insensitively (Windows drive paths)", () => {
    expect(apiSubpathFor("d:/Repos/Orders-Api", "D:/repos/orders-api/.swebkit-api")).toBe(
      ".swebkit-api",
    );
  });

  it("returns a nested subpath, not just the first segment", () => {
    expect(apiSubpathFor("/repo", "/repo/tools/api/.swebkit-api")).toBe("tools/api/.swebkit-api");
  });

  it("returns null when the api root is the repository root (bare-folder root)", () => {
    // No scoping applies — the whole repo is the API root.
    expect(apiSubpathFor("/repo", "/repo")).toBeNull();
    expect(apiSubpathFor("C:\\repo\\", "c:/repo")).toBeNull();
  });

  it("returns null when the api root is outside the repository", () => {
    // Must never produce a subpath that silently points at a different folder.
    expect(apiSubpathFor("/repo", "/other/.swebkit-api")).toBeNull();
    // Prefix traps: orders-api-v2 starts with orders-api but is a sibling.
    expect(apiSubpathFor("/repo/orders-api", "/repo/orders-api-v2/.swebkit-api")).toBeNull();
  });
});

/// `apiSend` turns the sidecar's 409 `{error, conflicts}` body into a
/// `ConflictError` carrying the changed file list — the conflict banner names
/// those files, so dropping them would leave the user guessing what to reload.

describe("apiSend conflict handling", () => {
  afterEach(() => vi.unstubAllGlobals());

  function stubResponse(status: number, body: string) {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(body, { status, statusText: "x" })),
    );
  }

  it("throws ConflictError carrying the conflicts list on 409", async () => {
    stubResponse(409, JSON.stringify({
      error: "Linked files changed on disk.",
      conflicts: ["D:/api/.swebkit-api/collections/orders/get.swebreq.json"],
    }));

    const error = (await apiSend("/api/config/collections", "PUT", {}).catch((e) => e)) as Error;
    expect(error).toBeInstanceOf(ConflictError);
    expect((error as ConflictError).conflicts).toEqual([
      "D:/api/.swebkit-api/collections/orders/get.swebreq.json",
    ]);
    expect(error.message).toBe("Linked files changed on disk.");
  });

  it("throws ConflictError with an empty list when the body has no conflicts", async () => {
    stubResponse(409, "not json");

    const error = (await apiSend("/api/config/collections", "PUT", {}).catch((e) => e)) as Error;
    expect(error).toBeInstanceOf(ConflictError);
    expect((error as ConflictError).conflicts).toEqual([]);
  });

  it("filters non-string entries out of the conflicts list", async () => {
    stubResponse(409, JSON.stringify({ conflicts: ["a.swebreq.json", 42, null] }));

    const error = (await apiSend("/api/config/collections", "PUT", {}).catch((e) => e)) as Error;
    expect((error as ConflictError).conflicts).toEqual(["a.swebreq.json"]);
  });

  it("throws a plain Error for non-409 failures", async () => {
    stubResponse(400, JSON.stringify({ error: "Bad request" }));

    const error = (await apiSend("/api/config/collections", "PUT", {}).catch((e) => e)) as Error;
    expect(error).toBeInstanceOf(Error);
    expect(error).not.toBeInstanceOf(ConflictError);
    expect(error.message).toBe("Bad request");
  });
});
