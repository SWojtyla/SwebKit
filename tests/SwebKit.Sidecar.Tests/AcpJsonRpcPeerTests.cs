using System.IO.Pipelines;
using System.Text.Json;
using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Drives <see cref="AcpJsonRpcPeer"/> over a pair of in-memory pipes standing in for the agent's
/// stdin/stdout, so the JSON-RPC framing, id correlation, and inbound request/notification
/// dispatch can be tested without spawning a real process.
/// </summary>
public class AcpJsonRpcPeerTests
{
    private sealed class Harness : IAsyncDisposable
    {
        private readonly Pipe _clientToAgent = new();
        private readonly Pipe _agentToClient = new();

        public AcpJsonRpcPeer Peer { get; }
        public StreamReader AgentReads { get; }
        public StreamWriter AgentWrites { get; }

        public Harness()
        {
            Peer = new AcpJsonRpcPeer(
                _agentToClient.Reader.AsStream(),
                _clientToAgent.Writer.AsStream());
            AgentReads = new StreamReader(_clientToAgent.Reader.AsStream());
            AgentWrites = new StreamWriter(_agentToClient.Writer.AsStream())
            {
                AutoFlush = true,
                NewLine = "\n",
            };
        }

        /// <summary>Closes the agent→client pipe, simulating the agent process exiting.</summary>
        public async Task CloseAgentOutputAsync() => await _agentToClient.Writer.CompleteAsync();

        public async ValueTask DisposeAsync()
        {
            await Peer.DisposeAsync();
            AgentReads.Dispose();
            AgentWrites.Dispose();
        }
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SendRequest_writes_jsonrpc_line_and_resolves_on_matching_response()
    {
        await using var h = new Harness();
        using var cts = new CancellationTokenSource(Timeout);

        var sendTask = h.Peer.SendRequestAsync("initialize", new { protocolVersion = 1 }, cts.Token);

        var line = await h.AgentReads.ReadLineAsync(cts.Token);
        Assert.NotNull(line);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("initialize", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("params").GetProperty("protocolVersion").GetInt32());
        var id = doc.RootElement.GetProperty("id").GetInt64();

        await h.AgentWrites.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"protocolVersion\":1}}}}");

        var result = await sendTask;
        Assert.Equal(1, result.GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public async Task SendRequest_throws_AcpRpcException_with_the_error_code()
    {
        await using var h = new Harness();
        using var cts = new CancellationTokenSource(Timeout);

        var sendTask = h.Peer.SendRequestAsync("session/new", new { }, cts.Token);
        var line = await h.AgentReads.ReadLineAsync(cts.Token);
        var id = JsonDocument.Parse(line!).RootElement.GetProperty("id").GetInt64();

        await h.AgentWrites.WriteLineAsync(
            $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-32000,\"message\":\"auth required\"}}}}");

        var ex = await Assert.ThrowsAsync<AcpRpcException>(() => sendTask);
        Assert.Equal(AcpRpcException.AuthRequired, ex.Code);
        Assert.Equal("auth required", ex.Message);
    }

    [Fact]
    public async Task SendRequest_correlates_out_of_order_responses_by_id()
    {
        await using var h = new Harness();
        using var cts = new CancellationTokenSource(Timeout);

        var first = h.Peer.SendRequestAsync("m/one", null, cts.Token);
        var second = h.Peer.SendRequestAsync("m/two", null, cts.Token);

        var line1 = await h.AgentReads.ReadLineAsync(cts.Token);
        var line2 = await h.AgentReads.ReadLineAsync(cts.Token);
        var id1 = JsonDocument.Parse(line1!).RootElement.GetProperty("id").GetInt64();
        var id2 = JsonDocument.Parse(line2!).RootElement.GetProperty("id").GetInt64();

        // Respond in reverse order — the second request must not steal the first's response.
        await h.AgentWrites.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id2},\"result\":{{\"which\":\"two\"}}}}");
        await h.AgentWrites.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id1},\"result\":{{\"which\":\"one\"}}}}");

        Assert.Equal("one", (await first).GetProperty("which").GetString());
        Assert.Equal("two", (await second).GetProperty("which").GetString());
    }

    [Fact]
    public async Task Inbound_notification_is_dispatched_to_OnNotification()
    {
        await using var h = new Harness();
        var received = new TaskCompletionSource<(string Method, JsonElement Params)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.OnNotification = (method, prms) =>
        {
            received.TrySetResult((method, prms));
            return Task.CompletedTask;
        };

        await h.AgentWrites.WriteLineAsync(
            "{\"jsonrpc\":\"2.0\",\"method\":\"session/update\",\"params\":{\"sessionId\":\"s1\",\"update\":{\"sessionUpdate\":\"agent_message_chunk\"}}}");

        using var cts = new CancellationTokenSource(Timeout);
        var (method, prms) = await received.Task.WaitAsync(cts.Token);
        Assert.Equal("session/update", method);
        Assert.Equal("s1", prms.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task Inbound_request_is_answered_with_the_OnRequest_result()
    {
        await using var h = new Harness();
        h.Peer.OnRequest = (method, prms, ct) =>
        {
            Assert.Equal("session/request_permission", method);
            return Task.FromResult<object?>(new { outcome = new { outcome = "selected", optionId = "allow" } });
        };

        await h.AgentWrites.WriteLineAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"session/request_permission\",\"params\":{\"sessionId\":\"s1\"}}");

        using var cts = new CancellationTokenSource(Timeout);
        var line = await h.AgentReads.ReadLineAsync(cts.Token);
        using var doc = JsonDocument.Parse(line!);
        Assert.Equal(42, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("selected",
            doc.RootElement.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Inbound_request_without_a_handler_gets_method_not_found()
    {
        await using var h = new Harness();

        await h.AgentWrites.WriteLineAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"fs/read_text_file\",\"params\":{}}");

        using var cts = new CancellationTokenSource(Timeout);
        var line = await h.AgentReads.ReadLineAsync(cts.Token);
        using var doc = JsonDocument.Parse(line!);
        Assert.Equal(-32601, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Malformed_lines_are_tolerated_and_parsing_continues()
    {
        await using var h = new Harness();
        using var cts = new CancellationTokenSource(Timeout);

        var sendTask = h.Peer.SendRequestAsync("ping", null, cts.Token);
        var line = await h.AgentReads.ReadLineAsync(cts.Token);
        var id = JsonDocument.Parse(line!).RootElement.GetProperty("id").GetInt64();

        // Real agents occasionally leak banner/log noise to stdout despite the spec.
        await h.AgentWrites.WriteLineAsync("this is not json at all");
        await h.AgentWrites.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{}}}}");

        await sendTask; // resolves — the noise line didn't poison the stream
    }

    [Fact]
    public async Task Agent_closing_stdout_fails_pending_requests()
    {
        await using var h = new Harness();
        using var cts = new CancellationTokenSource(Timeout);

        var sendTask = h.Peer.SendRequestAsync("session/prompt", null, cts.Token);
        await h.AgentReads.ReadLineAsync(cts.Token); // confirm the request went out
        await h.CloseAgentOutputAsync();

        await Assert.ThrowsAsync<IOException>(() => sendTask);
    }
}
