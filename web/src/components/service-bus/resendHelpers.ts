import type { SbEntityInfo, SbMessage } from "@/lib/types";

/**
 * The queue a resend will target for a message: the `NServiceBus.FailedQ` application
 * property when present (the queue the message failed in — the same target
 * ServiceInsight/ServicePulse retry to), otherwise the entity the message was selected
 * from. Mirrors `AzureServiceBusClient.ResolveResendTarget`, including the MSMQ-era
 * "@machine" suffix strip — used here only to preview the target in the confirm bar;
 * the sidecar resolves the real target per message when it forwards.
 */
export function resendTargetQueue(message: SbMessage, fallbackEntityPath: string): string {
  const value = message.applicationProperties?.["NServiceBus.FailedQ"];
  if (typeof value === "string" && value.trim().length > 0) {
    const at = value.indexOf("@");
    return at > 0 ? value.slice(0, at) : value;
  }
  return fallbackEntityPath;
}

/**
 * Human-readable target for a resend selection in the confirm bar: the single queue
 * every selected message resolves to, or a generic label when the selection spans
 * multiple original queues.
 */
export function resendTargetText(messages: SbMessage[], fallbackEntityPath: string): string {
  const targets = new Set(messages.map((m) => resendTargetQueue(m, fallbackEntityPath)));
  return targets.size === 1 ? [...targets][0] : "their original queues";
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
