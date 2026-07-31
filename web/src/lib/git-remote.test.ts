import { describe, it, expect } from "vitest";
import { inferCompareUrl, remoteProviderName } from "./git-remote";

describe("inferCompareUrl — GitHub", () => {
  it("handles an HTTPS remote", () => {
    expect(inferCompareUrl("https://github.com/org/repo.git", "feature/x")).toBe(
      "https://github.com/org/repo/compare/feature/x",
    );
  });

  it("handles an SSH remote identically", () => {
    expect(inferCompareUrl("git@github.com:org/repo.git", "main")).toBe(
      "https://github.com/org/repo/compare/main",
    );
  });

  it("handles a remote with no .git suffix", () => {
    expect(inferCompareUrl("https://github.com/org/repo", "main")).toBe(
      "https://github.com/org/repo/compare/main",
    );
  });
});

describe("inferCompareUrl — Azure DevOps", () => {
  it("handles the current HTTPS form", () => {
    expect(inferCompareUrl("https://dev.azure.com/org/project/_git/repo", "main")).toBe(
      "https://dev.azure.com/org/project/_git/repo/branchCompare?baseVersion=GBmain",
    );
  });

  it("strips an embedded credential from the output", () => {
    const url = inferCompareUrl("https://org@dev.azure.com/org/project/_git/repo", "main");
    expect(url).toBe(
      "https://dev.azure.com/org/project/_git/repo/branchCompare?baseVersion=GBmain",
    );
    expect(url).not.toContain("@");
  });

  it("handles the SSH form", () => {
    expect(inferCompareUrl("git@ssh.dev.azure.com:v3/org/project/repo", "main")).toBe(
      "https://dev.azure.com/org/project/_git/repo/branchCompare?baseVersion=GBmain",
    );
  });

  it("handles the legacy visualstudio.com form", () => {
    expect(inferCompareUrl("https://org.visualstudio.com/project/_git/repo", "main")).toBe(
      "https://dev.azure.com/org/project/_git/repo/branchCompare?baseVersion=GBmain",
    );
  });
});

describe("inferCompareUrl — unrecognized input", () => {
  it("returns null rather than guessing a URL", () => {
    expect(inferCompareUrl("https://gitlab.com/org/repo.git", "main")).toBeNull();
    expect(inferCompareUrl("https://bitbucket.org/org/repo.git", "main")).toBeNull();
  });

  it("returns null for empty or malformed remotes", () => {
    expect(inferCompareUrl("", "main")).toBeNull();
    expect(inferCompareUrl("not a url at all", "main")).toBeNull();
  });

  it("returns null for an empty branch", () => {
    expect(inferCompareUrl("https://github.com/org/repo.git", "")).toBeNull();
    expect(inferCompareUrl("https://github.com/org/repo.git", "   ")).toBeNull();
  });
});

describe("inferCompareUrl — encoding", () => {
  it("percent-encodes branch segments but keeps the slash", () => {
    expect(inferCompareUrl("https://github.com/org/repo.git", "feature/a b")).toBe(
      "https://github.com/org/repo/compare/feature/a%20b",
    );
  });
});

describe("remoteProviderName", () => {
  it("names recognized providers", () => {
    expect(remoteProviderName("https://github.com/org/repo.git")).toBe("GitHub");
    expect(remoteProviderName("https://dev.azure.com/org/p/_git/r")).toBe("Azure DevOps");
    expect(remoteProviderName("https://org.visualstudio.com/p/_git/r")).toBe("Azure DevOps");
  });

  it("returns null for anything else", () => {
    expect(remoteProviderName("https://gitlab.com/org/repo.git")).toBeNull();
    expect(remoteProviderName("")).toBeNull();
  });
});
