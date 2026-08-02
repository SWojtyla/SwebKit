import { ChevronRight, ChevronDown } from "lucide-react";
import { useRedisPageContext, type FlatRedisRow } from "../RedisPageContext";

export function KeyBrowserPanel() {
  const ctx = useRedisPageContext();

  const renderFlatRedisRow = (row: FlatRedisRow) => {
    if (row.kind === "namespace") {
      const { node, depth } = row;
      const isExpanded = ctx.expandedNamespaces.has(node.path);
      return (
        <div style={{ paddingLeft: `${depth * 0.75}rem` }}>
          <div className="flex items-center">
            <button
              onClick={() => ctx.toggleNamespace(node.path)}
              className="p-0.5 text-muted-foreground hover:text-foreground"
              data-testid={`redis-namespace-toggle-${node.path}`}
              aria-label={`${isExpanded ? "Collapse" : "Expand"} ${node.path}`}
            >
              {isExpanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
            </button>
            <button
              onClick={() => ctx.setNamespaceFilter(ctx.namespaceFilter === node.path ? null : node.path)}
              className={`flex-1 rounded px-2 py-0.5 text-left text-xs font-mono ${ctx.namespaceFilter === node.path ? "bg-primary text-primary-foreground" : "hover:bg-accent"}`}
              data-testid={`redis-namespace-${node.path}`}
            >
              {node.name} ({node.keyCount})
            </button>
          </div>
        </div>
      );
    }

    const { key, node, depth } = row;
    return (
      <div style={{ paddingLeft: `${depth * 0.75}rem` }}>
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
          className={`flex w-full items-center gap-2 rounded px-2 py-0.5 text-left text-xs font-mono cursor-pointer ${
            ctx.batchMode ? (ctx.selectedKeys.has(key) ? "bg-primary/20" : "") : ctx.selectedKey === key ? "bg-accent" : "hover:bg-accent"
          }`}
        >
          {ctx.batchMode && (
            <input
              type="checkbox"
              checked={ctx.selectedKeys.has(key)}
              onChange={() => ctx.toggleKeySelection(key)}
              onClick={(e) => e.stopPropagation()}
              className="h-3.5 w-3.5"
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
        {ctx.namespaceTree.length > 0 && ctx.namespaceFilter && (
          <button
            onClick={() => ctx.setNamespaceFilter(null)}
            className="mb-2 w-full rounded px-2 py-1 text-left text-xs font-mono hover:bg-accent"
            data-testid="redis-namespace-clear-filter"
          >
            ← All namespaces ({ctx.displayKeys.length})
          </button>
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
