import { FileDiff, Plus, Minus, Undo2 } from "lucide-react";
import type { GitFileChange } from "@/lib/tauri-bridge";
import {
  fileActions,
  fileSections,
  fileStateLabel,
  displayPath,
  type GitSection,
  type GitFileAction,
} from "@/lib/git-status-format";

interface GitFileListProps {
  files: GitFileChange[];
  subpath: string | null;
  busy: boolean;
  onAction: (action: GitFileAction, files: GitFileChange[]) => void;
}

const SECTION_TITLES: Record<GitSection, string> = {
  staged: "Staged",
  unstaged: "Not staged",
  conflicted: "Conflicted",
};

const ACTION_META: Record<GitFileAction, { label: string; icon: typeof Plus; title: string }> = {
  stage: { label: "Stage", icon: Plus, title: "Stage this file" },
  unstage: { label: "Unstage", icon: Minus, title: "Remove from the index" },
  revert: { label: "Revert", icon: Undo2, title: "Discard worktree changes" },
  diff: { label: "Diff", icon: FileDiff, title: "Show original and current" },
};

export function GitFileList({ files, subpath, busy, onAction }: GitFileListProps) {
  const sections: GitSection[] = ["conflicted", "staged", "unstaged"];

  const bySection = (section: GitSection) =>
    files.filter((f) => fileSections(f).includes(section));

  if (files.length === 0) {
    return (
      <div className="px-1 py-4 text-sm text-muted-foreground" data-testid="git-no-changes">
        No changes{subpath ? ` under ${subpath}` : ""}.
      </div>
    );
  }

  return (
    <div className="space-y-4" data-testid="git-file-list">
      {sections.map((section) => {
        const sectionFiles = bySection(section);
        if (sectionFiles.length === 0) return null;

        // Bulk action passes the explicit file list, so the scope of a "stage all"
        // is always visible rather than delegated to `git add --all`.
        const bulk: GitFileAction | null =
          section === "unstaged" ? "stage" : section === "staged" ? "unstage" : null;

        return (
          <div key={section} data-testid={`git-section-${section}`}>
            <div className="mb-1 flex items-center gap-2">
              <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                {SECTION_TITLES[section]}
              </h3>
              <span className="rounded-full bg-muted px-1.5 text-[10px] leading-4 text-muted-foreground">
                {sectionFiles.length}
              </span>
              {bulk && (
                <button
                  onClick={() => onAction(bulk, sectionFiles)}
                  disabled={busy}
                  className="ml-auto rounded border px-1.5 py-0.5 text-[11px] hover:bg-accent disabled:opacity-50"
                  data-testid={`git-section-bulk-${section}`}
                >
                  {ACTION_META[bulk].label} all ({sectionFiles.length})
                </button>
              )}
            </div>

            {section === "conflicted" && (
              <p className="mb-1 text-[11px] text-muted-foreground">
                Resolve conflicts in your editor — SwebKit does not merge.
              </p>
            )}

            <div className="space-y-0.5">
              {sectionFiles.map((file) => (
                <div
                  key={`${section}-${file.path}`}
                  className="group flex items-center gap-2 rounded px-1.5 py-1 text-xs hover:bg-accent/50"
                  data-testid={`git-file-${file.path}`}
                >
                  <span
                    className="w-16 shrink-0 font-mono text-[10px] uppercase"
                    style={{ color: stateColor(file) }}
                    title={fileStateLabel(file)}
                  >
                    {fileStateLabel(file)}
                  </span>
                  <span className="min-w-0 flex-1 truncate font-mono" title={file.path}>
                    {displayPath(file, subpath)}
                  </span>
                  <div className="flex shrink-0 items-center gap-0.5 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                    {fileActions(file, section).map((action) => {
                      const { label, icon: Icon, title } = ACTION_META[action];
                      return (
                        <button
                          key={action}
                          onClick={() => onAction(action, [file])}
                          disabled={busy}
                          title={title}
                          aria-label={`${label} ${file.path}`}
                          className={`flex items-center gap-1 rounded border px-1.5 py-0.5 text-[11px] hover:bg-accent disabled:opacity-50 ${
                            action === "revert" ? "hover:border-destructive" : ""
                          }`}
                          data-testid={`git-file-${action}-${file.path}`}
                        >
                          <Icon className="h-3 w-3" />
                          {label}
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function stateColor(file: GitFileChange): string {
  if (file.conflicted) return "var(--destructive)";
  if (file.untracked) return "var(--info)";
  const letter = file.indexState !== "." ? file.indexState : file.worktreeState;
  if (letter === "D") return "var(--destructive)";
  if (letter === "A") return "var(--success)";
  return "var(--warning)";
}
