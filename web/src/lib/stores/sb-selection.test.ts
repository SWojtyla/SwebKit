import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import { loadLastNamespace, saveLastNamespace, loadLastEntity, saveLastEntity } from "./sb-selection";

/** Minimal in-memory localStorage; the node environment has none. */
function installStorage(): Map<string, string> {
  const store = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
    clear: () => store.clear(),
  });
  return store;
}

let store: Map<string, string>;

beforeEach(() => {
  store = installStorage();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("last namespace", () => {
  it("round-trips the selected namespace id", () => {
    saveLastNamespace("ns-1");
    expect(loadLastNamespace()).toBe("ns-1");
  });

  it("returns null when nothing is stored", () => {
    expect(loadLastNamespace()).toBeNull();
  });
});

describe("last entity", () => {
  it("round-trips the selected entity, scoped by namespace", () => {
    saveLastEntity("ns-1", { entityPath: "orders", name: "Orders" });
    saveLastEntity("ns-2", { entityPath: "invoices", name: "Invoices" });
    expect(loadLastEntity("ns-1")).toEqual({ entityPath: "orders", name: "Orders" });
    expect(loadLastEntity("ns-2")).toEqual({ entityPath: "invoices", name: "Invoices" });
  });

  it("returns null when nothing is stored for that namespace", () => {
    expect(loadLastEntity("missing")).toBeNull();
  });

  it("clears the stored entity when saved with null", () => {
    saveLastEntity("ns-1", { entityPath: "orders", name: "Orders" });
    saveLastEntity("ns-1", null);
    expect(loadLastEntity("ns-1")).toBeNull();
  });

  it("returns null on malformed JSON without throwing", () => {
    store.set("sb-last-entity:ns-1", "{not json");
    expect(loadLastEntity("ns-1")).toBeNull();
  });

  it("returns null when the stored record has no entityPath", () => {
    store.set("sb-last-entity:ns-1", JSON.stringify({ name: "Orders" }));
    expect(loadLastEntity("ns-1")).toBeNull();
  });

  it("falls back to entityPath when name is missing", () => {
    store.set("sb-last-entity:ns-1", JSON.stringify({ entityPath: "orders" }));
    expect(loadLastEntity("ns-1")).toEqual({ entityPath: "orders", name: "orders" });
  });

  it("swallows storage failures on save", () => {
    vi.stubGlobal("localStorage", {
      getItem: () => null,
      setItem: () => {
        throw new Error("QuotaExceededError");
      },
    });
    expect(() => saveLastEntity("ns-1", { entityPath: "orders", name: "Orders" })).not.toThrow();
    expect(() => saveLastNamespace("ns-1")).not.toThrow();
  });
});
