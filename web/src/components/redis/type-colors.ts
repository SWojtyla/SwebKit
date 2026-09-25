// Shared so `KeyBrowserPanel` can reuse the same type→color mapping for the tree's type dot
// (derived by swapping the `text-` prefix for `bg-`) instead of duplicating the color choices.
export const typeColors: Record<string, string> = {
  string: "text-green-400",
  hash: "text-blue-400",
  list: "text-yellow-400",
  set: "text-purple-400",
  zset: "text-orange-400",
  none: "text-muted-foreground",
};
