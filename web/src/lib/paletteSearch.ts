// Shared fuzzy-match scoring for command-palette-style pickers (global nav palette,
// Service Bus entity palette, and any future domain-scoped palette). Centralizing this
// keeps search behavior consistent instead of each palette growing its own matcher.
// See docs/features/active/tauri-react-primary-tool/technical-plan.md Module 5.4.

export function fuzzyScore(query: string, text: string): number {
  const q = query.toLowerCase().trim();
  const t = text.toLowerCase();
  if (!q) return 0;
  if (t === q) return 10000;
  if (t.startsWith(q)) return 5000 - t.length;

  let idx = 0;
  let score = 0;
  let consecutive = 0;
  for (let i = 0; i < q.length; i++) {
    const pos = t.indexOf(q[i], idx);
    if (pos === -1) return -1;
    if (pos === idx) {
      consecutive++;
      score += 20 + consecutive * 10;
    } else {
      consecutive = 0;
      score += 2;
      if (
        pos === 0 ||
        t[pos - 1] === " " ||
        t[pos - 1] === "-" ||
        t[pos - 1] === "/" ||
        t[pos - 1] === ">" ||
        t[pos - 1] === "•"
      ) {
        score += 8;
      }
    }
    idx = pos + 1;
  }
  score -= (t.length - q.length) * 0.5;
  return score;
}

/** Scores and sorts `items` against `query` using `toSearchText` to build the matched
 * text for each item. Items that don't match at all (score <= 0) are dropped. Pass an
 * empty query to get every item back unscored, in its original order. */
export function fuzzyFilter<T>(query: string, items: T[], toSearchText: (item: T) => string): T[] {
  const q = query.trim();
  if (!q) return items;
  return items
    .map((item) => ({ item, score: fuzzyScore(q, toSearchText(item)) }))
    .filter(({ score }) => score > 0)
    .sort((a, b) => b.score - a.score)
    .map(({ item }) => item);
}
