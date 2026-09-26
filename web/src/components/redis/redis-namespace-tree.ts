export type NamespaceNode = {
  name: string;
  path: string;
  children: Map<string, NamespaceNode>;
  keys: string[];
  keyCount: number;
};

export function buildNamespaceTree(keys: string[], separator: string): NamespaceNode[] {
  const roots = new Map<string, NamespaceNode>();

  for (const key of keys) {
    const parts = key.split(separator);

    if (parts.length < 2) {
      let fallback = roots.get("(no prefix)");
      if (!fallback) {
        fallback = { name: "(no prefix)", path: "(no prefix)", children: new Map(), keys: [], keyCount: 0 };
        roots.set("(no prefix)", fallback);
      }
      fallback.keys.push(key);
      fallback.keyCount += 1;
      continue;
    }

    const namespaceParts = parts.slice(0, -1);
    let nodes = roots;
    let path = "";
    namespaceParts.forEach((name, index) => {
      path = index === 0 ? name : `${path}${separator}${name}`;
      let node = nodes.get(name);
      if (!node) {
        node = { name, path, children: new Map(), keys: [], keyCount: 0 };
        nodes.set(name, node);
      }
      node.keyCount += 1;
      nodes = node.children;
      if (index === namespaceParts.length - 1) {
        node.keys.push(key);
      }
    });
  }

  return [...roots.values()];
}

export type FlatRedisRow =
  | { kind: "namespace"; node: NamespaceNode; depth: number }
  | { kind: "key"; key: string; node: NamespaceNode; depth: number };

export function flattenNamespaceTree(
  nodes: NamespaceNode[],
  expandedNamespaces: Set<string>,
  depth = 0,
): FlatRedisRow[] {
  const rows: FlatRedisRow[] = [];
  for (const node of nodes) {
    rows.push({ kind: "namespace", node, depth });
    if (expandedNamespaces.has(node.path)) {
      rows.push(...flattenNamespaceTree([...node.children.values()], expandedNamespaces, depth + 1));
      for (const key of node.keys) {
        rows.push({ kind: "key", key, node, depth });
      }
    }
  }
  return rows;
}

export function redisRowKey(row: FlatRedisRow): string {
  return row.kind === "namespace" ? `ns:${row.node.path}` : `key:${row.key}`;
}

/**
 * Every namespace path in the tree, recursively. Used by "Expand all" — the deliberate,
 * user-triggered counterpart to "Collapse all".
 */
export function collectAllNamespacePaths(nodes: NamespaceNode[]): Set<string> {
  const paths = new Set<string>();
  const walk = (list: NamespaceNode[]) => {
    for (const node of list) {
      paths.add(node.path);
      walk([...node.children.values()]);
    }
  };
  walk(nodes);
  return paths;
}

/**
 * Every key in a namespace's subtree — its own keys plus all descendants'. Used by the
 * namespace-row selection checkbox, which selects or clears the whole subtree in one click
 * (same behavior as the MAUI browser's namespace checkboxes).
 */
export function collectSubtreeKeys(node: NamespaceNode): string[] {
  const keys = [...node.keys];
  for (const child of node.children.values()) keys.push(...collectSubtreeKeys(child));
  return keys;
}
