using SwebKit.Core.Models;

namespace SwebKit.Azure.ServiceBus;

/// <summary>
/// The receive loop behind reach-message parking — sibling of <see cref="MessageSequenceProcessor"/>,
/// but the stopping rule is sequence-bound rather than set-bound: receive batches in order; below the
/// target → park (stamped dead-letter); at the target → the caller's action; past the target →
/// abandon and stop. "Past the target" proving absence is why a deferred or consumed target can never
/// cause the loop to drain the whole queue — the first overshoot ends it.
/// </summary>
internal static class ReachParkProcessor
{
    public static async Task<SbParkResult> ProcessAsync<TMessage>(
        long targetSequenceNumber,
        int maxParked,
        int maxBatchSize,
        TimeSpan receiveWaitTime,
        Func<int, TimeSpan, CancellationToken, Task<IReadOnlyList<TMessage>>> receiveMessagesAsync,
        Func<TMessage, long> getSequenceNumber,
        Func<TMessage, CancellationToken, Task> parkMessageAsync,
        Func<TMessage, CancellationToken, Task> actOnTargetAsync,
        Func<TMessage, CancellationToken, Task> releaseMessageAsync,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var result = new SbParkResult();
        var stop = false;

        while (!stop)
        {
            ct.ThrowIfCancellationRequested();

            var received = await receiveMessagesAsync(maxBatchSize, receiveWaitTime, ct).ConfigureAwait(false);
            if (received.Count == 0)
            {
                break;
            }

            foreach (var message in received)
            {
                ct.ThrowIfCancellationRequested();
                var sequenceNumber = getSequenceNumber(message);

                if (stop)
                {
                    // Once the loop has stopped (target acted, cap hit, or overshoot) the rest of
                    // the batch is only released — never parked, never acted on.
                    await releaseMessageAsync(message, ct).ConfigureAwait(false);
                    result.OvershootAbandoned++;
                    continue;
                }

                if (sequenceNumber < targetSequenceNumber)
                {
                    if (result.ParkedCount >= maxParked)
                    {
                        // Cap reached — release this and everything after it; the target is
                        // reported unreached so the service can fail the op honestly with the
                        // parked set still recoverable via the DLQ stamp.
                        await releaseMessageAsync(message, ct).ConfigureAwait(false);
                        result.CapHit = true;
                        result.OvershootAbandoned++;
                        result.FirstSequenceBeyondTarget ??= sequenceNumber;
                        stop = true;
                        continue;
                    }

                    await parkMessageAsync(message, ct).ConfigureAwait(false);
                    result.ParkedCount++;
                    progress?.Report(result.ParkedCount);
                }
                else if (sequenceNumber == targetSequenceNumber)
                {
                    result.TargetReached = true;
                    await actOnTargetAsync(message, ct).ConfigureAwait(false);
                    stop = true;
                }
                else
                {
                    // First message past the target — release it and stop. Target unreachable by
                    // FIFO: deferred (invisible to plain receive) or consumed by someone else.
                    await releaseMessageAsync(message, ct).ConfigureAwait(false);
                    result.OvershootAbandoned++;
                    result.FirstSequenceBeyondTarget ??= sequenceNumber;
                    stop = true;
                }
            }
        }

        return result;
    }
}
