/// Presentation rules for a `GitFileChange`: which section it belongs to, what it
/// is called, and which actions are legal on it.

import type { GitFileChange } from "@/lib/tauri-bridge";

export type GitSection = "staged" | "unstaged" | "conflicted";
export type GitFileAction = "stage" | "unstage" | "revert" | "diff";

const STATE_LABELS: Record<string, string> = {
  M: "Modified",
  A: "Added",
  D: "Deleted",
  R: "Renamed",
  C: "Copied",
  T: "Type changed",
  U: "Conflicted",
  "?": "New",
};

/** Label for a change, from whichever of the two state letters is meaningful. */
export function fileStateLabel(file: GitFileChange): string {
  if (file.conflicted) return "Conflicted";
  if (file.untracked) return "New";
  const letter = file.indexState !== "." ? file.indexState : file.worktreeState;
  return STATE_LABELS[letter] ?? letter;
}

/**
 * Sections a change appears in.
 *
 * A file modified in both the index and the worktree genuinely belongs in both —
 * part of it is staged and part is not, and hiding either half is how users end up
 * committing less than they meant to.
 */
export function fileSections(file: GitFileChange): GitSection[] {
  if (file.conflicted) return ["conflicted"];
  const sections: GitSection[] = [];
  if (file.staged) sections.push("staged");
  if (file.unstaged) sections.push("unstaged");
  return sections;
}

/** Actions offered for a change within a given section. */
export function fileActions(file: GitFileChange, section: GitSection): GitFileAction[] {
  // Conflict resolution is out of scope, so only inspection is offered.
  if (section === "conflicted") return ["diff"];
  if (section === "staged") return ["unstage", "diff"];

  const actions: GitFileAction[] = ["stage", "diff"];
  // An untracked file has no committed version, so there is nothing to restore.
  if (!file.untracked) actions.splice(1, 0, "revert");
  return actions;
}

/** Display path, showing the rename source when git reported one. */
export function displayPath(file: GitFileChange, stripPrefix?: string | null): string {
  const strip = (p: string) => {
    if (!stripPrefix) return p;
    const prefix = stripPrefix.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
    if (!prefix) return p;
    return p.startsWith(`${prefix}/`) ? p.slice(prefix.length + 1) : p;
  };

  const path = strip(file.path);
  if (file.origPath) return `${strip(file.origPath)} → ${path}`;
  return path;
}
