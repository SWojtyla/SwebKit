import { describe, expect, it } from "vitest";
import {
    clampGridIndex,
    GRID_NAV_EDITABLE_SELECTOR,
    isActivatableEventTarget,
    isEditableEventTarget,
    resolveGridNavKey,
} from "./grid-nav";

// Fake element: records which selector `closest()` was asked about and whether
// the registered "match" covers it — enough to exercise the guard contract
// without a DOM.
function fakeTarget(matched: string | null) {
    const seen: string[] = [];
    return {
        closest: (selector: string) => {
            seen.push(selector);
            return selector === matched ? {} : null;
        },
        seen,
    };
}

describe("resolveGridNavKey", () => {
    it("maps vim-style movement keys", () => {
        expect(resolveGridNavKey("j")).toBe("down");
        expect(resolveGridNavKey("ArrowDown")).toBe("down");
        expect(resolveGridNavKey("k")).toBe("up");
        expect(resolveGridNavKey("ArrowUp")).toBe("up");
        expect(resolveGridNavKey("g")).toBe("first");
        expect(resolveGridNavKey("G")).toBe("last");
    });

    it("maps inspect/toggle/filter keys", () => {
        expect(resolveGridNavKey("e")).toBe("inspect");
        expect(resolveGridNavKey("Enter")).toBe("inspect");
        expect(resolveGridNavKey(" ")).toBe("toggle-select");
        expect(resolveGridNavKey("/")).toBe("filter");
    });

    it("gates left/right arrows on tree grids but always maps h/l", () => {
        expect(resolveGridNavKey("ArrowLeft")).toBeNull();
        expect(resolveGridNavKey("ArrowRight")).toBeNull();
        expect(resolveGridNavKey("ArrowLeft", { tree: true })).toBe("collapse");
        expect(resolveGridNavKey("ArrowRight", { tree: true })).toBe("expand");
        expect(resolveGridNavKey("h")).toBe("collapse");
        expect(resolveGridNavKey("l")).toBe("expand");
    });

    it("ignores unrelated keys", () => {
        expect(resolveGridNavKey("x")).toBeNull();
        expect(resolveGridNavKey("Escape")).toBeNull();
        expect(resolveGridNavKey("Tab")).toBeNull();
    });
});

describe("clampGridIndex", () => {
    it("clamps into range", () => {
        expect(clampGridIndex(5, 10)).toBe(5);
        expect(clampGridIndex(-1, 10)).toBe(0);
        expect(clampGridIndex(10, 10)).toBe(9);
    });

    it("returns null for an empty grid — focus can't point past nothing", () => {
        expect(clampGridIndex(0, 0)).toBeNull();
        expect(clampGridIndex(3, 0)).toBeNull();
        expect(clampGridIndex(0, -2)).toBeNull();
    });
});

describe("isEditableEventTarget", () => {
    it("matches the documented editable guard selector", () => {
        expect(GRID_NAV_EDITABLE_SELECTOR).toBe(
            "input,textarea,select,[contenteditable],.cm-editor",
        );
    });

    it("returns true for targets inside editable elements", () => {
        const input = fakeTarget(GRID_NAV_EDITABLE_SELECTOR);
        expect(isEditableEventTarget(input)).toBe(true);
        expect(input.seen).toContain(GRID_NAV_EDITABLE_SELECTOR);
    });

    it("returns false for rows, buttons and non-elements", () => {
        // `document` doesn't exist under the node test env — the hook's
        // inert-target branch (body/documentElement) covers it in the DOM.
        expect(isEditableEventTarget(fakeTarget(null))).toBe(false);
        expect(isEditableEventTarget(null)).toBe(false);
        expect(isEditableEventTarget("string-target")).toBe(false);
        expect(isEditableEventTarget({})).toBe(false);
    });
});

describe("isActivatableEventTarget", () => {
    it("defers Enter/Space to focused buttons and links", () => {
        expect(
            isActivatableEventTarget(fakeTarget("button,a[href]")),
        ).toBe(true);
        expect(isActivatableEventTarget(fakeTarget(null))).toBe(false);
    });
});
