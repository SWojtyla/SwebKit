import { describe, it, expect } from "vitest";
import { planBlobDownload } from "./storage-blob-download";
import type { StorageBlobContent } from "./types";

function content(overrides: Partial<StorageBlobContent>): StorageBlobContent {
  return {
    containerName: "configs",
    blobName: "app-settings.json",
    content: "",
    contentType: null,
    totalSizeBytes: 0,
    wasTruncated: false,
    isBinary: false,
    ...overrides,
  };
}

// Regression coverage for a real data-corruption bug: Download used to hand `content`
// straight to a text-file writer even when `isBinary` was true. For a binary blob the
// sidecar returns an *empty* `content`, so this silently wrote a 0-byte file with no
// warning — the exact same shape of bug the Content tab already guards against.
describe("planBlobDownload", () => {
  it("round-trips text content unchanged for a non-binary blob", () => {
    const data = content({ isBinary: false, content: '{"a":1}', contentType: "application/json" });
    const plan = planBlobDownload("app-settings.json", data);
    expect(plan).toEqual({
      kind: "text",
      filename: "app-settings.json",
      content: '{"a":1}',
      mimeType: "application/json",
    });
  });

  it("blocks the download instead of writing a binary blob's (empty) content as text", () => {
    const data = content({ isBinary: true, content: "", contentType: "image/png" });
    const plan = planBlobDownload("logo.png", data);
    expect(plan).toEqual({ kind: "blocked-binary", blobName: "logo.png" });
  });

  it("blocks a binary blob even if content happens to be non-empty (defense in depth)", () => {
    // The sidecar contract is that binary blobs come back with empty content, but the
    // decision must not depend on that — `isBinary` alone determines the outcome so a
    // future change to the sidecar's behavior can't silently corrupt downloads again.
    const data = content({ isBinary: true, content: "not actually the real bytes", contentType: "application/zip" });
    const plan = planBlobDownload("archive.zip", data);
    expect(plan).toEqual({ kind: "blocked-binary", blobName: "archive.zip" });
  });

  it("falls back to text/plain when contentType is missing", () => {
    const data = content({ isBinary: false, content: "hello", contentType: null });
    const plan = planBlobDownload("notes.txt", data);
    expect(plan).toEqual({ kind: "text", filename: "notes.txt", content: "hello", mimeType: "text/plain" });
  });

  it("derives the filename from the final path segment for a virtual-folder blob name", () => {
    const data = content({ isBinary: false, content: "x", contentType: "text/plain" });
    const plan = planBlobDownload("env/prod/settings.json", data);
    expect(plan.kind).toBe("text");
    expect((plan as { filename: string }).filename).toBe("settings.json");
  });
});
