import { ChevronRight, ChevronsDownUp, Folder } from "lucide-react";
import { useRedisPageContext, type FlatRedisRow } from "../RedisPageContext";

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
  const ctx = useRedisPageContext();

  const renderFlatRedisRow = (row: FlatRedisRow) => {
    if (row.kind === "namespace") {
      const { node, depth } = row;
      const isExpanded = ctx.expandedNamespaces.has(node.path);
      return (
        <div className="flex items-stretch">
          <IndentGuides depth={depth} />
          <div className="flex min-w-0 flex-1 items-center gap-1 py-[3px] pr-2">
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
    const isSelected = ctx.batchMode ? ctx.selectedKeys.has(key) : ctx.selectedKey === key;
    return (
      <div className="flex items-stretch">
        <IndentGuides depth={depth + 1} />
        <div
          role="button"
          tabIndex={0}
          data-testid={`redis-key-${key}`}
          onClick={() => (ctx.batchMode ? ctx.toggleKeySelection(key) : ctx.setSelectedKey(key))}
          onKeyDown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault();
              if (ctx.batchMode) ctx.toggleKeySelection(key);
              else ctx.setSelectedKey(key);
            }
          }}
          className={`flex min-w-0 flex-1 cursor-pointer items-center gap-2 rounded-md px-2 py-[5px] text-left text-[13px] font-mono transition-colors ${
            isSelected ? "bg-accent text-foreground" : "text-muted-foreground hover:bg-accent hover:text-foreground"
          }`}
        >
          {ctx.batchMode && (
            <input
              type="checkbox"
              checked={ctx.selectedKeys.has(key)}
              onChange={() => ctx.toggleKeySelection(key)}
              onClick={(e) => e.stopPropagation()}
              className="h-3.5 w-3.5 shrink-0"
              data-testid={`redis-key-checkbox-${key}`}
            />
          )}
          <span className="truncate">
            {node.name === "(no prefix)" ? key : key.slice(node.path.length + ctx.separator.length) || key}
          </span>
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
              value={ctx.separator}
              onChange={(e) => ctx.setSeparator(e.target.value || ":")}
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
            onClick={() => { ctx.setBatchMode(!ctx.batchMode); ctx.setSelectedKeys(new Set()); }}
            className={`rounded border px-2 py-1 text-xs ${ctx.batchMode ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
            data-testid="redis-batch-toggle"
          >
            {ctx.batchMode ? "Exit Batch" : "Batch Select"}
          </button>
          {ctx.batchMode && ctx.selectedKeys.size > 0 && (
            <>
              <span className="text-xs text-muted-foreground" data-testid="redis-batch-count">{ctx.selectedKeys.size} selected</span>
              <button onClick={ctx.handleExportSelected} className="rounded border px-2 py-1 text-xs hover:bg-accent" data-testid="redis-batch-export">Export JSON</button>
              <button onClick={ctx.handleBatchDelete} className="rounded border border-destructive px-2 py-1 text-xs text-destructive hover:bg-destructive/10" data-testid="redis-batch-delete">Delete</button>
              <button onClick={() => ctx.setSelectedKeys(new Set())} className="text-xs text-muted-foreground" data-testid="redis-batch-clear">Clear</button>
            </>
          )}
        </div>
      </div>

      <div ref={ctx.redisTreeRef} className="flex-1 overflow-auto" data-testid="redis-key-tree-scroll">
        {ctx.scanResult.isLoading && (
          <div className="p-3 text-sm text-muted-foreground">Loading keys...</div>
        )}
        {ctx.scanResult.error && (
          <div className="p-3 text-sm text-destructive" data-testid="redis-key-error">
            Error: {ctx.scanResult.error.message}
          </div>
        )}
        {ctx.namespaceTree.length === 0 && !ctx.scanResult.isLoading && (
          <div className="p-3 text-sm text-muted-foreground">No keys found</div>
        )}
        {ctx.flatRedisRows.length > 0 && (
          <div
            style={{ height: `${ctx.redisVirtualizer.getTotalSize()}px`, position: "relative", width: "100%" }}
            data-testid="redis-key-tree-virtualizer"
          >
            {ctx.redisVirtualizer.getVirtualItems().map((item) => {
              const row = ctx.flatRedisRows[item.index];
              return (
                <div
                  key={item.key}
                  data-index={item.index}
                  ref={ctx.redisVirtualizer.measureElement}
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
