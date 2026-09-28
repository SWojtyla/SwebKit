import { useEffect, useRef, useState } from "react";
import { ChevronRight, ChevronsDownUp, ChevronsUpDown, Folder } from "lucide-react";
import { useGridKeyboardNav } from "@/lib/hooks/useGridKeyboardNav";
import {
  useRedisBrowser,
  useRedisConnection,
  useRedisNav,
  useRedisQueries,
} from "../redis-context";
import { redisRowKey, type FlatRedisRow } from "../redis-namespace-tree";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useRedisKeyInfoBatch } from "@/lib/hooks";
import { QueryState } from "@/components/shared/QueryState";
import { typeColors } from "../type-colors";
import { formatTtl } from "@/lib/redis-format";

// Vertical guide rules connecting a row to its ancestors, VSCode-file-tree style — the thing
// that was missing before and made the whole tree read as a flat, undifferentiated wall of text.
function IndentGuides({ depth }: { depth: number }) {
  if (depth === 0) return null;
  return (
    <div className="flex shrink-0 self-stretch">
      {Array.from({ length: depth }).map((_, i) => (
        <span key={i} className="w-3 shrink-0 self-stretch border-r border-border/60" />
      ))}
    </div>
  );
}

export function KeyBrowserPanel() {
  const ctx = {
    ...useRedisConnection(),
    ...useRedisNav(),
    ...useRedisQueries(),
    ...useRedisBrowser(),
  };

  // Owned here rather than in the page context: `useVirtualizer` returns a stable
  // instance whose internals mutate on scroll, so a memoized context value holding it
  // would keep handing consumers the same object and the tree would stop re-rendering
  // as you scroll.
  const redisVirtualizer = useVirtualizer({
    count: ctx.flatRedisRows.length,
    getScrollElement: () => ctx.redisTreeRef.current,
    estimateSize: () => 28,
    getItemKey: (index) => redisRowKey(ctx.flatRedisRows[index]),
    measureElement: (el) => el?.getBoundingClientRect().height ?? 28,
  });

  const virtualItems = redisVirtualizer.getVirtualItems();

  // The `/` shortcut target — the pattern input above the tree.
  const searchInputRef = useRef<HTMLInputElement | null>(null);

  // Keyboard nav (ux-power-pack §4): j/k + arrows move a focused row through
  // the flattened namespace tree (the virtualizer mounts it before focus
  // lands), e/Enter inspects (expand/collapse namespaces, open keys),
  // h/ArrowLeft collapses or steps to the parent, l/ArrowRight expands or steps
  // to the first child, / focuses the pattern input, g/G jump first/last.
  const flatRows = ctx.flatRedisRows;
  const gridNav = useGridKeyboardNav({
    containerRef: ctx.redisTreeRef,
    itemCount: flatRows.length,
    resetKey: `${ctx.resolvedCacheId}|${ctx.pattern}`,
    getFilterInput: () => searchInputRef.current,
    scrollToIndex: (index) => redisVirtualizer.scrollToIndex(index),
    onInspect: (index) => {
      const row = flatRows[index];
      if (!row) return;
      if (row.kind === "namespace") ctx.toggleNamespace(row.node.path);
      else ctx.setSelectedKey(row.key);
    },
    onToggleSelect: (index) => {
      const row = flatRows[index];
      if (!row) return;
      if (row.kind === "key") ctx.toggleKeySelection(row.key);
      else ctx.toggleSubtreeSelection(row.node);
    },
    onCollapse: (index) => {
      const row = flatRows[index];
      if (!row) return false;
      if (
        row.kind === "namespace" &&
        ctx.expandedNamespaces.has(row.node.path)
      ) {
        ctx.toggleNamespace(row.node.path);
        return true;
      }
      if (row.kind === "key") {
        // Keys are flattened at their namespace's own depth, after all
        // expanded descendants — the parent ns row is the nearest previous
        // namespace row with the same node.path.
        for (let j = index - 1; j >= 0; j--) {
          const candidate = flatRows[j];
          if (
            candidate.kind === "namespace" &&
            candidate.node.path === row.node.path
          ) {
            gridNav.focusIndex(j);
            return true;
          }
        }
        return false;
      }
      // Already-collapsed namespace → step to the parent: nearest previous
      // namespace row at a lower depth.
      for (let j = index - 1; j >= 0; j--) {
        const candidate = flatRows[j];
        if (candidate.kind === "namespace" && candidate.depth < row.depth) {
          gridNav.focusIndex(j);
          return true;
        }
      }
      return false;
    },
    onExpand: (index) => {
      const row = flatRows[index];
      if (!row || row.kind !== "namespace") return false;
      if (!ctx.expandedNamespaces.has(row.node.path)) {
        ctx.toggleNamespace(row.node.path);
        return true;
      }
      // Already expanded → step to the first child row (a deeper namespace,
      // or one of this node's own keys which share its depth).
      const next = flatRows[index + 1];
      if (
        next &&
        (next.depth > row.depth ||
          (next.kind === "key" && next.node.path === row.node.path))
      ) {
        gridNav.focusIndex(index + 1);
        return true;
      }
      return false;
    },
  });

  // Committed on blur/Enter, not per keystroke — typing used to recompute the whole namespace
  // tree (`namespaceTree` depends on `separator`) on every character, which also kept re-tripping
  // the expansion seed's dependency. See docs/pitfalls/react-frontend.md's DraftInput note.
  const [separatorDraft, setSeparatorDraft] = useState(ctx.separator);
  useEffect(() => setSeparatorDraft(ctx.separator), [ctx.separator]);
  const commitSeparator = () => {
    const next = separatorDraft.trim() || ":";
    setSeparatorDraft(next);
    if (next !== ctx.separator) ctx.setSeparator(next);
  };

  // Type/TTL hint for key rows: one request for exactly the rows the virtualizer currently renders
  // (a bounded handful, not the whole loaded key set), sharing its cache with the detail panel so
  // opening a key you've already seen a hint for is instant. See `useRedisKeyInfoBatch`.
  const visibleKeys = virtualItems
    .map((item) => ctx.flatRedisRows[item.index])
    .filter((row): row is Extract<FlatRedisRow, { kind: "key" }> => row?.kind === "key")
    .map((row) => row.key);
  const keyInfoByKey = useRedisKeyInfoBatch(ctx.resolvedCacheId, visibleKeys);

  const renderFlatRedisRow = (row: FlatRedisRow) => {
    if (row.kind === "namespace") {
      const { node, depth } = row;
      const isExpanded = ctx.expandedNamespaces.has(node.path);
      const subtree = ctx.subtreeKeysByPath.get(node.path) ?? [];
      const selectedInSubtree = subtree.reduce((n, k) => n + (ctx.selectedKeys.has(k) ? 1 : 0), 0);
      const subtreeAllSelected = subtree.length > 0 && selectedInSubtree === subtree.length;
      return (
        <div className="flex items-stretch">
          <IndentGuides depth={depth} />
          <div className="flex min-w-0 flex-1 items-center gap-1 py-[3px] pr-2">
            <input
              type="checkbox"
              checked={subtreeAllSelected}
              ref={(el) => {
                if (el) el.indeterminate = !subtreeAllSelected && selectedInSubtree > 0;
              }}
              onChange={() => ctx.toggleSubtreeSelection(node)}
              className="h-3.5 w-3.5 shrink-0"
              title="Select all keys in this namespace"
              data-testid={`redis-namespace-checkbox-${node.path}`}
            />
            <button
              onClick={() => ctx.toggleNamespace(node.path)}
              className="flex h-5 w-5 shrink-0 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
              data-testid={`redis-namespace-toggle-${node.path}`}
              aria-label={`${isExpanded ? "Collapse" : "Expand"} ${node.path}`}
            >
              <ChevronRight className={`h-3.5 w-3.5 transition-transform duration-150 ${isExpanded ? "rotate-90" : ""}`} />
            </button>
            <button
              onClick={() => ctx.toggleNamespace(node.path)}
              className="flex min-w-0 flex-1 items-center gap-1.5 rounded-md px-1.5 py-1 text-left text-[13px] font-medium text-foreground/90 transition-colors hover:bg-accent"
              data-testid={`redis-namespace-${node.path}`}
            >
              <Folder className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
              <span className="min-w-0 flex-1 truncate">{node.name}</span>
              <span className="shrink-0 rounded-full bg-muted px-1.5 py-px text-[10px] font-normal tabular-nums text-muted-foreground">
                {node.keyCount}
              </span>
            </button>
          </div>
        </div>
      );
    }

    const { key, node, depth } = row;
    const isSelected = ctx.selectedKey === key;
    const info = keyInfoByKey.get(key);
    const dotColorClass = (typeColors[info?.type ?? ""] ?? "text-muted-foreground").replace("text-", "bg-");
    return (
      <div className="flex items-stretch">
        <IndentGuides depth={depth + 1} />
        <div
          role="button"
          tabIndex={0}
          data-testid={`redis-key-${key}`}
          onClick={() => ctx.setSelectedKey(key)}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              // stopPropagation keeps this from also reaching the grid-nav
              // window listener (space would toggle the bulk checkbox too).
              e.preventDefault();
              e.stopPropagation();
              ctx.setSelectedKey(key);
            }
          }}
          className={`flex min-w-0 flex-1 cursor-pointer items-center gap-2 rounded-md px-2 py-[5px] text-left text-[13px] font-mono transition-colors ${
            isSelected ? "bg-accent text-foreground" : "text-muted-foreground hover:bg-accent hover:text-foreground"
          }`}
        >
          <input
            type="checkbox"
            checked={ctx.selectedKeys.has(key)}
            onChange={() => ctx.toggleKeySelection(key)}
            onClick={(e) => e.stopPropagation()}
            className="h-3.5 w-3.5 shrink-0"
            data-testid={`redis-key-checkbox-${key}`}
          />
          {info && (
            <span
              className={`h-1.5 w-1.5 shrink-0 rounded-full ${dotColorClass}`}
              title={info.type}
              data-testid={`redis-key-type-dot-${key}`}
            />
          )}
          <span className="truncate">
            {node.name === "(no prefix)" ? key : key.slice(node.path.length + ctx.separator.length) || key}
          </span>
          {info?.ttl && (
            <span
              className="shrink-0 rounded bg-muted px-1 py-0.5 text-[10px] tabular-nums text-muted-foreground"
              data-testid={`redis-key-ttl-badge-${key}`}
            >
              {formatTtl(info.ttl)}
            </span>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="w-1/3 border-r overflow-hidden flex flex-col" data-testid="redis-key-browser">
      <div className="p-3 border-b">
        <div className="flex gap-2">
          <input
            type="text"
            ref={searchInputRef}
            data-testid="redis-key-search"
            value={ctx.searchInput}
            onChange={(e) => ctx.setSearchInput(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && ctx.handleSearch()}
            placeholder="Pattern (e.g. user:*)"
            className="flex-1 rounded-md border bg-card px-3 py-1.5 text-sm"
          />
          <button
            data-testid="redis-key-search-btn"
            onClick={ctx.handleSearch}
            className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90"
          >
            Search
          </button>
        </div>
        <div className="mt-2 flex items-center gap-2">
          <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
            Separator
            <input
              type="text"
              value={separatorDraft}
              onChange={(e) => setSeparatorDraft(e.target.value)}
              onBlur={commitSeparator}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  commitSeparator();
                  (e.target as HTMLInputElement).blur();
                }
              }}
              className="w-16 rounded border bg-card px-1.5 py-0.5 font-mono text-xs"
              data-testid="redis-separator-input"
            />
          </label>
          <span className="ml-auto text-xs text-muted-foreground" data-testid="redis-key-count">
            {ctx.displayKeys.length} keys loaded
            {ctx.scanResult.data?.isComplete ? " (all)" : ""}
          </span>
          <button
            onClick={ctx.collapseAllNamespaces}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="redis-collapse-all"
            title="Collapse all namespaces"
          >
            <ChevronsDownUp className="h-3.5 w-3.5" />
            Collapse all
          </button>
          <button
            onClick={ctx.expandAllNamespaces}
            className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
            data-testid="redis-expand-all"
            title="Expand all namespaces"
          >
            <ChevronsUpDown className="h-3.5 w-3.5" />
            Expand all
          </button>
        </div>
        <div className="mt-2 flex items-center gap-2 border-t pt-2 text-xs">
          <label
            className="flex items-center gap-1.5"
            title="Select or clear every currently loaded matching key"
          >
            <input
              type="checkbox"
              checked={ctx.allLoadedSelected}
              ref={(el) => {
                if (el) el.indeterminate = ctx.someLoadedSelected;
              }}
              onChange={ctx.toggleSelectAllLoaded}
              disabled={ctx.displayKeys.length === 0}
              data-testid="redis-select-all-loaded"
            />
            Select all loaded
          </label>
          <span className="text-muted-foreground" data-testid="redis-batch-count">
            {ctx.selectedKeys.size} selected of {ctx.displayKeys.length} loaded
          </span>
          <span className="ml-auto flex items-center gap-1">
            <button
              onClick={() => ctx.setSelectedKeys(new Set())}
              disabled={ctx.selectedKeys.size === 0}
              title={ctx.selectedKeys.size === 0 ? "Select keys first" : undefined}
              className="rounded border px-2 py-1 hover:bg-accent disabled:opacity-50"
              data-testid="redis-batch-clear"
            >
              Clear
            </button>
            <button
              onClick={ctx.handleExportSelected}
              disabled={ctx.selectedKeys.size === 0}
              title={ctx.selectedKeys.size === 0 ? "Select keys to export" : undefined}
              className="rounded border px-2 py-1 hover:bg-accent disabled:opacity-50"
              data-testid="redis-batch-export"
            >
              Export JSON
            </button>
            <button
              onClick={ctx.handleBatchDelete}
              disabled={ctx.selectedKeys.size === 0}
              title={ctx.selectedKeys.size === 0 ? "Select keys to delete" : undefined}
              className="rounded border border-destructive px-2 py-1 text-destructive hover:bg-destructive/10 disabled:opacity-50"
              data-testid="redis-batch-delete"
            >
              Delete
            </button>
          </span>
        </div>
      </div>

      <div ref={ctx.redisTreeRef} className="flex-1 overflow-auto" data-testid="redis-key-tree-scroll">
        <QueryState
          isLoading={ctx.scanResult.isLoading}
          error={ctx.scanResult.error}
          data={ctx.namespaceTree}
          emptyTitle="No keys found"
          emptyDescription="Adjust the pattern or load more keys."
          skeletonRows={8}
        >
          {() => null}
        </QueryState>
        {ctx.flatRedisRows.length > 0 && (
          <div
            style={{ height: `${redisVirtualizer.getTotalSize()}px`, position: "relative", width: "100%" }}
            data-testid="redis-key-tree-virtualizer"
          >
            {virtualItems.map((item) => {
              const row = ctx.flatRedisRows[item.index];
              const isFocused = gridNav.focusedIndex === item.index;
              return (
                <div
                  key={item.key}
                  data-index={item.index}
                  data-grid-nav-row={item.index}
                  ref={redisVirtualizer.measureElement}
                  tabIndex={isFocused ? 0 : -1}
                  onFocus={() => gridNav.setFocusedIndex(item.index)}
                  className={`outline-none ${isFocused ? "bg-accent/40 rounded-md" : ""}`}
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: "100%",
                    transform: `translateY(${item.start}px)`,
                  }}
                >
                  {renderFlatRedisRow(row)}
                </div>
              );
            })}
          </div>
        )}
        {ctx.scanResult.data && !ctx.scanResult.data.isComplete && (
          <div className="flex gap-2 border-t px-3 py-2">
            <button
              data-testid="redis-load-more"
              onClick={ctx.handleLoadMore}
              className="flex-1 rounded border px-3 py-1.5 text-sm text-primary hover:bg-accent"
            >
              Load more
            </button>
            {ctx.displayKeys.length < 1000 && (
              <button
                data-testid="redis-load-all"
                onClick={ctx.handleLoadAll}
                disabled={ctx.loadAllActive}
                title={ctx.loadAllActive ? "Loading all keys…" : undefined}
                className="flex-1 rounded border px-3 py-1.5 text-sm text-primary hover:bg-accent disabled:opacity-50"
              >
                {ctx.loadAllActive ? "Loading all..." : "Load all"}
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
