import { describe, it, expect } from "vitest";
import { extractSlowLogKey } from "./redis-format";

describe("extractSlowLogKey", () => {
  it("takes the first whitespace-separated token as the candidate key", () => {
    expect(extractSlowLogKey("user:profile:1001")).toBe("user:profile:1001");
    expect(extractSlowLogKey("cache:products 0 -1")).toBe("cache:products");
  });

  it("trims surrounding whitespace before splitting", () => {
    expect(extractSlowLogKey("  session:abc123 EX 60  ")).toBe("session:abc123");
  });

  it("returns null for empty, whitespace-only, or missing arguments", () => {
    expect(extractSlowLogKey("")).toBeNull();
    expect(extractSlowLogKey("   ")).toBeNull();
    expect(extractSlowLogKey(null)).toBeNull();
    expect(extractSlowLogKey(undefined)).toBeNull();
  });
});
