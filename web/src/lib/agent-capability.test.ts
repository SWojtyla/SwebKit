import { describe, it, expect } from "vitest";
import { profileSupportsTools } from "./agent-capability";

describe("profileSupportsTools", () => {
  it("is true for a local-model profile that probed as ToolCalling", () => {
    expect(profileSupportsTools({ provider: "LmStudio", capability: "ToolCalling" })).toBe(true);
  });

  it("is false for ChatOnly and Unknown local-model profiles", () => {
    expect(profileSupportsTools({ provider: "LmStudio", capability: "ChatOnly" })).toBe(false);
    expect(profileSupportsTools({ provider: "OpenAiCompatible", capability: "Unknown" })).toBe(false);
  });

  it("is false when no profile is active", () => {
    expect(profileSupportsTools(undefined)).toBe(false);
  });

  it("is true for ACP profiles even when the stored capability is Unknown", () => {
    // ACP capability is live: real tool delivery is gated by the agent's mcpCapabilities from
    // session/new, not by a stored value that stays "Unknown" until the user runs Test Connection.
    expect(profileSupportsTools({ provider: "Acp", capability: "Unknown" })).toBe(true);
    expect(profileSupportsTools({ provider: "Acp", capability: "ChatOnly" })).toBe(true);
    expect(profileSupportsTools({ provider: "Acp", capability: "ToolCalling" })).toBe(true);
  });
});
