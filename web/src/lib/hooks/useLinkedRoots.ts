import { useMemo } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, describeApiError, apiFetch, apiSend } from "../api";
import { useNotification } from "@/components/layout/notification-context";
import type {
    ApiEnvironment,
    CollectionsStoreResponse,
    HttpRequestEntry,
    LinkedCollectionMutationResult,
    LinkedRequestConflict,
    LinkedRequestMutationResult,
    LinkedRootInfo,
} from "../types";

// ── Linked collection roots (/api/linked-roots) ───────────────────────────────
//
// Linked roots arrive embedded in the collections store response, so the hook
// shares the exact ["collections"] query key with useCollections — there must
// never be a second key for this data or the two would drift out of sync. Every
// mutation below refreshes that same key on success.

export function useLinkedRoots(enabled = true) {
    return useQuery<CollectionsStoreResponse, Error, LinkedRootInfo[]>({
        queryKey: ["collections"],
        queryFn: ({ signal }) =>
            apiFetch<CollectionsStoreResponse>(
                "/api/config/collections/store",
                { signal },
            ),
        select: (data) => data.linkedRoots ?? [],
        enabled,
    });
}

/** Outcome of a linked `PUT .../requests/{id}` save — distinguishes a 409
 *  disk-edit conflict (retryable with the fresh stamp) from plain errors
 *  (already surfaced as notifications). */
export type LinkedSaveOutcome =
    | { kind: "saved"; result: LinkedRequestMutationResult }
    | { kind: "conflict"; conflict: LinkedRequestConflict }
    | { kind: "error" };

function asLinkedRequestConflict(
    payload: unknown,
): LinkedRequestConflict | null {
    if (typeof payload !== "object" || payload === null) return null;
    const p = payload as Record<string, unknown>;
    // The contract guarantees these fields on a 409; tolerate them being null.
    if (!("currentContentStamp" in p) && !("requestFilePath" in p)) return null;
    return {
        error: typeof p.error === "string" ? p.error : undefined,
        currentContentStamp:
            typeof p.currentContentStamp === "string"
                ? p.currentContentStamp
                : null,
        requestFilePath:
            typeof p.requestFilePath === "string" ? p.requestFilePath : null,
    };
}

export interface LinkedRootActions {
    // Roots
    createRoot(input: {
        path: string;
        name?: string | null;
        brunoSyncFolderPath?: string | null;
    }): Promise<LinkedRootInfo | null>;
    updateRoot(
        rootId: string,
        patch: {
            name?: string | null;
            isEnabled?: boolean;
            brunoSyncFolderPath?: string | null;
            brunoSyncEnabled?: boolean;
        },
    ): Promise<LinkedRootInfo | null>;
    removeRoot(rootId: string): Promise<boolean>;
    reloadRoot(rootId: string): Promise<LinkedRootInfo | null>;
    // Collections
    createCollection(
        rootId: string,
        name: string,
    ): Promise<LinkedCollectionMutationResult | null>;
    renameCollection(
        rootId: string,
        collectionId: string,
        name: string,
    ): Promise<LinkedRootInfo | null>;
    deleteCollection(
        rootId: string,
        collectionId: string,
    ): Promise<LinkedRootInfo | null>;
    // Requests — `requestId` is the request node's tree id
    createRequest(
        rootId: string,
        collectionId: string,
        input: {
            name: string;
            parentFolderId?: string | null;
            request: HttpRequestEntry;
        },
    ): Promise<LinkedRequestMutationResult | null>;
    /** Save with the file's last-known content stamp; omit it to overwrite. */
    saveRequest(
        rootId: string,
        collectionId: string,
        requestId: string,
        request: HttpRequestEntry,
        contentStamp?: string | null,
    ): Promise<LinkedSaveOutcome>;
    deleteRequest(
        rootId: string,
        collectionId: string,
        requestId: string,
    ): Promise<LinkedRootInfo | null>;
    // Folders — `folderId` is the folder node's tree id
    createFolder(
        rootId: string,
        collectionId: string,
        name: string,
        parentFolderId?: string | null,
    ): Promise<LinkedRootInfo | null>;
    renameFolder(
        rootId: string,
        collectionId: string,
        folderId: string,
        name: string,
    ): Promise<LinkedRootInfo | null>;
    deleteFolder(
        rootId: string,
        collectionId: string,
        folderId: string,
    ): Promise<LinkedRootInfo | null>;
    // Move/order — node/parent ids are request/folder tree ids; null parent is the collection root
    moveNode(
        rootId: string,
        collectionId: string,
        nodeId: string,
        parentFolderId: string | null,
    ): Promise<LinkedRootInfo | null>;
    setChildOrder(
        rootId: string,
        collectionId: string,
        parentFolderId: string | null,
        orderedChildIds: string[],
    ): Promise<LinkedRootInfo | null>;
    // Environments
    createEnvironment(
        rootId: string,
        environment: ApiEnvironment,
        collectionId?: string | null,
    ): Promise<LinkedRootInfo | null>;
    updateEnvironment(
        rootId: string,
        environmentId: string,
        environment: ApiEnvironment,
    ): Promise<LinkedRootInfo | null>;
    deleteEnvironment(
        rootId: string,
        environmentId: string,
    ): Promise<LinkedRootInfo | null>;
}

