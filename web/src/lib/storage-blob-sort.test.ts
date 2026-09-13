import { describe, it, expect } from "vitest";
import { sortStorageBlobItems } from "./storage-blob-sort";
import type { StorageBlobItem } from "./types";

function item(overrides: Partial<StorageBlobItem>): StorageBlobItem {
  return {
    name: "item",
    isPrefix: false,
    sizeBytes: null,
    contentType: null,
    lastModified: null,
    etag: null,
    ...overrides,
  };
}

describe("sortStorageBlobItems", () => {
  const items = [
    item({ name: "banana.txt", sizeBytes: 300, lastModified: "2026-01-02T00:00:00Z" }),
    item({ name: "apple.txt", sizeBytes: 100, lastModified: "2026-01-03T00:00:00Z" }),
    item({ name: "cherry.txt", sizeBytes: 200, lastModified: "2026-01-01T00:00:00Z" }),
  ];

  it("sorts by name ascending/descending", () => {
    expect(sortStorageBlobItems(items, "name", "asc").map((i) => i.name)).toEqual([
      "apple.txt",
      "banana.txt",
      "cherry.txt",
    ]);
    expect(sortStorageBlobItems(items, "name", "desc").map((i) => i.name)).toEqual([
      "cherry.txt",
      "banana.txt",
      "apple.txt",
    ]);
  });

  it("sorts by size", () => {
    expect(sortStorageBlobItems(items, "size", "asc").map((i) => i.name)).toEqual([
      "apple.txt",
      "cherry.txt",
      "banana.txt",
    ]);
  });

  it("sorts by last modified", () => {
    expect(sortStorageBlobItems(items, "modified", "desc").map((i) => i.name)).toEqual([
      "apple.txt",
      "banana.txt",
      "cherry.txt",
    ]);
  });

  it("treats null size/lastModified as least", () => {
    const withNull = [...items, item({ name: "folder-like", sizeBytes: null, lastModified: null })];
    expect(sortStorageBlobItems(withNull, "size", "asc")[0].name).toBe("folder-like");
    expect(sortStorageBlobItems(withNull, "modified", "asc")[0].name).toBe("folder-like");
  });

  it("does not mutate the input array", () => {
    const copy = [...items];
    sortStorageBlobItems(items, "name", "asc");
    expect(items).toEqual(copy);
  });
});
