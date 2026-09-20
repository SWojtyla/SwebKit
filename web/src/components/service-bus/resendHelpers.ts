import type { SbEntityInfo, SbMessage } from "@/lib/types";

// The broker writes these into ApplicationProperties when it dead-letters a
// message; a resent copy must not carry them. Mirrors the strip the server-side
// clone does in AzureServiceBusClient.ResubmitDeadLetterAsync.
const DLQ_PROP_KEYS = ["DeadLetterReason", "DeadLetterErrorDescription"];

/**
 * Clones a peeked message for resend-as-copy: every clone gets a fresh
 * MessageId so broker duplicate detection can't silently drop it, and all
 * broker-owned fields (sequence number, lock token, delivery count, DLQ
 * metadata) are cleared — the broker reassigns them on send.
 */
export function cloneForResend(source: SbMessage): SbMessage {
  const applicationProperties = { ...source.applicationProperties };
  for (const key of DLQ_PROP_KEYS) {
    delete applicationProperties[key];
  }
  return {
    messageId: crypto.randomUUID(),
    correlationId: source.correlationId,
    subject: source.subject,
    contentType: source.contentType,
    body: source.body,
    applicationProperties,
    systemProperties: null,
    deadLetterReason: null,
    deadLetterErrorDescription: null,
    enqueuedAt: new Date().toISOString(),
    deliveryCount: 0,
    lockToken: null,
    sequenceNumber: null,
    sessionId: source.sessionId,
  };
}

/**
 * The path a send must target for a given entity. Subscriptions are
 * receive-only — sending to `topic/subscriptions/name` fails on the broker —
 * so send-like actions normalize to the parent topic, same as the composer's
 * existing subscription rule.
 */
export function sendableEntityPath(entity: SbEntityInfo): string {
  return entity.isSubscription && entity.topicName ? entity.topicName : entity.entityPath;
}
