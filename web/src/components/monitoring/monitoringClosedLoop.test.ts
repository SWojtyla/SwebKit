import { describe, expect, it } from "vitest";
import { buildPrefilledRuleDraft } from "./prefillRule";
import { proposalsForSession, proposalsLinkedToReport } from "./proposalLinks";
import type { PendingAction } from "../../lib/types";

describe("buildPrefilledRuleDraft", () => {
    it("always produces a new-rule draft (empty id) with the dialog's safe defaults", () => {
        const draft = buildPrefilledRuleDraft({});

        expect(draft.id).toBe(""); // routes the dialog down the create path
        expect(draft.name).toBe("");
        expect(draft.enabled).toBe(true);
        expect(draft.severity).toBe("Warning");
        expect(draft.intervalSeconds).toBe(60);
        expect(draft.cooldownMinutes).toBe(5);
        expect(draft.aiInvestigationEnabled).toBe(true);
        // The opt-in gate a deep link must not bypass silently.
        expect(draft.autoFixProposalsEnabled).toBe(false);
    });

    it("merges the surface's partial rule over the defaults", () => {
        const draft = buildPrefilledRuleDraft({
            source: "AksPodRestartRate",
            name: "api restarts",
            aksPodParams: { namespace: "prod" },
        });

        expect(draft.source).toBe("AksPodRestartRate");
        expect(draft.name).toBe("api restarts");
        expect(draft.aksPodParams?.namespace).toBe("prod");
        expect(draft.severity).toBe("Warning"); // untouched defaults stay
    });

    it("lets a surface explicitly opt into autofix proposals", () => {
        const draft = buildPrefilledRuleDraft({ autoFixProposalsEnabled: true });
        expect(draft.autoFixProposalsEnabled).toBe(true);
    });
});

describe("proposalLinks", () => {
    const action = (id: string, originSessionId?: string): PendingAction =>
        ({
            id,
            type: "PurgeServiceBusDeadLetters",
            summary: "s",
            target: "t",
            risk: "High",
            preview: "p",
            origin: originSessionId ? "investigation" : undefined,
            originSessionId,
        }) as PendingAction;

    describe("proposalsLinkedToReport", () => {
        it("keeps only the actions whose ids the report lists", () => {
            const actions = [action("a1", "s1"), action("a2", "s1"), action("other", "s2")];
            const linked = proposalsLinkedToReport({ pendingActionIds: ["a1", "a2"] }, actions);
            expect(linked.map((a) => a.id)).toEqual(["a1", "a2"]);
        });

        it("drops resolved actions — a confirmed proposal no longer appears in the live list", () => {
            // The report still carries the id; reconciliation is that the live list is the filter.
            const linked = proposalsLinkedToReport({ pendingActionIds: ["a1", "gone"] }, [action("a1", "s1")]);
            expect(linked.map((a) => a.id)).toEqual(["a1"]);
        });

        it("tolerates a report with no pendingActionIds (pre-closed-loop reports)", () => {
            const linked = proposalsLinkedToReport({}, [action("a1", "s1")]);
            expect(linked).toEqual([]);
        });
    });

    describe("proposalsForSession", () => {
        it("joins the transient insight card to parked actions by originSessionId", () => {
            const actions = [action("a1", "proactive-r1-1"), action("a2", "proactive-r1-2"), action("chat-1")];
            const linked = proposalsForSession("proactive-r1-2", actions);
            expect(linked.map((a) => a.id)).toEqual(["a2"]);
        });

        it("returns nothing when the investigation parked no proposals", () => {
            expect(proposalsForSession("proactive-r1-1", [action("chat-1")])).toEqual([]);
        });
    });
});
