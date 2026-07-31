import type { SbMessage } from "@/lib/types";

export function messageToDownloadObject(message: SbMessage): unknown {
  return {
    messageId: message.messageId,
    correlationId: message.correlationId,
    subject: message.subject,
    contentType: message.contentType,
    body: message.body,
    applicationProperties: message.applicationProperties,
    systemProperties: message.systemProperties,
    enqueuedAt: message.enqueuedAt,
    deliveryCount: message.deliveryCount,
    sequenceNumber: message.sequenceNumber,
    sessionId: message.sessionId,
    deadLetterReason: message.deadLetterReason,
    deadLetterErrorDescription: message.deadLetterErrorDescription,
  };
}

export function safeFileName(name: string, maxLength = 80): string {
  const safe = name.replace(/[^a-zA-Z0-9_-]/g, "_").replace(/_+/g, "_").slice(0, maxLength);
  return safe || "unknown";
}

export function messageKey(message: SbMessage): string {
  return `${message.messageId}-${message.sequenceNumber ?? ""}`;
}
