import type {
    ApiCollection,
    ApiEnvironment,
    LinkedRequestFileState,
    LinkedRootInfo,
} from "./types";
import { DEMO_COLLECTION_ID } from "./collection-tree-utils";

// ── Linked collection roots — frontend helpers ────────────────────────────────
//
// Linked roots arrive inside `CollectionsStoreResponse.linkedRoots`; the page
// context flattens their collections/environments into the same lists the
// internal store uses, tagging each item's `origin` so mutation handlers can
// route to `/api/linked-roots/...` instead of the whole-store PUT.

/** Path comparisons must tolerate `\` vs `/` (paths arrive OS-native). */
export function normalizePath(path: string): string {
    return path.replace(/\\/g, "/").replace(/\/+$/, "");
}

export function samePath(
    a: string | null | undefined,
    b: string | null | undefined,
): boolean {
    if (!a || !b) return false;
    return normalizePath(a).toLowerCase() === normalizePath(b).toLowerCase();
}

/** `apiRootPath` relative to `repositoryRoot` (POSIX separators) — the value the
 *  Git panel persists as `apiSubpath`. Returns null when the api root sits outside
 *  the repository. */
export function apiSubpathWithin(
    repositoryRoot: string,
    apiRootPath: string,
): string | null {
    const root = normalizePath(repositoryRoot);
    const full = normalizePath(apiRootPath);
    if (full.toLowerCase() === root.toLowerCase()) return "";
    if (full.toLowerCase().startsWith(`${root.toLowerCase()}/`)) {
        return full.slice(root.length + 1);
    }
    return null;
}

/**
 * Flatten internal collections + linked roots into the workspace collection
 * list, tagging each collection's `origin` (`internal` / `demo` / `linked`).
 * Disabled roots contribute no collections; demo mode hides linked data entirely.
 */
export function mergeCollections(
    internalCollections: ApiCollection[],
    roots: LinkedRootInfo[],
    isDemo: boolean,
): ApiCollection[] {
    const internal = internalCollections.map((c) => ({
        ...c,
        origin: {
            kind:
                c.id === DEMO_COLLECTION_ID
                    ? ("demo" as const)
                    : ("internal" as const),
        },
    }));
    if (isDemo) return internal;
    const linked = roots
        .filter((r) => r.isEnabled)
        .flatMap((r) =>
            r.collections.map((c) => ({
                ...c,
                origin: {
                    kind: "linked" as const,
                    rootId: r.id,
                    rootName: r.name,
                    rootPath: r.path,
                },
            })),
        );
    return [...internal, ...linked];
}

/**
 * Merge internal environments with every enabled root's environments, tagging
 * `origin` so saves route to the right backend. `filePath` comes from the
 * root's `environmentFiles` records.
 */
export function mergeEnvironments(
    internalEnvironments: ApiEnvironment[],
    roots: LinkedRootInfo[],
    isDemo: boolean,
): ApiEnvironment[] {
    const internal = internalEnvironments.map((e) => ({
        ...e,
        origin: { kind: "internal" as const },
    }));
    if (isDemo) return internal;
    const linked = roots
        .filter((r) => r.isEnabled)
        .flatMap((r) => {
            const fileById = new Map(
                r.environmentFiles.map((f) => [
                    f.environmentId,
                    f.environmentFilePath,
                ]),
            );
            return r.environments.map((e) => ({
                ...e,
                origin: {
                    kind: "linked" as const,
                    rootId: r.id,
                    filePath: fileById.get(e.id),
                },
            }));
        });
    return [...internal, ...linked];
}

/** Strip the frontend-only `origin` tag before an environment is sent to any endpoint. */
export function stripEnvironmentOrigin(env: ApiEnvironment): ApiEnvironment {
    const { origin: _origin, ...rest } = env;
    return rest;
}

/** The file record (path + content stamp) for a linked request node id. */
export function requestFileState(
    root: LinkedRootInfo | undefined,
    requestId: string,
): LinkedRequestFileState | undefined {
    return root?.requestFiles.find((f) => f.requestId === requestId);
}
