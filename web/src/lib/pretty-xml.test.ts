import { describe, expect, it } from "vitest";
import { tryPrettifyXml } from "./pretty-xml";

describe("tryPrettifyXml", () => {
    it("indents nested elements one per line", () => {
        const out = tryPrettifyXml("<root><a><b>x</b></a></root>");
        expect(out).toBe("<root>\n  <a>\n    <b>x</b>\n  </a>\n</root>");
    });

    it("keeps text-only elements on one line", () => {
        const out = tryPrettifyXml("<a><k>value</k></a>");
        expect(out).toBe("<a>\n  <k>value</k>\n</a>");
    });

    it("keeps empty elements collapsed", () => {
        expect(tryPrettifyXml("<a><b></b></a>")).toBe("<a>\n  <b></b>\n</a>");
        expect(tryPrettifyXml("<a><b/></a>")).toBe("<a>\n  <b/>\n</a>");
    });

    it("handles declarations, comments, CDATA and doctype", () => {
        const out = tryPrettifyXml(
            '<?xml version="1.0"?><!--note--><!DOCTYPE d><d><![CDATA[<raw>]]></d>',
        );
        expect(out).toBe(
            '<?xml version="1.0"?>\n<!--note-->\n<!DOCTYPE d>\n<d>\n  <![CDATA[<raw>]]>\n</d>',
        );
    });

    it("does not split a tag on > inside quoted attribute values", () => {
        const out = tryPrettifyXml('<a x="1>2"><b/></a>');
        expect(out).toBe('<a x="1>2">\n  <b/>\n</a>');
    });

    it("normalises attribute whitespace but leaves quoted values alone", () => {
        const out = tryPrettifyXml('<a\n   x="a  b"    y="c">t</a>');
        expect(out).toBe('<a x="a  b" y="c">t</a>');
    });

    it("formats a minified document as seen in file-share previews", () => {
        const minified =
            '<orders><order id="1"><item sku="A1" qty="2"/><item sku="B2" qty="1"/></order></orders>';
        const out = tryPrettifyXml(minified);
        expect(out).toBe(
            '<orders>\n' +
                '  <order id="1">\n' +
                '    <item sku="A1" qty="2"/>\n' +
                '    <item sku="B2" qty="1"/>\n' +
                '  </order>\n' +
                '</orders>',
        );
    });

    it("tolerates input truncated mid-tag (server preview cap)", () => {
        const out = tryPrettifyXml("<root><a><b>x</b></a><a><b");
        expect(out).toBe("<root>\n  <a>\n    <b>x</b>\n  </a>\n  <a>\n    <b");
    });

    it("returns null for non-XML content", () => {
        expect(tryPrettifyXml("Id,Name\n1,Alpha")).toBeNull();
        expect(tryPrettifyXml('{"a":1}')).toBeNull();
        expect(tryPrettifyXml("")).toBeNull();
        expect(tryPrettifyXml("plain text")).toBeNull();
    });

    it("skips the BOM and surrounding whitespace", () => {
        expect(tryPrettifyXml("\ufeff  <a/>  ")).toBe("<a/>");
    });

    it("formats large single-line input in linear time", () => {
        const row = "<row a='1' b='2'>cell</row>";
        const doc = `<root>${row.repeat(50_000)}</root>`;
        const start = performance.now();
        const out = tryPrettifyXml(doc, 8 * 1024 * 1024);
        const elapsed = performance.now() - start;
        expect(out).not.toBeNull();
        expect(elapsed).toBeLessThan(2000);
    });
});