export function useLinkedRootActions(): LinkedRootActions {
    const qc = useQueryClient();
    const { notify } = useNotification();

    return useMemo<LinkedRootActions>(() => {
        const refresh = () => {
            qc.invalidateQueries({ queryKey: ["collections"] });
        };

        /** Run a mutation; refresh the store + notify on success/failure. */
        const run = async <T>(
            title: string,
            fn: () => Promise<T>,
            opts?: { onOk?: (result: T) => void; alsoEnvironments?: boolean },
        ): Promise<T | null> => {
            try {
                const result = await fn();
                refresh();
                if (opts?.alsoEnvironments) {
                    qc.invalidateQueries({ queryKey: ["environments"] });
                }
                opts?.onOk?.(result);
                return result;
            } catch (error) {
                notify("error", title, describeApiError(error));
                return null;
            }
        };

        return {
            // ── Roots ────────────────────────────────────────────────────
            createRoot: async (input) => {
                try {
                    const root = await apiSend<LinkedRootInfo>(
                        "/api/linked-roots",
                        "POST",
                        input,
                    );
                    refresh();
                    notify(
                        "success",
                        "Linked collection root",
                        `${root.name} — ${root.path}`,
                    );
                    return root;
                } catch (error) {
                    if (error instanceof ApiError && error.status === 409) {
                        notify(
                            "info",
                            "Already linked",
                            "That folder is already a linked collection root.",
                        );
                    } else {
                        notify(
                            "error",
                            "Couldn't link folder",
                            describeApiError(error),
                        );
                    }
                    return null;
                }
            },
            updateRoot: (rootId, patch) =>
                run("Couldn't update linked root", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}`,
                        "PATCH",
                        patch,
                    ),
                ),
            removeRoot: async (rootId) =>
                (await run("Couldn't unlink collection root", () =>
                    apiSend(`/api/linked-roots/${rootId}`, "DELETE"),
                )) !== null,
            reloadRoot: (rootId) =>
                run("Couldn't reload collection root", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/reload`,
                        "POST",
                    ),
                ),

            // ── Collections ──────────────────────────────────────────────
            createCollection: (rootId, name) =>
                run("Couldn't create collection", () =>
                    apiSend<LinkedCollectionMutationResult>(
                        `/api/linked-roots/${rootId}/collections`,
                        "POST",
                        { name },
                    ),
                ),
            renameCollection: (rootId, collectionId, name) =>
                run("Couldn't rename collection", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}`,
                        "PATCH",
                        { name },
                    ),
                ),
            deleteCollection: (rootId, collectionId) =>
                run("Couldn't delete collection", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}`,
                        "DELETE",
                    ),
                ),

            // ── Requests ─────────────────────────────────────────────────
            createRequest: (rootId, collectionId, input) =>
                run("Couldn't create request", () =>
                    apiSend<LinkedRequestMutationResult>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/requests`,
                        "POST",
                        input,
                    ),
                ),
            saveRequest: async (
                rootId,
                collectionId,
                requestId,
                request,
                contentStamp,
            ) => {
                try {
                    const result = await apiSend<LinkedRequestMutationResult>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/requests/${requestId}`,
                        "PUT",
                        { request, contentStamp: contentStamp ?? null },
                    );
                    refresh();
                    return { kind: "saved", result };
                } catch (error) {
                    // A 409 is a disk-edit conflict — return it (with the fresh
                    // stamp/path) so callers can offer reload/overwrite instead
                    // of surfacing a dead-end error toast.
                    if (
                        error instanceof ApiError &&
                        error.status === 409 &&
                        asLinkedRequestConflict(error.payload)
                    ) {
                        return {
                            kind: "conflict",
                            conflict: asLinkedRequestConflict(error.payload)!,
                        };
                    }
                    notify(
                        "error",
                        "Couldn't save request",
                        describeApiError(error),
                    );
                    return { kind: "error" };
                }
            },
            deleteRequest: (rootId, collectionId, requestId) =>
                run("Couldn't delete request", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/requests/${requestId}`,
                        "DELETE",
                    ),
                ),

            // ── Folders ──────────────────────────────────────────────────
            createFolder: (rootId, collectionId, name, parentFolderId) =>
                run("Couldn't create folder", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/folders`,
                        "POST",
                        { name, parentFolderId: parentFolderId ?? null },
                    ),
                ),
            renameFolder: (rootId, collectionId, folderId, name) =>
                run("Couldn't rename folder", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/folders/${folderId}`,
                        "PATCH",
                        { name },
                    ),
                ),
            deleteFolder: (rootId, collectionId, folderId) =>
                run("Couldn't delete folder", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/folders/${folderId}`,
                        "DELETE",
                    ),
                ),

            // ── Move / order ─────────────────────────────────────────────
            moveNode: (rootId, collectionId, nodeId, parentFolderId) =>
                run("Couldn't move item", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/nodes/${nodeId}/move`,
                        "PATCH",
                        { parentFolderId },
                    ),
                ),
            setChildOrder: (
                rootId,
                collectionId,
                parentFolderId,
                orderedChildIds,
            ) =>
                run("Couldn't reorder items", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/collections/${collectionId}/order`,
                        "PUT",
                        { parentFolderId, orderedChildIds },
                    ),
                ),

            // ── Environments ─────────────────────────────────────────────
            createEnvironment: (rootId, environment, collectionId) =>
                run("Couldn't create environment", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/environments`,
                        "POST",
                        { environment, collectionId: collectionId ?? null },
                    ),
                ),
            updateEnvironment: (rootId, environmentId, environment) =>
                run("Couldn't save environment", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/environments/${environmentId}`,
                        "PUT",
                        // SaveLinkedEnvironmentRequest — collectionId only
                        // applies on create, so the update carries just the env.
                        { environment },
                    ),
                ),
            deleteEnvironment: (rootId, environmentId) =>
                run("Couldn't delete environment", () =>
                    apiSend<LinkedRootInfo>(
                        `/api/linked-roots/${rootId}/environments/${environmentId}`,
                        "DELETE",
                    ),
                ),
        };
    }, [qc, notify]);
}
