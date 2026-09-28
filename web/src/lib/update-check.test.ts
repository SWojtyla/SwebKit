import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { compareVersions, checkForUpdate, UPDATE_MANIFEST_URL, RELEASES_API_URL } from "./update-check";

// Vitest runs these in the node environment — no localStorage/window — so the
// module's cache reads/writes all take their catch-paths and stay inert.

describe("compareVersions", () => {
    it("orders numeric segments correctly", () => {
        expect(compareVersions("1.0.0", "1.0.1")).toBeLessThan(0);
        expect(compareVersions("1.10.0", "1.9.9")).toBeGreaterThan(0);
        expect(compareVersions("2.0.0", "1.99.99")).toBeGreaterThan(0);
    });

    it("treats a leading v and missing segments as equal", () => {
        expect(compareVersions("v1.2.0", "1.2.0")).toBe(0);
        expect(compareVersions("1.2", "1.2.0")).toBe(0);
    });
});

describe("checkForUpdate", () => {
    const fetchMock = vi.fn();

    beforeEach(() => {
        fetchMock.mockReset();
        vi.stubGlobal("fetch", fetchMock);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    function jsonResponse(body: unknown, status = 200) {
        return new Response(JSON.stringify(body), {
            status,
            headers: { "Content-Type": "application/json" },
        });
    }

    it("prefers the release manifest and reports an available update", async () => {
        fetchMock.mockResolvedValueOnce(
            jsonResponse({ version: "1.2.0", url: "https://example.test/r/v120" }),
        );

        const result = await checkForUpdate("1.0.0");

        expect(result.updateAvailable).toBe(true);
        expect(result.latestVersion).toBe("1.2.0");
        expect(result.source).toBe("manifest");
        expect(result.releaseUrl).toBe("https://example.test/r/v120");
        expect(fetchMock).toHaveBeenCalledTimes(1);
        expect(fetchMock.mock.calls[0][0]).toBe(UPDATE_MANIFEST_URL);
    });

    it("falls back to the GitHub releases API when the manifest is missing", async () => {
        fetchMock
            .mockResolvedValueOnce(new Response("nope", { status: 404 }))
            .mockResolvedValueOnce(
                jsonResponse({ tag_name: "v1.1.0", html_url: "https://example.test/tag" }),
            );

        const result = await checkForUpdate("1.0.0");

        expect(result.source).toBe("github");
        expect(result.latestVersion).toBe("1.1.0");
        expect(fetchMock).toHaveBeenCalledTimes(2);
        expect(fetchMock.mock.calls[1][0]).toBe(RELEASES_API_URL);
    });

    it("reports no update when the running version is current", async () => {
        fetchMock.mockResolvedValueOnce(jsonResponse({ version: "1.0.0" }));

        const result = await checkForUpdate("1.0.0");

        expect(result.updateAvailable).toBe(false);
    });

    it("degrades to 'unavailable' when every source fails and nothing is cached", async () => {
        fetchMock.mockRejectedValue(new Error("offline"));

        const result = await checkForUpdate("1.0.0");

        expect(result.source).toBe("unavailable");
        expect(result.updateAvailable).toBe(false);
        expect(result.error).toBeTruthy();
    });
});
