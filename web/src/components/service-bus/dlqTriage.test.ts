import { describe, expect, it } from "vitest";
import {
    DLQ_TRIAGE_NO_REASON,
    groupDlqByReason,
    groupSequenceNumbers,
} from "./dlqTriage";
import type { SbMessage } from "@/lib/types";

function msg(
    sequenceNumber: number,
    reason: string | null,
    description: string | null,
): SbMessage {
    return {
        messageId: `m-${sequenceNumber}`,
        correlationId: null,
        subject: null,
        contentType: null,
        body: "",
        applicationProperties: {},
        systemProperties: null,
        deadLetterReason: reason,
        deadLetterErrorDescription: description,
        enqueuedAt: "",
        deliveryCount: 0,
        lockToken: null,
        sequenceNumber,
        sessionId: null,
    };
}

describe("groupDlqByReason", () => {
    it("groups by (reason, description) pair, largest group first", () => {
        const groups = groupDlqByReason([
            msg(1, "MaxDeliveryCountExceeded", "gave up"),
            msg(2, "MaxDeliveryCountExceeded", "gave up"),
            msg(3, "MaxDeliveryCountExceeded", "ttl too short"),
            msg(4, "DeadLetteredByApplication", null),
        ]);

        expect(groups).toHaveLength(3);
        expect(groups[0].reason).toBe("MaxDeliveryCountExceeded");
        expect(groups[0].description).toBe("gave up");
        expect(groups[0].count).toBe(2);
        // Ties and singles follow; reason+desc pairs stay distinct.
        expect(groups.map((g) => g.description)).toContain("ttl too short");
        expect(groups.map((g) => g.reason)).toContain("DeadLetteredByApplication");
    });

    it("keeps descriptions distinct under the same reason", () => {
        const groups = groupDlqByReason([
            msg(1, "R", "d1"),
            msg(2, "R", "d2"),
        ]);
        expect(groups).toHaveLength(2);
    });

    it("buckets reason-less messages under (no reason), keeps null description", () => {
        const groups = groupDlqByReason([msg(1, null, null)]);
        expect(groups).toHaveLength(1);
        expect(groups[0].reason).toBe(DLQ_TRIAGE_NO_REASON);
        expect(groups[0].description).toBeNull();
    });

    it("returns [] on an empty window", () => {
        expect(groupDlqByReason([])).toEqual([]);
    });
});

describe("groupSequenceNumbers", () => {
    it("collects the group's sequence numbers, skipping nulls", () => {
        const group = groupDlqByReason([
            msg(10, "R", null),
            msg(11, "R", null),
        ])[0];
        group.messages.push({ ...msg(0, "R", null), sequenceNumber: null });
        expect(groupSequenceNumbers(group)).toEqual([10, 11]);
    });
});
