/// Provider inference for the "compare on remote" link.
///
/// Pure string work with many input shapes (HTTPS, SSH, `git@`, Azure's two URL
/// generations, embedded credentials), so it lives in TypeScript where it is cheap
/// to test rather than in Rust — `git_remote_url` returns the raw remote and this
/// interprets it. See docs/features/active/api-client-git-completion/decisions.md DEC-G5.

interface ParsedRemote {
  host: string;
  /** Path segments after the host, with any `.git` suffix stripped. */
  segments: string[];
}

function parseRemote(remoteUrl: string): ParsedRemote | null {
  const url = remoteUrl.trim();
  if (!url) return null;

  let host: string;
  let path: string;

  // scp-style: git@host:path, or ssh://git@host/path
  const scpMatch = /^[^@/]+@([^:/]+):(.+)$/.exec(url);
  if (scpMatch) {
    host = scpMatch[1];
    path = scpMatch[2];
  } else {
    try {
      const parsed = new URL(url);
      // Strips any embedded credential — it must never reach the rendered link.
      host = parsed.hostname;
      path = parsed.pathname;
    } catch {
      return null;
    }
  }

  const segments = path
    .replace(/\.git$/i, "")
    .split("/")
    .filter((s) => s.length > 0);

  if (!host || segments.length === 0) return null;
  return { host: host.toLowerCase(), segments };
}

/**
 * Builds a branch-compare URL for a recognized provider.
 *
 * Returns `null` for anything unrecognized so a broken or guessed link is never
 * rendered.
 */
export function inferCompareUrl(remoteUrl: string, branch: string): string | null {
  const parsed = parseRemote(remoteUrl);
  if (!parsed || !branch.trim()) return null;

  const { host, segments } = parsed;
  const encodedBranch = branch.split("/").map(encodeURIComponent).join("/");

  // GitHub: https://github.com/<owner>/<repo>/compare/<branch>
  if (host === "github.com" || host.endsWith(".github.com")) {
    if (segments.length < 2) return null;
    const [owner, repo] = segments;
    return `https://github.com/${owner}/${repo}/compare/${encodedBranch}`;
  }

  // Azure DevOps (current): https://dev.azure.com/<org>/<project>/_git/<repo>
  if (host === "dev.azure.com") {
    const gitIndex = segments.indexOf("_git");
    if (gitIndex < 2 || gitIndex + 1 >= segments.length) return null;
    const org = segments[0];
    const project = segments.slice(1, gitIndex).join("/");
    const repo = segments[gitIndex + 1];
    return `https://dev.azure.com/${org}/${project}/_git/${repo}/branchCompare?baseVersion=GB${encodedBranch}`;
  }

  // Azure DevOps SSH: ssh.dev.azure.com:v3/<org>/<project>/<repo>
  if (host === "ssh.dev.azure.com") {
    const withoutVersion = segments[0] === "v3" ? segments.slice(1) : segments;
    if (withoutVersion.length < 3) return null;
    const [org, project, repo] = withoutVersion;
    return `https://dev.azure.com/${org}/${project}/_git/${repo}/branchCompare?baseVersion=GB${encodedBranch}`;
  }

  // Azure DevOps (legacy): https://<org>.visualstudio.com/<project>/_git/<repo>
  if (host.endsWith(".visualstudio.com")) {
    const org = host.slice(0, -".visualstudio.com".length);
    const gitIndex = segments.indexOf("_git");
    if (gitIndex < 1 || gitIndex + 1 >= segments.length) return null;
    const project = segments.slice(0, gitIndex).join("/");
    const repo = segments[gitIndex + 1];
    return `https://dev.azure.com/${org}/${project}/_git/${repo}/branchCompare?baseVersion=GB${encodedBranch}`;
  }

  return null;
}

/** Human-readable provider name for the link label. */
export function remoteProviderName(remoteUrl: string): string | null {
  const parsed = parseRemote(remoteUrl);
  if (!parsed) return null;
  const { host } = parsed;
  if (host === "github.com" || host.endsWith(".github.com")) return "GitHub";
  if (host === "dev.azure.com" || host === "ssh.dev.azure.com" || host.endsWith(".visualstudio.com")) {
    return "Azure DevOps";
  }
  return null;
}
