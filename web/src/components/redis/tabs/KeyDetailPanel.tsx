import { Copy, Pencil, Check, X, Clock, Trash2, Plus } from "lucide-react";
import { formatTtl, parseTtl, getTtlColorClass, formatBytes } from "@/lib/redis-format";
import { useRedisPageContext } from "../RedisPageContext";

const typeColors: Record<string, string> = {
  string: "text-green-400",
  hash: "text-blue-400",
  list: "text-yellow-400",
  set: "text-purple-400",
  zset: "text-orange-400",
  none: "text-muted-foreground",
};

function TtlBar({ ttl }: { ttl: string | null }) {
  const ms = parseTtl(ttl);
  if (ms === null || ms <= 0) return null;

  const maxMs = 3600_000;
  const pct = Math.min(100, (ms / maxMs) * 100);
  const colorClass = getTtlColorClass(ms);

  return (
    <div className="mt-2" data-testid="redis-ttl-bar">
      <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
        <div className={`h-full ${colorClass} transition-all`} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

export function KeyDetailPanel() {
  const ctx = useRedisPageContext();

  return (
    <div className="flex-1 overflow-auto" data-testid="redis-key-detail">
      {!ctx.selectedKey ? (
        <div className="flex h-full items-center justify-center text-muted-foreground" data-testid="redis-no-key-selected">
          Select a key to view details
        </div>
      ) : ctx.keyInfo.isLoading ? (
        <div className="p-6 text-sm text-muted-foreground">Loading key info...</div>
      ) : ctx.keyInfo.error ? (
        <div className="p-6 text-sm text-destructive">Error: {ctx.keyInfo.error.message}</div>
      ) : ctx.keyInfo.data ? (
        <div className="p-6 space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex-1 min-w-0">
              {ctx.renaming ? (
                <div className="flex items-center gap-2">
                  <input
                    type="text"
                    value={ctx.renameValue}
                    onChange={(e) => ctx.setRenameValue(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && ctx.handleRenameKey(ctx.keyInfo.data!.key)}
                    className="rounded border bg-card px-2 py-1 text-sm font-mono flex-1"
                    autoFocus
                    data-testid="redis-rename-input"
                  />
                  <button onClick={() => ctx.handleRenameKey(ctx.keyInfo.data!.key)} className="rounded bg-primary p-1 text-primary-foreground" data-testid="redis-rename-confirm">
                    <Check className="h-3.5 w-3.5" />
                  </button>
                  <button onClick={() => ctx.setRenaming(false)} className="rounded border p-1" data-testid="redis-rename-cancel">
                    <X className="h-3.5 w-3.5" />
                  </button>
                </div>
              ) : (
                <div className="flex items-center gap-2">
                  <div className="text-lg font-mono font-semibold truncate" data-testid="redis-detail-key-name">
                    {ctx.keyInfo.data.key}
                  </div>
                  <button onClick={() => ctx.handleCopyKey(ctx.keyInfo.data!.key)} className="text-muted-foreground hover:text-foreground" data-testid="redis-copy-key-btn" title="Copy key name">
                    <Copy className="h-3.5 w-3.5" />
                  </button>
                  <button onClick={() => { ctx.setRenaming(true); ctx.setRenameValue(ctx.keyInfo.data!.key); }} className="text-muted-foreground hover:text-foreground" data-testid="redis-rename-btn" title="Rename key">
                    <Pencil className="h-3.5 w-3.5" />
                  </button>
                </div>
              )}
              <div className="mt-1 flex items-center gap-3 text-sm">
                <span className={`font-medium ${typeColors[ctx.keyInfo.data.type] ?? ""}`} data-testid="redis-detail-key-type">
                  {ctx.keyInfo.data.type}
                </span>
                <span className="text-muted-foreground" data-testid="redis-detail-key-ttl">
                  TTL: {formatTtl(ctx.keyInfo.data.ttl)}
                </span>
                <span className="text-muted-foreground" data-testid="redis-detail-key-memory">
                  {formatBytes(ctx.keyInfo.data.memoryBytes)}
                </span>
                {ctx.keyInfo.data.encoding && (
                  <span className="text-muted-foreground">enc: {ctx.keyInfo.data.encoding}</span>
                )}
              </div>
              {/* TTL bar */}
              {ctx.keyInfo.data.ttl && ctx.keyInfo.data.type !== "none" && (
                <TtlBar ttl={ctx.keyInfo.data.ttl} />
              )}
              {/* TTL controls */}
              {ctx.showTtlEditor ? (
                <div className="mt-2 flex items-center gap-2" data-testid="redis-ttl-editor">
                  <input
                    type="number"
                    value={ctx.ttlSeconds}
                    onChange={(e) => ctx.setTtlSeconds(parseInt(e.target.value) || 0)}
                    placeholder="seconds"
                    className="w-24 rounded border bg-card px-2 py-1 text-xs"
                    autoFocus
                  />
                  <button onClick={() => ctx.handleSetTtl(ctx.keyInfo.data!.key)} className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground" data-testid="redis-ttl-set-btn">Set TTL</button>
                  <button onClick={() => ctx.handleRemoveTtl(ctx.keyInfo.data!.key)} className="rounded border px-2 py-1 text-xs" data-testid="redis-ttl-remove-btn">Remove TTL</button>
                  <button onClick={() => ctx.setShowTtlEditor(false)} className="text-xs text-muted-foreground">Cancel</button>
                </div>
              ) : (
                <button onClick={() => { ctx.setShowTtlEditor(true); ctx.setTtlSeconds(3600); }} className="mt-2 flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid="redis-ttl-edit-btn">
                  <Clock className="h-3 w-3" /> Set TTL
                </button>
              )}
            </div>
            <button
              data-testid="redis-delete-key-btn"
              onClick={() => ctx.requestDeleteKey(ctx.keyInfo.data!.key)}
              disabled={ctx.deleteKey.isPending}
              className="flex items-center gap-1 rounded-md border border-destructive px-3 py-1.5 text-sm text-destructive hover:bg-destructive/10"
            >
              <Trash2 className="h-3.5 w-3.5" /> Delete
            </button>
          </div>

          {ctx.keyInfo.data.type === "string" && (
            <div>
              <div className="mb-2 flex items-center justify-between">
                <h3 className="text-sm font-semibold">Value</h3>
                {ctx.editingValue ? (
                  <div className="flex items-center gap-2">
                    <button onClick={() => ctx.handleSaveStringValue(ctx.keyInfo.data!.key)} disabled={ctx.setValue.isPending} className="rounded bg-primary px-2 py-1 text-xs text-primary-foreground" data-testid="redis-string-save-btn">
                      Save
                    </button>
                    <button onClick={() => ctx.setEditingValue(false)} className="rounded border px-2 py-1 text-xs" data-testid="redis-string-cancel-btn">
                      Cancel
                    </button>
                  </div>
                ) : (
                  <button onClick={() => { ctx.setEditingValue(true); ctx.setStringValue(ctx.keyValue.data?.value ?? ""); }} className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground" data-testid="redis-string-edit-btn">
                    <Pencil className="h-3 w-3" /> Edit
                  </button>
                )}
              </div>
              {ctx.editingValue ? (
                <textarea
                  value={ctx.stringValue}
                  onChange={(e) => ctx.setStringValue(e.target.value)}
                  className="w-full rounded-md border bg-card p-4 text-sm font-mono max-h-96 min-h-48"
                  data-testid="redis-detail-string-edit"
                  autoFocus
                />
              ) : (
                <pre
                  className="rounded-md border bg-muted p-4 text-sm font-mono overflow-auto max-h-96"
                  data-testid="redis-detail-string-value"
                >
                  {ctx.keyValue.data?.value ?? "(empty)"}
                </pre>
              )}
            </div>
          )}

          {ctx.keyInfo.data.type === "hash" && (
            <div>
              <div className="mb-2 flex items-center justify-between">
                <h3 className="text-sm font-semibold">Hash Fields</h3>
                {!ctx.hashAdding && (
                  <button
                    onClick={() => ctx.setHashAdding(true)}
                    className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent"
                    data-testid="redis-hash-add-btn"
                  >
                    <Plus className="h-3 w-3" /> Add field
                  </button>
                )}
              </div>
              <div className="rounded-md border overflow-hidden" data-testid="redis-detail-hash-fields">
                <table className="w-full text-sm">
                  <thead className="bg-muted">
                    <tr>
                      <th className="px-3 py-2 text-left font-medium">Field</th>
                      <th className="px-3 py-2 text-left font-medium">Value</th>
                      <th className="px-3 py-2 text-left font-medium w-24">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {ctx.hashAdding && (
                      <tr className="border-t">
                        <td className="px-3 py-2">
                          <input
                            type="text"
                            value={ctx.newHashField}
                            onChange={(e) => ctx.setNewHashField(e.target.value)}
                            placeholder="field"
                            className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                            data-testid="redis-hash-new-field"
                            autoFocus
                          />
                        </td>
                        <td className="px-3 py-2">
                          <input
                            type="text"
                            value={ctx.newHashValue}
                            onChange={(e) => ctx.setNewHashValue(e.target.value)}
                            placeholder="value"
                            className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                            data-testid="redis-hash-new-value"
                          />
                        </td>
                        <td className="px-3 py-2">
                          <div className="flex items-center gap-1">
                            <button
                              onClick={() => ctx.handleAddHashField(ctx.keyInfo.data!.key)}
                              className="rounded bg-primary p-1 text-primary-foreground"
                              data-testid="redis-hash-new-save"
                            >
                              <Check className="h-3 w-3" />
                            </button>
                            <button
                              onClick={() => ctx.setHashAdding(false)}
                              className="rounded border p-1"
                              data-testid="redis-hash-new-cancel"
                            >
                              <X className="h-3 w-3" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    )}
                    {ctx.hashFields.data?.map((f) => (
                      <tr key={f.field} className="border-t">
                        {ctx.hashEditingField === f.field ? (
                          <>
                            <td className="px-3 py-2">
                              <input
                                type="text"
                                value={ctx.hashEditFieldName}
                                onChange={(e) => ctx.setHashEditFieldName(e.target.value)}
                                onKeyDown={(e) => e.key === "Enter" && ctx.handleSaveHashField(ctx.keyInfo.data!.key, f.field)}
                                className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                                data-testid={`redis-hash-edit-field-${f.field}`}
                                autoFocus
                              />
                            </td>
                            <td className="px-3 py-2">
                              <input
                                type="text"
                                value={ctx.hashEditValue}
                                onChange={(e) => ctx.setHashEditValue(e.target.value)}
                                onKeyDown={(e) => e.key === "Enter" && ctx.handleSaveHashField(ctx.keyInfo.data!.key, f.field)}
                                className="w-full rounded border bg-card px-2 py-1 text-xs font-mono"
                                data-testid={`redis-hash-edit-value-${f.field}`}
                              />
                            </td>
                            <td className="px-3 py-2">
                              <div className="flex items-center gap-1">
                                <button
                                  onClick={() => ctx.handleSaveHashField(ctx.keyInfo.data!.key, f.field)}
                                  className="rounded bg-primary p-1 text-primary-foreground"
                                  data-testid={`redis-hash-save-${f.field}`}
                                >
                                  <Check className="h-3 w-3" />
                                </button>
                                <button
                                  onClick={() => ctx.setHashEditingField(null)}
                                  className="rounded border p-1"
                                  data-testid={`redis-hash-cancel-${f.field}`}
                                >
                                  <X className="h-3 w-3" />
                                </button>
                              </div>
                            </td>
                          </>
                        ) : (
                          <>
                            <td className="px-3 py-2 font-mono">{f.field}</td>
                            <td className="px-3 py-2 font-mono break-all">{f.value}</td>
                            <td className="px-3 py-2">
                              <div className="flex items-center gap-1">
                                <button
                                  onClick={() => { ctx.setHashEditingField(f.field); ctx.setHashEditFieldName(f.field); ctx.setHashEditValue(f.value); }}
                                  className="text-muted-foreground hover:text-foreground"
                                  data-testid={`redis-hash-edit-${f.field}`}
                                  title="Edit field"
                                >
                                  <Pencil className="h-3.5 w-3.5" />
                                </button>
                                <button
                                  onClick={() => ctx.requestDeleteHashField(ctx.keyInfo.data!.key, f.field)}
                                  className="text-destructive hover:text-destructive/80"
                                  data-testid={`redis-hash-delete-${f.field}`}
                                  title="Delete field"
                                >
                                  <Trash2 className="h-3.5 w-3.5" />
                                </button>
                              </div>
                            </td>
                          </>
                        )}
                      </tr>
                    ))}
                    {(!ctx.hashFields.data || ctx.hashFields.data.length === 0) && !ctx.hashAdding && (
                      <tr><td colSpan={3} className="px-3 py-4 text-center text-muted-foreground">No fields</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {ctx.keyInfo.data.type === "list" && (
            <div>
              <div className="mb-2 flex items-center justify-between">
                <h3 className="text-sm font-semibold">List Items</h3>
                {ctx.listItemsQuery.hasNextPage && (
                  <button
                    onClick={() => ctx.listItemsQuery.fetchNextPage()}
                    disabled={ctx.listItemsQuery.isFetchingNextPage}
                    className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                    data-testid="redis-list-load-more"
                  >
                    {ctx.listItemsQuery.isFetchingNextPage ? "Loading..." : "Load more"}
                  </button>
                )}
              </div>
              <div className="rounded-md border overflow-hidden" data-testid="redis-detail-list-items">
                <table className="w-full text-sm">
                  <thead className="bg-muted">
                    <tr>
                      <th className="px-3 py-2 text-left font-medium w-16">#</th>
                      <th className="px-3 py-2 text-left font-medium">Value</th>
                    </tr>
                  </thead>
                  <tbody>
                    {ctx.listItems.map((item, i) => (
                      <tr key={i} className="border-t">
                        <td className="px-3 py-2 text-muted-foreground">{i}</td>
                        <td className="px-3 py-2 font-mono break-all">{item}</td>
                      </tr>
                    ))}
                    {ctx.listItems.length === 0 && (
                      <tr><td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">No items</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {ctx.keyInfo.data.type === "set" && (
            <div>
              <div className="mb-2 flex items-center justify-between">
                <h3 className="text-sm font-semibold">Set Members</h3>
                {ctx.setMembersQuery.hasNextPage && (
                  <button
                    onClick={() => ctx.setMembersQuery.fetchNextPage()}
                    disabled={ctx.setMembersQuery.isFetchingNextPage}
                    className="flex items-center gap-1 rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                    data-testid="redis-set-load-more"
                  >
                    {ctx.setMembersQuery.isFetchingNextPage ? "Loading..." : "Load more"}
                  </button>
                )}
              </div>
              <div className="rounded-md border p-3" data-testid="redis-detail-set-members">
                {ctx.setMembers.length ? (
                  <div className="flex flex-wrap gap-2">
                    {ctx.setMembers.map((m) => (
                      <span key={m} className="rounded bg-muted px-2 py-1 text-sm font-mono">{m}</span>
                    ))}
                  </div>
                ) : (
                  <span className="text-sm text-muted-foreground">No members</span>
                )}
              </div>
            </div>
          )}

          {ctx.keyInfo.data.type === "zset" && (
            <div>
              <h3 className="mb-2 text-sm font-semibold">Sorted Set Members</h3>
              <div className="rounded-md border overflow-hidden" data-testid="redis-detail-zset-members">
                <table className="w-full text-sm">
                  <thead className="bg-muted">
                    <tr>
                      <th className="px-3 py-2 text-left font-medium">Member</th>
                      <th className="px-3 py-2 text-right font-medium w-32">Score</th>
                    </tr>
                  </thead>
                  <tbody>
                    {ctx.sortedSetMembers.data?.map((m) => (
                      <tr key={m.member} className="border-t">
                        <td className="px-3 py-2 font-mono">{m.member}</td>
                        <td className="px-3 py-2 text-right">
                          {ctx.zsetEditingMember === m.member ? (
                            <div className="flex items-center justify-end gap-2">
                              <input
                                type="number"
                                value={ctx.zsetEditScore}
                                onChange={(e) => ctx.setZsetEditScore(e.target.value)}
                                onKeyDown={(e) => e.key === "Enter" && ctx.handleSaveZsetScore(ctx.keyInfo.data!.key, m.member)}
                                className="w-24 rounded border bg-card px-2 py-1 text-xs font-mono text-right"
                                data-testid={`redis-zset-score-input-${m.member}`}
                                autoFocus
                              />
                              <button
                                onClick={() => ctx.handleSaveZsetScore(ctx.keyInfo.data!.key, m.member)}
                                className="rounded bg-primary p-1 text-primary-foreground"
                                data-testid={`redis-zset-score-save-${m.member}`}
                              >
                                <Check className="h-3 w-3" />
                              </button>
                              <button
                                onClick={() => ctx.setZsetEditingMember(null)}
                                className="rounded border p-1"
                                data-testid={`redis-zset-score-cancel-${m.member}`}
                              >
                                <X className="h-3 w-3" />
                              </button>
                            </div>
                          ) : (
                            <button
                              onClick={() => { ctx.setZsetEditingMember(m.member); ctx.setZsetEditScore(String(m.score)); }}
                              className="font-mono hover:underline"
                              data-testid={`redis-zset-score-${m.member}`}
                            >
                              {m.score}
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                    {(!ctx.sortedSetMembers.data || ctx.sortedSetMembers.data.length === 0) && (
                      <tr><td colSpan={2} className="px-3 py-4 text-center text-muted-foreground">No members</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {ctx.keyInfo.data.type === "none" && (
            <div className="text-sm text-muted-foreground" data-testid="redis-detail-key-not-found">
              Key does not exist
            </div>
          )}
        </div>
      ) : null}
    </div>
  );
}
