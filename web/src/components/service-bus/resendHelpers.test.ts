import { describe, expect, it } from "vitest";
import { cloneForResend, sendableEntityPath } from "./resendHelpers";
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

describe("cloneForResend", () => {
  it("assigns a fresh MessageId that differs from the source", () => {
    const source = makeMessage();
    const clone = cloneForResend(source);
    expect(clone.messageId).not.toBe(source.messageId);
    expect(clone.messageId).toMatch(/^[0-9a-f-]{36}$/);
  });

  it("generates a distinct MessageId per clone", () => {
    const source = makeMessage();
    const a = cloneForResend(source);
    const b = cloneForResend(source);
    expect(a.messageId).not.toBe(b.messageId);
  });

  it("clears broker-owned fields", () => {
    const clone = cloneForResend(makeMessage());
    expect(clone.sequenceNumber).toBeNull();
    expect(clone.lockToken).toBeNull();
    expect(clone.deliveryCount).toBe(0);
    expect(clone.deadLetterReason).toBeNull();
    expect(clone.deadLetterErrorDescription).toBeNull();
    expect(clone.systemProperties).toBeNull();
  });

  it("strips dead-letter application properties", () => {
    const clone = cloneForResend(
      makeMessage({
        applicationProperties: {
          orderId: "ORD-1",
          DeadLetterReason: "MaxDeliveryCount",
          DeadLetterErrorDescription: "boom",
        },
      }),
    );
    expect(clone.applicationProperties).toEqual({ orderId: "ORD-1" });
  });

  it("preserves payload and routing fields", () => {
    const source = makeMessage();
    const clone = cloneForResend(source);
    expect(clone.body).toBe(source.body);
    expect(clone.subject).toBe(source.subject);
    expect(clone.correlationId).toBe(source.correlationId);
    expect(clone.contentType).toBe(source.contentType);
    expect(clone.sessionId).toBe(source.sessionId);
    expect(clone.applicationProperties.orderId).toBe("ORD-1");
  });

  it("does not mutate the source message", () => {
    const source = makeMessage({
      applicationProperties: { DeadLetterReason: "x", keep: "y" },
    });
    cloneForResend(source);
    expect(source.applicationProperties.DeadLetterReason).toBe("x");
    expect(source.messageId).toBe("original-id");
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
