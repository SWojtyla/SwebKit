using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SwebKit.Core.Services;

/// <summary>
/// Keyed cache whose values are built <em>at most once per key</em>, however many callers miss at
/// the same time. Concurrent cold misses for one key share a single build instead of each running
/// the factory and racing to publish — which matters when the factory is expensive: building an
/// AKS client runs kubelogin/<c>az</c> synchronously, and every duplicate build was seconds of
/// wasted work on a cold dashboard load.
/// </summary>
/// <remarks>
/// <para>Each key holds a <see cref="TaskCompletionSource{TResult}"/> slot rather than a
/// <c>Lazy&lt;T&gt;</c>: an evictor must be able to hand off disposal of a value that is still
/// being built (a profile save landing mid-build) without blocking on that build, and a task gives
/// it a continuation to hang the disposal on. <c>Lazy&lt;T&gt;</c> only exposes the value by
/// running or waiting for the factory.</para>
/// <para>A build that throws is removed from the cache before its waiters observe the exception,
/// so one transient failure never poisons the key — the next call builds afresh.</para>
/// <para>Builds are shared, so they are never cancelled by one caller: async factories run with
/// <see cref="CancellationToken.None"/> and each caller's token only bounds its own wait.</para>
/// <para>Disposal is the owner's job. <see cref="TryRemove"/>/<see cref="RemoveAll"/> hand back the
/// removed builds exactly once (the dictionary's atomic remove picks the single winner), and
/// <see cref="SingleFlightCache.WhenBuilt{T}"/> runs a disposal on a build now or when it completes.</para>
/// </remarks>
public sealed class SingleFlightCache<T>
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<T>> _slots;

    public SingleFlightCache(IEqualityComparer<string>? comparer = null) =>
        _slots = new ConcurrentDictionary<string, TaskCompletionSource<T>>(comparer ?? StringComparer.Ordinal);

    /// <summary>Number of keys currently cached (built or in flight).</summary>
    public int Count => _slots.Count;

    /// <summary>
    /// Returns the value for <paramref name="key"/>, running <paramref name="factory"/> only if no
    /// build exists. A caller that finds a build in flight blocks until it finishes.
    /// </summary>
    public T GetOrCreate(string key, Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var (slot, isOwner) = Claim(key);
        if (!isOwner)
            return slot.Task.GetAwaiter().GetResult();

        T value;
        try
        {
            value = factory();
        }
        catch (Exception ex)
        {
            Fail(key, slot, ex);
            throw;
        }

        slot.SetResult(value);
        return value;
    }

    /// <summary>
    /// Async variant of <see cref="GetOrCreate"/>. The build itself is not cancellable (other
    /// callers may be waiting on it); <paramref name="ct"/> only abandons this caller's wait.
    /// </summary>
    public async Task<T> GetOrCreateAsync(string key, Func<Task<T>> factory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var (slot, isOwner) = Claim(key);
        if (isOwner)
            _ = BuildAsync(key, slot, factory);

        return await slot.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Removes <paramref name="key"/>; <paramref name="build"/> is the removed (possibly still running) build.</summary>
    public bool TryRemove(string key, [NotNullWhen(true)] out Task<T>? build)
    {
        build = _slots.TryRemove(key, out var slot) ? slot.Task : null;
        return build is not null;
    }

    /// <summary>Removes every key; each removed build is returned exactly once across concurrent callers.</summary>
    public List<(string Key, Task<T> Build)> RemoveAll()
    {
        var removed = new List<(string, Task<T>)>();
        foreach (var key in _slots.Keys.ToArray())
        {
            if (_slots.TryRemove(key, out var slot))
                removed.Add((key, slot.Task));
        }
        return removed;
    }

    private (TaskCompletionSource<T> Slot, bool IsOwner) Claim(string key)
    {
        // RunContinuationsAsynchronously: completing the slot must not run every waiter's
        // continuation inline on the builder's thread.
        var candidate = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slot = _slots.GetOrAdd(key, candidate);
        return (slot, ReferenceEquals(slot, candidate));
    }

    private async Task BuildAsync(string key, TaskCompletionSource<T> slot, Func<Task<T>> factory)
    {
        try
        {
            slot.SetResult(await factory().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            Fail(key, slot, ex);
            // Every waiter may have abandoned the build (cancelled); observe so it is never reported unobserved.
            _ = slot.Task.Exception;
        }
    }

    /// <summary>Drops the failed slot (only if it is still the installed one) before failing its waiters.</summary>
    private void Fail(string key, TaskCompletionSource<T> slot, Exception ex)
    {
        _slots.TryRemove(new KeyValuePair<string, TaskCompletionSource<T>>(key, slot));
        if (ex is OperationCanceledException oce)
            slot.SetCanceled(oce.CancellationToken);
        else
            slot.SetException(ex);
    }
}

/// <summary>Helpers for consuming builds handed back by <see cref="SingleFlightCache{T}"/>.</summary>
public static class SingleFlightCache
{
    /// <summary>
    /// Runs <paramref name="onBuilt"/> on the value of <paramref name="build"/> — synchronously if
    /// it already succeeded, otherwise when it completes. A failed build has nothing to dispose and
    /// is ignored (its exception already reached the callers that waited on it).
    /// </summary>
    public static void WhenBuilt<T>(Task<T> build, Action<T> onBuilt)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(onBuilt);

        if (build.IsCompletedSuccessfully)
        {
            onBuilt(build.Result);
            return;
        }

        _ = build.ContinueWith(
            static (t, state) =>
            {
                if (t.IsCompletedSuccessfully)
                    ((Action<T>)state!)(t.Result);
                else
                    _ = t.Exception; // observe, so an abandoned failed build is not an unobserved-task warning
            },
            onBuilt,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
