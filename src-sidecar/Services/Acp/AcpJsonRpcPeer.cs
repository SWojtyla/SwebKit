using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace SwebKit.Sidecar.Services.Acp;

/// <summary>A JSON-RPC protocol error returned by the agent (or synthesized locally).</summary>
public sealed class AcpRpcException : Exception
{
    /// <summary>ACP's authentication-required error code (per the protocol's reserved codes).</summary>
    public const int AuthRequired = -32000;
    public const int MethodNotFound = -32601;

    public int Code { get; }

    public AcpRpcException(int code, string message) : base(message) => Code = code;

    public AcpRpcException(JsonElement error)
        : base(error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()!
            : "JSON-RPC error")
    {
        Code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetInt32()
            : -32603;
    }
}

/// <summary>
/// Minimal JSON-RPC 2.0 peer speaking ACP's stdio framing: one complete JSON message per line,
/// UTF-8, no embedded newlines. The connection is bidirectional — outbound requests are
/// correlated by id; inbound requests (<c>session/request_permission</c>, unsupported
/// <c>fs/*</c>/<c>terminal/*</c>) and notifications (<c>session/update</c>) are dispatched to
/// <see cref="OnRequest"/>/<see cref="OnNotification"/>.
/// </summary>
public sealed class AcpJsonRpcPeer : IAsyncDisposable
{
    private readonly Stream _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Task _readLoop;
    private long _nextId;

    /// <summary>Handles an inbound agent→client request; the return value becomes the JSON-RPC
    /// <c>result</c>. Throw <see cref="AcpRpcException"/> to send a specific error code; any other
    /// exception becomes a generic <c>-32603</c> error.</summary>
    public Func<string, JsonElement, CancellationToken, Task<object?>>? OnRequest { get; set; }

    /// <summary>Handles an inbound agent→client notification (no response expected).</summary>
    public Func<string, JsonElement, Task>? OnNotification { get; set; }

    /// <summary>Completes when the read loop ends (agent closed stdout or the pipe broke). All
    /// pending requests are failed by then.</summary>
    public Task Completion => _readLoop;

    public AcpJsonRpcPeer(Stream reader, Stream writer)
    {
        _writer = writer;
        _readLoop = Task.Run(() => ReadLoopAsync(reader));
    }

