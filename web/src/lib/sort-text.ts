import { isValidElement, type ReactNode } from "react";

/** Recursively collects the visible text inside a rendered cell so a table column
 * can sort even without an explicit sortValue. Elements walk their children;
 * strings and numbers concatenate; null, booleans and non-text leaves (icons)
 * contribute nothing. The caller compares with a numeric-aware localeCompare so
 * extracted "2" vs "10" still orders correctly. */
export function extractSortText(node: ReactNode): string {
    if (node === null || node === undefined || typeof node === "boolean") {
        return "";
    }
    if (typeof node === "string" || typeof node === "number") {
        return String(node);
    }
    if (Array.isArray(node)) {
        return node.map(extractSortText).join("");
    }
    if (isValidElement<{ children?: ReactNode }>(node)) {
        return extractSortText(node.props.children);
    }
    return "";
}
