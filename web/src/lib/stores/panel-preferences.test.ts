import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import {
  loadPanelWidths,
  savePanelWidths,
  loadViewPreference,
  saveViewPreference,
  PANEL_WIDTHS_VERSION,
} from "./panel-preferences";

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

describe("loadPanelWidths", () => {
  it("round-trips saved widths", () => {
    savePanelWidths("test", [300, 500, 500]);
    expect(loadPanelWidths("test", 3)).toEqual([300, 500, 500]);
  });

  it("returns null when nothing is stored", () => {
    expect(loadPanelWidths("missing", 3)).toBeNull();
  });

  it("returns null on malformed JSON without throwing", () => {
    store.set("panel-widths:test", "{not json");
    expect(loadPanelWidths("test", 3)).toBeNull();
  });

  /** DEC-6: an old version resets rather than restoring cramped proportions. */
  it("discards a record from an older version", () => {
    store.set(
      "panel-widths:test",
      JSON.stringify({ version: PANEL_WIDTHS_VERSION - 1, widths: [260, 540, 600] }),
    );
    expect(loadPanelWidths("test", 3)).toBeNull();
  });

  it("discards a record whose panel count no longer matches", () => {
    savePanelWidths("test", [300, 500]);
    expect(loadPanelWidths("test", 3)).toBeNull();
  });

  it("rejects non-positive or non-numeric widths", () => {
    store.set("panel-widths:test", JSON.stringify({ version: PANEL_WIDTHS_VERSION, widths: [300, -5, 500] }));
    expect(loadPanelWidths("test", 3)).toBeNull();

    store.set("panel-widths:test", JSON.stringify({ version: PANEL_WIDTHS_VERSION, widths: [300, "wide", 500] }));
    expect(loadPanelWidths("test", 3)).toBeNull();
  });

  it("rejects a widths field that is not an array", () => {
    store.set("panel-widths:test", JSON.stringify({ version: PANEL_WIDTHS_VERSION, widths: 300 }));
    expect(loadPanelWidths("test", 3)).toBeNull();
  });
});

describe("savePanelWidths", () => {
  it("swallows storage failures", () => {
    vi.stubGlobal("localStorage", {
      getItem: () => null,
      setItem: () => {
        throw new Error("QuotaExceededError");
      },
    });
    expect(() => savePanelWidths("test", [300, 500, 500])).not.toThrow();
  });
});

describe("view preferences", () => {
  it("round-trips a boolean", () => {
    saveViewPreference("wrap", true);
    expect(loadViewPreference("wrap", false)).toBe(true);
    saveViewPreference("wrap", false);
    expect(loadViewPreference("wrap", true)).toBe(false);
  });

  it("round-trips a string", () => {
    saveViewPreference("width", "460");
    expect(loadViewPreference("width", "0")).toBe("460");
  });

  it("falls back when nothing is stored", () => {
    expect(loadViewPreference("absent", true)).toBe(true);
  });

  it("falls back on malformed JSON", () => {
    store.set("view-pref:broken", "{oops");
    expect(loadViewPreference("broken", false)).toBe(false);
  });
});
