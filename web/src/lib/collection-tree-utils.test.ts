import { describe, it, expect } from "vitest";
import type { ApiCollection, ApiCollectionNode } from "./types";
import {
  DEMO_COLLECTION_ID,
  moveCollection,
  moveNode,
  isDescendant,
  resolveDropTarget,
  type FlatRow,
} from "./collection-tree-utils";

function node(id: string, type: "Folder" | "Request" = "Request", children: ApiCollectionNode[] = []): ApiCollectionNode {
  return {
    id,
    type,
    name: id,
    isExpanded: true,
    children,
    defaultAuth: null,
    request: type === "Request" ? { id, name: id, method: "Get", url: "" } as any : null,
  };
}

function collection(id: string, nodes: ApiCollectionNode[] = []): ApiCollection {
  return {
    id,
    name: id,
    nodes,
    variables: [],
    defaultAuth: null,
    createdAt: "",
    updatedAt: "",
  };
}

function flatRow(
  id: string,
  collectionId: string,
  isCollection: boolean,
  type: "Folder" | "Request" = "Request",
): FlatRow {
  return {
    id,
    collectionId,
    depth: 0,
    isCollection,
    node: node(id, type, []),
  };
}

describe("isDescendant", () => {
  it("detects a descendant", () => {
    const child = node("child");
    const folder = node("folder", "Folder", [child]);
    expect(isDescendant([folder], "folder", "child")).toBe(true);
  });

  it("is false for an unrelated node", () => {
    const a = node("a", "Folder", [node("child")]);
    const b = node("b");
    expect(isDescendant([a, b], "a", "b")).toBe(false);
  });
});

describe("moveNode", () => {
  it("moves a request to the end of a folder", () => {
    const request = node("req");
    const folder = node("folder", "Folder", []);
    const collections = [collection("c1", [request, folder])];

    const result = moveNode(collections, "req", {
      targetCollectionId: "c1",
      targetNodeId: "folder",
      placement: "inside",
    });

    expect(result[0].nodes[0].children.map((c) => c.id)).toEqual(["req"]);
    expect(result[0].nodes.map((n) => n.id)).toEqual(["folder"]);
  });

  it("moves a request before another request in the same list", () => {
    const collections = [collection("c1", [node("a"), node("b"), node("c")])];

    const result = moveNode(collections, "c", {
      targetCollectionId: "c1",
      targetNodeId: "a",
      placement: "before",
    });

    expect(result[0].nodes.map((n) => n.id)).toEqual(["c", "a", "b"]);
  });

  it("moves a request after another request in the same list", () => {
    const collections = [collection("c1", [node("a"), node("b"), node("c")])];

    const result = moveNode(collections, "a", {
      targetCollectionId: "c1",
      targetNodeId: "b",
      placement: "after",
    });

    expect(result[0].nodes.map((n) => n.id)).toEqual(["b", "a", "c"]);
  });

  it("moves a request into another collection", () => {
    const collections = [collection("c1", [node("req")]), collection("c2", [])];

    const result = moveNode(collections, "req", {
      targetCollectionId: "c2",
      targetNodeId: undefined,
      placement: "inside",
    });

    expect(result[0].nodes).toHaveLength(0);
    expect(result[1].nodes.map((n) => n.id)).toEqual(["req"]);
  });

  it("prevents dropping a folder into itself", () => {
    const folder = node("folder", "Folder", [node("child")]);
    const collections = [collection("c1", [folder])];

    const result = moveNode(collections, "folder", {
      targetCollectionId: "c1",
      targetNodeId: "folder",
      placement: "inside",
    });

    expect(result[0].nodes[0].id).toBe("folder");
    expect(result[0].nodes[0].children[0].id).toBe("child");
  });

  it("prevents dropping a folder into its own descendant", () => {
    const child = node("child", "Folder", []);
    const folder = node("folder", "Folder", [child]);
    const collections = [collection("c1", [folder])];

    const result = moveNode(collections, "folder", {
      targetCollectionId: "c1",
      targetNodeId: "child",
      placement: "inside",
    });

    expect(result[0].nodes[0].id).toBe("folder");
    expect(result[0].nodes[0].children[0].id).toBe("child");
  });

  it("falls back to 'after' when dropping inside a request", () => {
    const a = node("a");
    const b = node("b");
    const collections = [collection("c1", [a, b])];

    const result = moveNode(collections, "b", {
      targetCollectionId: "c1",
      targetNodeId: "a",
      placement: "inside",
    });

    expect(result[0].nodes.map((n) => n.id)).toEqual(["a", "b"]);
  });
});

