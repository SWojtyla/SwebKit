using SwebKit.Sidecar.Services.Acp;

namespace SwebKit.Sidecar.Tests;

public class AcpPermissionStoreTests
{
    private static readonly AcpPermissionOption[] Options =
    [
        new("opt-allow", "Allow once", "allow_once"),
        new("opt-deny", "Reject once", "reject_once"),
    ];

    [Fact]
    public void Create_parks_the_request_and_lists_it_as_pending()
    {
        var store = new AcpPermissionStore();

        var entry = store.Create("acp-session-1", "Run tool X", Options);

        Assert.False(entry.Outcome.Task.IsCompleted);
        var pending = store.GetPending();
        Assert.Single(pending);
        Assert.Equal(entry.Id, pending[0].Id);
        Assert.Equal("Run tool X", pending[0].ToolCallTitle);
        Assert.Equal(2, pending[0].Options.Count);
    }

    [Fact]
    public async Task Respond_completes_the_outcome_with_the_chosen_option()
    {
        var store = new AcpPermissionStore();
        var entry = store.Create("s1", "tool", Options);

        Assert.True(store.Respond(entry.Id, "opt-deny"));

        Assert.Equal("opt-deny", await entry.Outcome.Task);
        Assert.Empty(store.GetPending());
    }

    [Fact]
    public void Respond_returns_false_for_an_unknown_id()
    {
        var store = new AcpPermissionStore();
        store.Create("s1", "tool", Options);

        Assert.False(store.Respond("no-such-id", "opt-allow"));
    }

    [Fact]
    public void Respond_returns_false_when_the_request_was_already_resolved()
    {
        var store = new AcpPermissionStore();
        var entry = store.Create("s1", "tool", Options);

        Assert.True(store.Respond(entry.Id, "opt-allow"));
        Assert.False(store.Respond(entry.Id, "opt-deny"));
    }
}
