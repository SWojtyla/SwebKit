import { useMemo } from "react";
import type { UseInfiniteQueryResult, UseQueryResult } from "@tanstack/react-query";

/**
 * `useQuery`/`useMutation` return a *fresh* result object on every render. Putting
 * them into a memoized context value therefore defeats the memo — the value
 * recomputes on every provider render and every consumer re-renders on unrelated
 * state changes (an editor keystroke re-rendering a virtualized key tree, say).
 *
 * A facade copies only the fields feature UIs actually read (`data`, status
 * booleans, `error`, `refetch`) into an object memoized on those fields, so its
 * identity is stable across renders where nothing material changed — while
 * staying transparent: `facade.data` keeps the query's structural sharing.
 */
export type QueryFacade<Q extends UseQueryResult<unknown, unknown>> = Pick<
  Q,
  | "data"
  | "error"
  | "status"
  | "isLoading"
  | "isFetching"
  | "isError"
  | "isSuccess"
  | "refetch"
  | "dataUpdatedAt"
>;

export type InfiniteQueryFacade<Q extends UseInfiniteQueryResult<unknown, unknown>> = Pick<
  Q,
  | "data"
  | "error"
  | "status"
  | "isLoading"
  | "isFetching"
  | "isError"
  | "fetchNextPage"
  | "hasNextPage"
  | "isFetchingNextPage"
  | "refetch"
>;

// The constraint is structural rather than `UseMutationResult<...>`: `mutate`'s
// options parameter makes mutation results invariant on TVariables, so no generic
// instantiation accepts every mutation. Requiring the picked keys exists instead.
type MutationFacadeKeys =
  | "mutate"
  | "mutateAsync"
  | "isPending"
  | "isError"
  | "isSuccess"
  | "error"
  | "reset";

export type MutationFacade<M extends Record<MutationFacadeKeys, unknown>> = Pick<
  M,
  MutationFacadeKeys
>;

export function useQueryFacade<Q extends UseQueryResult<unknown, unknown>>(
  q: Q,
): QueryFacade<Q> {
  return useMemo(
    () => ({
      data: q.data,
      error: q.error,
      status: q.status,
      isLoading: q.isLoading,
      isFetching: q.isFetching,
      isError: q.isError,
      isSuccess: q.isSuccess,
      refetch: q.refetch,
      dataUpdatedAt: q.dataUpdatedAt,
    }),
    [
      q.data,
      q.error,
      q.status,
      q.isLoading,
      q.isFetching,
      q.isError,
      q.isSuccess,
      q.refetch,
      q.dataUpdatedAt,
    ],
  );
}

export function useInfiniteQueryFacade<Q extends UseInfiniteQueryResult<unknown, unknown>>(
  q: Q,
): InfiniteQueryFacade<Q> {
  return useMemo(
    () => ({
      data: q.data,
      error: q.error,
      status: q.status,
      isLoading: q.isLoading,
      isFetching: q.isFetching,
      isError: q.isError,
      fetchNextPage: q.fetchNextPage,
      hasNextPage: q.hasNextPage,
      isFetchingNextPage: q.isFetchingNextPage,
      refetch: q.refetch,
    }),
    [
      q.data,
      q.error,
      q.status,
      q.isLoading,
      q.isFetching,
      q.isError,
      q.fetchNextPage,
      q.hasNextPage,
      q.isFetchingNextPage,
      q.refetch,
    ],
  );
}

export function useMutationFacade<M extends Record<MutationFacadeKeys, unknown>>(
  m: M,
): MutationFacade<M> {
  return useMemo(
    () => ({
      mutate: m.mutate,
      mutateAsync: m.mutateAsync,
      isPending: m.isPending,
      isError: m.isError,
      isSuccess: m.isSuccess,
      error: m.error,
      reset: m.reset,
    }),
    [m.mutate, m.mutateAsync, m.isPending, m.isError, m.isSuccess, m.error, m.reset],
  );
}
