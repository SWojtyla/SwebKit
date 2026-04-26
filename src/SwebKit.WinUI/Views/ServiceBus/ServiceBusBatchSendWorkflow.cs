using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;

namespace SwebKit.WinUI.Views.ServiceBus;

internal static class ServiceBusBatchSendWorkflow
{
    public static List<BatchSendEntry> ParseEntries(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Payload is empty.");
        }

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
            });

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Expected a JSON array at the root.");
        }

        var entries = new List<BatchSendEntry>();
        var index = 0;

        foreach (var element in document.RootElement.EnumerateArray())
        {
            index++;
            var entry = new BatchSendEntry();

            if (element.ValueKind != JsonValueKind.Object)
            {
                entry.ValidationError = $"Entry {index}: expected an object, got {element.ValueKind}.";
                entries.Add(entry);
                continue;
            }

            if (TryGetProperty(element, "messageId", out var messageIdElement)
                && messageIdElement.ValueKind == JsonValueKind.String)
            {
                entry.MessageId = messageIdElement.GetString() ?? entry.MessageId;
            }

            if (TryGetProperty(element, "correlationId", out var correlationIdElement)
                && correlationIdElement.ValueKind == JsonValueKind.String)
            {
                entry.CorrelationId = correlationIdElement.GetString();
            }

            if (TryGetProperty(element, "subject", out var subjectElement)
                && subjectElement.ValueKind == JsonValueKind.String)
            {
                entry.Subject = subjectElement.GetString();
            }

            if (TryGetProperty(element, "contentType", out var contentTypeElement)
                && contentTypeElement.ValueKind == JsonValueKind.String)
            {
                entry.ContentType = contentTypeElement.GetString();
            }

            if (TryGetProperty(element, "body", out var bodyElement))
            {
                entry.Body = bodyElement.ValueKind == JsonValueKind.String
                    ? bodyElement.GetString() ?? string.Empty
                    : bodyElement.GetRawText();
            }
            else
            {
                entry.ValidationError = $"Entry {index}: 'body' is required.";
                entries.Add(entry);
                continue;
            }

            if (TryGetProperty(element, "applicationProperties", out var propertiesElement)
                && propertiesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in propertiesElement.EnumerateObject())
                {
                    entry.ApplicationProperties[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
            }

            if (string.IsNullOrWhiteSpace(entry.MessageId))
            {
                entry.MessageId = Guid.NewGuid().ToString();
            }

            entries.Add(entry);
        }

        return entries;
    }

    public static async Task<BatchOperationResult> SendAsync(
        IServiceBusClient client,
        string targetEntityPath,
        IReadOnlyList<BatchSendEntry> entries,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(entries);

        var entityPath = targetEntityPath.Trim();
        if (string.IsNullOrWhiteSpace(entityPath))
        {
            throw new InvalidOperationException("Target entity is required.");
        }

        var result = new BatchOperationResult();
        var validEntries = entries.Where(static entry => entry.IsValid).ToList();
        result.Skipped = entries.Count - validEntries.Count;

        var processed = 0;
        foreach (var chunk in validEntries.Chunk(10))
        {
            try
            {
                var messages = chunk.Select(entry => new SbMessage
                {
                    MessageId = entry.MessageId,
                    CorrelationId = entry.CorrelationId,
                    Subject = entry.Subject,
                    ContentType = entry.ContentType,
                    Body = entry.Body,
                    ApplicationProperties = entry.ApplicationProperties.ToDictionary(
                        static pair => pair.Key,
                        static pair => (object)pair.Value,
                        StringComparer.OrdinalIgnoreCase),
                }).ToList();

                await client.SendBatchAsync(entityPath, messages, cancellationToken);
                result.Succeeded += chunk.Length;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failed += chunk.Length;
                result.Errors.Add(new BatchOperationItemError
                {
                    MessageId = $"chunk of {chunk.Length}",
                    Reason = ex.Message.Length > 120 ? ex.Message[..120] + "…" : ex.Message,
                });
            }

            processed += chunk.Length;
            progress?.Report(processed);
        }

        return result;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}