describe("moveNode into and within nested folders", () => {
  /**
   * outer/
   *   inner/
   *     deep-a
   *     deep-b
   *   sibling-in-outer
   * top-level-request
   * other-top-folder/
   *   other-child
   */
  function nested(): ApiCollection[] {
    return [
      collection("c1", [
        node("outer", "Folder", [
          node("inner", "Folder", [node("deep-a"), node("deep-b")]),
          node("sibling-in-outer"),
        ]),
        node("top-level-request"),
        node("other-top-folder", "Folder", [node("other-child")]),
      ]),
    ];
  }

  function names(nodes: ApiCollectionNode[]): string[] {
    return nodes.map((n) => n.id);
  }

  function find(nodes: ApiCollectionNode[], id: string): ApiCollectionNode | null {
    for (const n of nodes) {
      if (n.id === id) return n;
      if (n.type === "Folder") {
        const found = find(n.children, id);
        if (found) return found;
      }
    }
    return null;
  }

  it("keeps the rest of the collection when dropping into a nested folder", () => {
    // Regression: the insert used to splice the array *directly containing* the
    // target and then assign it to `collection.nodes`, so dropping into a nested
    // folder replaced the whole collection with that one inner list — everything
    // else in it vanished.
    const result = moveNode(nested(), "top-level-request", {
      targetCollectionId: "c1",
      targetNodeId: "inner",
      placement: "inside",
    });

    expect(names(result[0].nodes)).toEqual(["outer", "other-top-folder"]);
    expect(names(find(result[0].nodes, "inner")!.children)).toEqual([
      "deep-a",
      "deep-b",
      "top-level-request",
    ]);
    expect(names(find(result[0].nodes, "other-top-folder")!.children)).toEqual(["other-child"]);
    expect(names(find(result[0].nodes, "outer")!.children)).toEqual(["inner", "sibling-in-outer"]);
  });

  it("keeps the rest of the collection when reordering inside a nested folder", () => {
    const result = moveNode(nested(), "deep-b", {
      targetCollectionId: "c1",
      targetNodeId: "deep-a",
      placement: "before",
    });

    expect(names(result[0].nodes)).toEqual(["outer", "top-level-request", "other-top-folder"]);
    expect(names(find(result[0].nodes, "inner")!.children)).toEqual(["deep-b", "deep-a"]);
  });

  it("moves a node out of a nested folder to the collection root", () => {
    const result = moveNode(nested(), "deep-a", {
      targetCollectionId: "c1",
      targetNodeId: "top-level-request",
      placement: "after",
    });

    expect(names(result[0].nodes)).toEqual([
      "outer",
      "top-level-request",
      "deep-a",
      "other-top-folder",
    ]);
    expect(names(find(result[0].nodes, "inner")!.children)).toEqual(["deep-b"]);
  });

  it("moves a whole nested folder into another folder, children intact", () => {
    const result = moveNode(nested(), "inner", {
      targetCollectionId: "c1",
      targetNodeId: "other-top-folder",
      placement: "inside",
    });

    expect(names(result[0].nodes)).toEqual(["outer", "top-level-request", "other-top-folder"]);
    expect(names(find(result[0].nodes, "outer")!.children)).toEqual(["sibling-in-outer"]);
    expect(names(find(result[0].nodes, "other-top-folder")!.children)).toEqual([
      "other-child",
      "inner",
    ]);
    expect(names(find(result[0].nodes, "inner")!.children)).toEqual(["deep-a", "deep-b"]);
  });

  it("treats a drop inside a request as a drop after it", () => {
    const result = moveNode(nested(), "top-level-request", {
      targetCollectionId: "c1",
      targetNodeId: "deep-a",
      placement: "inside",
    });

    expect(names(find(result[0].nodes, "inner")!.children)).toEqual([
      "deep-a",
      "top-level-request",
      "deep-b",
    ]);
    expect(names(result[0].nodes)).toEqual(["outer", "other-top-folder"]);
  });

  it("refuses to move a folder into its own descendant", () => {
    const before = nested();
    const result = moveNode(before, "outer", {
      targetCollectionId: "c1",
      targetNodeId: "deep-a",
      placement: "after",
    });
    expect(result).toBe(before);
  });

  it("never loses or duplicates a node, whatever the placement", () => {
    const ids = ["outer", "inner", "deep-a", "deep-b", "sibling-in-outer", "top-level-request", "other-top-folder", "other-child"];
    const collect = (nodes: ApiCollectionNode[]): string[] =>
      nodes.flatMap((n) => [n.id, ...(n.type === "Folder" ? collect(n.children) : [])]);

    for (const source of ids) {
      for (const targetNodeId of ids) {
        for (const placement of ["before", "after", "inside"] as const) {
          const result = moveNode(nested(), source, { targetCollectionId: "c1", targetNodeId, placement });
          const seen = collect(result[0].nodes).sort();
          expect(seen, `${source} -> ${placement} ${targetNodeId}`).toEqual([...ids].sort());
        }
      }
    }
  });

  it("moves a node across collections without disturbing either tree", () => {
    const collections = [
      ...nested(),
      collection("c2", [node("c2-folder", "Folder", [node("c2-child")])]),
    ];
    const result = moveNode(collections, "deep-a", {
      targetCollectionId: "c2",
      targetNodeId: "c2-child",
      placement: "after",
    });

    expect(names(find(result[0].nodes, "inner")!.children)).toEqual(["deep-b"]);
    expect(names(result[0].nodes)).toEqual(["outer", "top-level-request", "other-top-folder"]);
    expect(names(find(result[1].nodes, "c2-folder")!.children)).toEqual(["c2-child", "deep-a"]);
  });
});

