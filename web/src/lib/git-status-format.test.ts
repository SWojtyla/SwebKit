import { describe, it, expect } from "vitest";
import { fileStateLabel, fileSections, fileActions, displayPath } from "./git-status-format";
import type { GitFileChange } from "./tauri-bridge";

function change(overrides: Partial<GitFileChange> = {}): GitFileChange {
  return {
    path: "api/a.json",
    indexState: ".",
    worktreeState: ".",
    staged: false,
    unstaged: false,
    untracked: false,
    conflicted: false,
    origPath: null,
    ...overrides,
  };
}

describe("fileStateLabel", () => {
  it("labels a staged modification", () => {
    expect(fileStateLabel(change({ indexState: "M", staged: true }))).toBe("Modified");
  });

  it("labels an unstaged modification", () => {
    expect(fileStateLabel(change({ worktreeState: "M", unstaged: true }))).toBe("Modified");
  });

  it("labels untracked files as new", () => {
    expect(fileStateLabel(change({ untracked: true, worktreeState: "?" }))).toBe("New");
  });

  it("labels conflicts", () => {
    expect(fileStateLabel(change({ conflicted: true, indexState: "U" }))).toBe("Conflicted");
  });

  it("labels deletions and renames", () => {
    expect(fileStateLabel(change({ indexState: "D", staged: true }))).toBe("Deleted");
    expect(fileStateLabel(change({ indexState: "R", staged: true }))).toBe("Renamed");
  });
});

describe("fileSections", () => {
  it("places a staged-only change in the staged section", () => {
    expect(fileSections(change({ staged: true }))).toEqual(["staged"]);
  });

  it("places an unstaged-only change in the unstaged section", () => {
    expect(fileSections(change({ unstaged: true }))).toEqual(["unstaged"]);
  });

  /** Hiding either half is how users commit less than they meant to. */
  it("places a partially staged change in both sections", () => {
    expect(fileSections(change({ staged: true, unstaged: true }))).toEqual(["staged", "unstaged"]);
  });

  it("places conflicts in their own section only", () => {
    expect(fileSections(change({ conflicted: true, staged: true }))).toEqual(["conflicted"]);
  });
});

describe("fileActions", () => {
  it("offers unstage and diff on staged rows", () => {
    expect(fileActions(change({ staged: true }), "staged")).toEqual(["unstage", "diff"]);
  });

  it("offers stage, revert and diff on tracked unstaged rows", () => {
    expect(fileActions(change({ unstaged: true }), "unstaged")).toEqual(["stage", "revert", "diff"]);
  });

  it("omits revert for untracked files, which have no committed version", () => {
    const actions = fileActions(change({ untracked: true, unstaged: true }), "unstaged");
    expect(actions).toEqual(["stage", "diff"]);
    expect(actions).not.toContain("revert");
  });

  it("offers only diff on conflicts, since merging is out of scope", () => {
    expect(fileActions(change({ conflicted: true }), "conflicted")).toEqual(["diff"]);
  });
});

describe("displayPath", () => {
  it("returns the path unchanged with no prefix", () => {
    expect(displayPath(change({ path: "api/a.json" }))).toBe("api/a.json");
  });

  it("strips the configured API subpath", () => {
    expect(displayPath(change({ path: "api/a.json" }), "api")).toBe("a.json");
  });

  it("leaves paths outside the prefix intact", () => {
    expect(displayPath(change({ path: "src/b.cs" }), "api")).toBe("src/b.cs");
  });

  it("does not strip a similar prefix", () => {
    expect(displayPath(change({ path: "apixyz/c.json" }), "api")).toBe("apixyz/c.json");
  });

  it("shows the rename source", () => {
    const renamed = change({ path: "api/new.json", origPath: "api/old.json" });
    expect(displayPath(renamed, "api")).toBe("old.json → new.json");
  });

  it("normalizes Windows separators in the prefix", () => {
    expect(displayPath(change({ path: "api/sub/a.json" }), "api\\sub")).toBe("a.json");
  });
});
