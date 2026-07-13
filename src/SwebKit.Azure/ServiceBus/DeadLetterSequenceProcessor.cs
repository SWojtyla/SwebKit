namespace SwebKit.Azure.ServiceBus;

internal static class DeadLetterSequenceProcessor
{
    public static async Task ProcessAsync<TMessage>(
        IReadOnlyCollection<long> requestedSequenceNumbers,
        int maxBatchSize,
        TimeSpan receiveWaitTime,
        Func<int, TimeSpan, CancellationToken, Task<IReadOnlyList<TMessage>>> receiveMessagesAsync,
        Func<TMessage, long> getSequenceNumber,
        Func<TMessage, CancellationToken, Task> processMatchedMessageAsync,
        Func<TMessage, CancellationToken, Task> releaseUnmatchedMessageAsync,
        CancellationToken ct = default)
    {
        if (requestedSequenceNumbers.Count == 0)
        {
            return;
        }

        var remaining = new HashSet<long>(requestedSequenceNumbers);

        while (remaining.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var receiveCount = Math.Min(maxBatchSize, Math.Max(1, remaining.Count));
            var received = await receiveMessagesAsync(receiveCount, receiveWaitTime, ct).ConfigureAwait(false);
            if (received.Count == 0)
            {
                break;
            }

            foreach (var message in received)
            {
                ct.ThrowIfCancellationRequested();

                if (remaining.Remove(getSequenceNumber(message)))
                {
                    await processMatchedMessageAsync(message, ct).ConfigureAwait(false);
                }
                else
                {
                    await releaseUnmatchedMessageAsync(message, ct).ConfigureAwait(false);
                }
            }
        }

        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(
                $"Dead-letter operation could not find the requested sequence numbers: {string.Join(", ", remaining.OrderBy(static sequenceNumber => sequenceNumber))}.");
        }
    }
}