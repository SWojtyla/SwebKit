import { describe, expect, it } from "vitest";
import { parseEnvVars, serializeEnvVars } from "./AgentSettings";

describe("parseEnvVars", () => {
 it("parses KEY=VALUE lines", () => {
  expect(parseEnvVars("FOO=bar\nBAZ=qux")).toEqual({ FOO: "bar", BAZ: "qux" });
 });

 it("keeps = characters in values", () => {
  expect(parseEnvVars("URL=https://x?a=b")).toEqual({ URL: "https://x?a=b" });
 });

 it("skips blanks, comments, and lines without a key", () => {
  expect(parseEnvVars("\n# comment\n=novalue\nnoeq\nA=1")).toEqual({ A: "1" });
 });

 it("returns an empty map for empty input", () => {
  expect(parseEnvVars("")).toEqual({});
 });
});

describe("serializeEnvVars", () => {
 it("round-trips with parseEnvVars", () => {
  const env = { FOO: "bar", ANTHROPIC_BASE_URL: "https://x?a=b" };
  expect(parseEnvVars(serializeEnvVars(env))).toEqual(env);
 });
});