describe("moveCollection", () => {
  it("moves a collection before another", () => {
    const collections = [collection("a"), collection("b"), collection("c")];

    const result = moveCollection(collections, "c", {
      targetCollectionId: "a",
      placement: "before",
    });

    expect(result.map((c) => c.id)).toEqual(["c", "a", "b"]);
  });

  it("moves a collection after another", () => {
    const collections = [collection("a"), collection("b"), collection("c")];

    const result = moveCollection(collections, "a", {
      targetCollectionId: "b",
      placement: "after",
    });

    expect(result.map((c) => c.id)).toEqual(["b", "a", "c"]);
  });

  it("pins the demo collection at index 0", () => {
    const demo = collection(DEMO_COLLECTION_ID);
    const collections = [demo, collection("a"), collection("b")];

    const result = moveCollection(collections, "b", {
      targetCollectionId: DEMO_COLLECTION_ID,
      placement: "before",
    });

    expect(result.map((c) => c.id)).toEqual([DEMO_COLLECTION_ID, "b", "a"]);
  });

  it("does not move the demo collection", () => {
    const collections = [collection(DEMO_COLLECTION_ID), collection("a")];

    const result = moveCollection(collections, DEMO_COLLECTION_ID, {
      targetCollectionId: "a",
      placement: "after",
    });

    expect(result.map((c) => c.id)).toEqual([DEMO_COLLECTION_ID, "a"]);
  });
});

describe("resolveDropTarget", () => {
  it("resolves collection drop before target", () => {
    const dragging = flatRow("c2", "c2", true);
    const target = flatRow("c1", "c1", true);
    const rect = { top: 0, height: 40 } as DOMRect;

    const result = resolveDropTarget(dragging, target, 10, rect);
    expect(result).toEqual({
      kind: "collection",
      target: { targetCollectionId: "c1", placement: "before" },
    });
  });

  it("resolves collection drop after target", () => {
    const dragging = flatRow("c2", "c2", true);
    const target = flatRow("c1", "c1", true);
    const rect = { top: 0, height: 40 } as DOMRect;

    const result = resolveDropTarget(dragging, target, 30, rect);
    expect(result).toEqual({
      kind: "collection",
      target: { targetCollectionId: "c1", placement: "after" },
    });
  });

  it("resolves node drop inside a folder", () => {
    const dragging = flatRow("req", "c1", false, "Request");
    const target = flatRow("folder", "c1", false, "Folder");
    const rect = { top: 0, height: 40 } as DOMRect;

    const result = resolveDropTarget(dragging, target, 20, rect);
    expect(result).toEqual({
      kind: "node",
      target: { targetCollectionId: "c1", targetNodeId: "folder", placement: "inside" },
    });
  });

  it("falls back to 'after' when dropping inside a request", () => {
    const dragging = flatRow("b", "c1", false, "Request");
    const target = flatRow("a", "c1", false, "Request");
    const rect = { top: 0, height: 40 } as DOMRect;

    const result = resolveDropTarget(dragging, target, 20, rect);
    expect(result).toEqual({
      kind: "node",
      target: { targetCollectionId: "c1", targetNodeId: "a", placement: "after" },
    });
  });
});
