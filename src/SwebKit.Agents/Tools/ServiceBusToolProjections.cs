using SwebKit.Core.Models;

namespace SwebKit.Agents.Tools;

/// <summary>
/// Shared projections that shape Service Bus domain objects into the anonymous
/// structures returned (as JSON) by the Service Bus agent tools. Centralizing
/// them keeps the serialized field contract identical across tools.
/// </summary>
internal static class ServiceBusToolProjections
{
    /// <summary>Projects a Service Bus message into the tool response shape.</summary>
    public static object Message(SbMessage m) => new
    {
        message_id = m.MessageId,
        correlation_id = m.CorrelationId,
        subject = m.Subject,
        content_type = m.ContentType,
        body = m.Body,
        enqueued_at = m.EnqueuedAt.ToString("o"),
        delivery_count = m.DeliveryCount,
        dead_letter_reason = m.DeadLetterReason,
        dead_letter_error = m.DeadLetterErrorDescription,
        sequence_number = m.SequenceNumber,
        session_id = m.SessionId,
        application_properties = m.ApplicationProperties
    };
}
