using SwebKit.Core.Abstractions;
using SwebKit.Core.Models;
using SwebKit.WinUI.Views.ServiceBus;

namespace SwebKit.WinUI.Tests;

public sealed class ServiceBusBatchSendWorkflowTests
{
    [Fact]
    public void ParseEntries_UsesCaseInsensitivePropertiesAndMarksInvalidRows()
    {
        const string payload = """
            [
              {
                "MESSAGEID": "msg-1",
                "Subject": "Accepted",
                "Body": { "tenant": "ops" },
                "ApplicationProperties": {
                  "priority": "high"
                }
              },
              {
                "correlationId": "corr-2"
              }
            ]
            """;

        var entries = ServiceBusBatchSendWorkflow.ParseEntries(payload);

        var validEntry = Assert.Single(entries.Where(static entry => entry.IsValid));
        Assert.Equal("msg-1", validEntry.MessageId);
        Assert.Equal("Accepted", validEntry.Subject);
        Assert.Equal("{\"tenant\":\"ops\"}", validEntry.Body);
        Assert.Equal("high", validEntry.ApplicationProperties["priority"]);

        var invalidEntry = Assert.Single(entries.Where(static entry => !entry.IsValid));
        Assert.Contains("'body' is required", invalidEntry.ValidationError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_ProcessesChunksAndCountsSkippedRows()
    {
        var client = new RecordingBatchSendClient
        {
            FailOnCall = 2,
        };

        var entries = Enumerable.Range(1, 12)
            .Select(index => new BatchSendEntry
            {
                MessageId = $"msg-{index}",
                Body = $"body-{index}",
                Subject = $"subject-{index}",
            })
            .Append(new BatchSendEntry
            {
                MessageId = "invalid",
                ValidationError = "missing body",
            })
            .ToList();

        var progressValues = new List<int>();
        var result = await ServiceBusBatchSendWorkflow.SendAsync(
            client,
            "orders",
            entries,
            new Progress<int>(value => progressValues.Add(value)));

        Assert.Equal(10, result.Succeeded);
        Assert.Equal(2, result.Failed);
        Assert.Equal(1, result.Skipped);
        Assert.Single(result.Errors);
        Assert.Equal(2, client.CallCount);
        Assert.Equal("orders", client.LastEntityPath);
        Assert.Equal(new[] { 10, 12 }, progressValues);
    }

    private sealed class RecordingBatchSendClient : IServiceBusClient
    {
        public int CallCount { get; private set; }

        public int? FailOnCall { get; init; }

        public string? LastEntityPath { get; private set; }

        public Task<SbNamespaceInfo> GetNamespaceInfoAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListQueuesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListTopicsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbEntityInfo>> ListSubscriptionsAsync(string topicName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetQueueEnabledAsync(string queueName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetTopicEnabledAsync(string topicName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetSubscriptionEnabledAsync(string topicName, string subscriptionName, bool enabled, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SbEntityStats> GetEntityStatsAsync(string entityPath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbMessage>> PeekMessagesAsync(string entityPath, int count, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SbMessage>> PeekDeadLetterAsync(string entityPath, int count, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CompleteMessagesAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> PurgeMessagesAsync(string entityPath, bool deadLetter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendMessageAsync(string entityPath, SbMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendBatchAsync(string entityPath, IReadOnlyList<SbMessage> messages, CancellationToken ct = default)
        {
            CallCount++;
            LastEntityPath = entityPath;

            if (FailOnCall == CallCount)
            {
                throw new InvalidOperationException("batch failed");
            }

            return Task.CompletedTask;
        }

        public Task<long> ScheduleMessageAsync(string entityPath, SbMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CancelScheduledMessageAsync(string entityPath, long sequenceNumber, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ResubmitDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, string? targetEntityPath, RemapRules? remapRules = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CompleteDeadLetterAsync(string entityPath, IReadOnlyList<string> sequenceNumbers, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TestConnectionAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}