import { describe, expect, it } from "vitest";
import { resendTargetQueue, resendTargetText, sendableEntityPath } from "./resendHelpers";
import type { SbEntityInfo, SbMessage } from "@/lib/types";

function makeMessage(overrides: Partial<SbMessage> = {}): SbMessage {
  return {
    messageId: "original-id",
    correlationId: "corr-1",
    subject: "Order created",
    contentType: "application/json",
    body: '{"orderId":"ORD-1"}',
    applicationProperties: { orderId: "ORD-1" },
    systemProperties: { partitionKey: "pk" } as SbMessage["systemProperties"],
    deadLetterReason: "MaxDeliveryCount",
    deadLetterErrorDescription: "too many retries",
    enqueuedAt: "2026-01-01T00:00:00Z",
    deliveryCount: 10,
    lockToken: "lock-token",
    sequenceNumber: 42,
    sessionId: "session-1",
    ...overrides,
  };
}

function makeEntity(overrides: Partial<SbEntityInfo> = {}): SbEntityInfo {
  return {
    name: "orders",
    entityPath: "orders",
    stats: null,
    isDisabled: false,
    isTopic: false,
    isSubscription: false,
    topicName: null,
    subscriptionDeadLetterCount: null,
    ...overrides,
  };
}

describe("resendTargetQueue", () => {
  it("returns the NServiceBus.FailedQ queue when the header is present", () => {
    const message = makeMessage({
      applicationProperties: { "NServiceBus.FailedQ": "sbq-orders" },
    });
    expect(resendTargetQueue(message, "error")).toBe("sbq-orders");
  });

  it("strips an MSMQ-era @machine suffix from the FailedQ value", () => {
    const message = makeMessage({
      applicationProperties: { "NServiceBus.FailedQ": "sbq-orders@machine" },
    });
    expect(resendTargetQueue(message, "error")).toBe("sbq-orders");
  });

  it("falls back to the viewed entity when the header is absent", () => {
    expect(resendTargetQueue(makeMessage(), "error")).toBe("error");
  });

  it("falls back when the header is blank or not a string", () => {
    expect(
      resendTargetQueue(makeMessage({ applicationProperties: { "NServiceBus.FailedQ": "  " } }), "error"),
    ).toBe("error");
    expect(
      resendTargetQueue(makeMessage({ applicationProperties: { "NServiceBus.FailedQ": 42 } }), "error"),
    ).toBe("error");
  });
});

describe("resendTargetText", () => {
  it("names the queue when every message resolves to the same target", () => {
    const messages = [
      makeMessage({ applicationProperties: { "NServiceBus.FailedQ": "sbq-orders" } }),
      makeMessage({ applicationProperties: { "NServiceBus.FailedQ": "sbq-orders" } }),
    ];
    expect(resendTargetText(messages, "error")).toBe("sbq-orders");
  });

  it("uses the generic label when the selection spans multiple queues", () => {
    const messages = [
      makeMessage({ applicationProperties: { "NServiceBus.FailedQ": "sbq-orders" } }),
      makeMessage({ applicationProperties: { "NServiceBus.FailedQ": "sbq-billing" } }),
    ];
    expect(resendTargetText(messages, "error")).toBe("their original queues");
  });
});

describe("sendableEntityPath", () => {
  it("returns the entity path for queues and topics", () => {
    expect(sendableEntityPath(makeEntity())).toBe("orders");
    expect(sendableEntityPath(makeEntity({ isTopic: true }))).toBe("orders");
  });

  it("returns the parent topic for subscriptions", () => {
    const sub = makeEntity({
      isSubscription: true,
      entityPath: "orders/subscriptions/auditing",
      topicName: "orders",
      name: "auditing",
    });
    expect(sendableEntityPath(sub)).toBe("orders");
  });

  it("falls back to the entity path when topicName is missing", () => {
    const sub = makeEntity({ isSubscription: true, topicName: null });
    expect(sendableEntityPath(sub)).toBe("orders");
  });
});
