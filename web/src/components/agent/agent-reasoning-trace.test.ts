import { describe, expect, it } from "vitest";
import { countFailedSteps, describeReasoningToggleLabel } from "./AgentReasoningTrace";
import type { AgentChatStep } from "@/lib/types";

const call = (toolName: string): AgentChatStep => ({ type: "tool_call", toolName, summary: `Calling ${toolName}` });
const result = (toolName: string, isFailure = false): AgentChatStep => ({
  type: "tool_result",
  toolName,
  summary: isFailure ? `{"error":"${toolName} failed"}` : "ok",
  isFailure,
});

describe("countFailedSteps", () => {
  it("counts only tool_result steps flagged as a failure", () => {
    const steps = [call("a"), result("a", false), call("b"), result("b", true)];
    expect(countFailedSteps(steps)).toBe(1);
  });

  it("never counts a tool_call step, even if isFailure were somehow set on one", () => {
    const steps: AgentChatStep[] = [{ type: "tool_call", toolName: "a", isFailure: true }];
    expect(countFailedSteps(steps)).toBe(0);
  });

  it("is zero for an all-success trace", () => {
    expect(countFailedSteps([call("a"), result("a")])).toBe(0);
  });
});

describe("describeReasoningToggleLabel", () => {
  it("matches the plain pre-7.4 label when nothing failed", () => {
    expect(describeReasoningToggleLabel([call("a"), result("a")])).toBe("Show reasoning (2 steps)");
  });

  it("uses singular 'step' for exactly one step", () => {
    expect(describeReasoningToggleLabel([call("a")])).toBe("Show reasoning (1 step)");
  });

  it("appends a failed count so a bad data source isn't hidden behind the disclosure", () => {
    const steps = [call("a"), result("a", true), call("b"), result("b", true), call("c"), result("c")];
    expect(describeReasoningToggleLabel(steps)).toBe("Show reasoning (6 steps, 2 failed)");
  });
});