    /// <summary>Sends a request and awaits the agent's response. <paramref name="params"/> is any
    /// serializable object; <see cref="AcpRpcException"/> is thrown for a JSON-RPC error result.</summary>
    public async Task<JsonElement> SendRequestAsync(string method, object? @params, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        await WriteMessageAsync(w => w
            .String("jsonrpc", "2.0")
            .Number("id", id)
            .String("method", method)
            .Object("params", @params), ct);

        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            return await tcs.Task;
        }
    }

    public Task SendNotificationAsync(string method, object? @params, CancellationToken ct) =>
        WriteMessageAsync(w => w
            .String("jsonrpc", "2.0")
            .String("method", method)
            .Object("params", @params), ct);

    /// <summary>Serializes one message as a single line (ACP forbids embedded newlines — compact
    /// JSON is single-line by construction) and writes it under a lock so interleaved calls can't
    /// tear a frame.</summary>
    private async Task WriteMessageAsync(Action<Utf8JsonWriter> write, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        await _writeLock.WaitAsync(ct);
        try
        {
            await _writer.WriteAsync(ms.GetBuffer().AsMemory(0, (int)ms.Length), ct);
            await _writer.WriteAsync("\n"u8.ToArray(), ct);
            await _writer.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(Stream reader)
    {
        Exception? failure = null;
        try
        {
            var buffer = new byte[64 * 1024];
            var line = new MemoryStream();
            while (true)
            {
                var read = await reader.ReadAsync(buffer);
                if (read == 0)
                    break;

                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        if (line.Length > 0)
                        {
                            var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
                            line.SetLength(0);
                            HandleMessage(text);
                        }
                    }
                    else if (buffer[i] != (byte)'\r')
                    {
                        line.WriteByte(buffer[i]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            var error = failure ?? new IOException("ACP agent closed stdout.");
            foreach (var tcs in _pending.Values)
                tcs.TrySetException(error);
            _pending.Clear();
        }
    }

    private void HandleMessage(string line)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return; // tolerate non-JSON noise on stdout — the spec forbids it, real agents still do it
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("method", out var methodEl) && methodEl.ValueKind == JsonValueKind.String)
            {
                var method = methodEl.GetString()!;
                var prms = root.TryGetProperty("params", out var p) ? p.Clone() : default;

                if (root.TryGetProperty("id", out var idEl))
                {
                    // Clone now, on this thread — the element points into `doc`, which is disposed
                    // before the Task.Run body runs. (Same reason `params` is cloned above.)
                    var id = idEl.Clone();
                    _ = Task.Run(() => RespondToInboundRequestAsync(id, method, prms));
                }
                else if (OnNotification is not null)
                {
                    _ = Task.Run(() => OnNotification(method, prms));
                }
                return;
            }

            if (root.TryGetProperty("id", out var respId) && TryGetIdKey(respId, out var key)
                && _pending.TryRemove(key, out var tcs))
            {
                if (root.TryGetProperty("error", out var err))
                    tcs.TrySetException(new AcpRpcException(err));
                else
                    tcs.TrySetResult(root.TryGetProperty("result", out var r) ? r.Clone() : default);
            }
        }
    }

    /// <summary>Response ids come back in whatever JSON type the agent echoes — we send numbers,
    /// but a stringified number still resolves.</summary>
    private static bool TryGetIdKey(JsonElement id, out long key)
    {
        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out key))
            return true;
        if (id.ValueKind == JsonValueKind.String && long.TryParse(id.GetString(), out key))
            return true;
        key = -1;
        return false;
    }

    private async Task RespondToInboundRequestAsync(JsonElement id, string method, JsonElement prms)
    {
        try
        {
            object? result = OnRequest is null
                ? throw new AcpRpcException(AcpRpcException.MethodNotFound, "Method not found")
                : await OnRequest(method, prms, CancellationToken.None);

            await WriteMessageAsync(w => w
                .String("jsonrpc", "2.0")
                .Raw("id", id)
                .Object("result", result ?? new { }), CancellationToken.None);
        }
        catch (AcpRpcException ex)
        {
            await WriteErrorAsync(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(id, -32603, ex.Message);
        }
    }

    private Task WriteErrorAsync(JsonElement id, int code, string message) =>
        WriteMessageAsync(w =>
        {
            w.String("jsonrpc", "2.0").Raw("id", id);
            w.WritePropertyName("error");
            w.WriteStartObject();
            w.WriteNumber("code", code);
            w.WriteString("message", message);
            w.WriteEndObject();
        }, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();
        try { _writer.Close(); } catch { }
        try { await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }
}

/// <summary>Small fluent helpers for writing JSON-RPC message fields.</summary>
internal static class AcpJsonWriterExtensions
{
    public static Utf8JsonWriter String(this Utf8JsonWriter w, string name, string value)
    {
        w.WriteString(name, value);
        return w;
    }

    public static Utf8JsonWriter Number(this Utf8JsonWriter w, string name, long value)
    {
        w.WriteNumber(name, value);
        return w;
    }

    /// <summary>Writes a pre-parsed JSON element verbatim (used to echo a request id back without
    /// caring whether it was a number or a string).</summary>
    public static Utf8JsonWriter Raw(this Utf8JsonWriter w, string name, JsonElement value)
    {
        w.WritePropertyName(name);
        value.WriteTo(w);
        return w;
    }

    public static Utf8JsonWriter Object(this Utf8JsonWriter w, string name, object? value)
    {
        w.WritePropertyName(name);
        JsonSerializer.Serialize(w, value);
        return w;
    }
}
