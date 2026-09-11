using SwebKit.Core.Models;
using SwebKit.Redis;
using StackExchange.Redis;

namespace SwebKit.Core.Tests;

/// <summary>
/// Covers the response-parsing and classification logic inside <see cref="RedisClient"/>.
/// Everything here runs on raw <see cref="RedisResult"/> / INFO payloads, so it needs no
/// live Redis server — but it is exactly the code that silently mis-reports server state
/// when a Redis version changes a reply shape.
/// </summary>
public sealed class RedisClientParsingTests
{
    // ── ToTypeString ──

    [Theory]
    [InlineData(RedisType.String, "string")]
    [InlineData(RedisType.Hash, "hash")]
    [InlineData(RedisType.List, "list")]
    [InlineData(RedisType.Set, "set")]
    [InlineData(RedisType.SortedSet, "zset")]
    [InlineData(RedisType.Stream, "stream")]
    [InlineData(RedisType.None, "none")]
    [InlineData(RedisType.Unknown, "none")]
    public void ToTypeString_MapsEveryRedisTypeOntoTheAppToken(RedisType type, string expected)
        => Assert.Equal(expected, RedisClient.ToTypeString(type));

    // ── BuildServerInfo: metric mapping ──

    [Fact]
    public void BuildServerInfo_MapsEachInfoFieldOntoItsOwnProperty()
    {
        var info = RedisClient.BuildServerInfo(
            Section("server",
                ("redis_version", "7.2.4"),
                ("uptime_in_seconds", "86400"),
                ("connected_clients", "42"),
                ("used_memory", "1048576"),
                ("maxmemory", "10485760"),
                ("used_memory_human", "1.00M"),
                ("total_commands_processed", "999")),
            []);

        Assert.Equal("7.2.4", info.RedisVersion);
        Assert.Equal(86_400, info.UptimeSeconds);
        Assert.Equal(42, info.ConnectedClients);
        Assert.Equal(1_048_576, info.UsedMemoryBytes);
        Assert.Equal(10_485_760, info.MaxMemoryBytes);
        Assert.Equal("1.00M", info.UsedMemoryHuman);
        Assert.Equal(999, info.TotalCommandsProcessed);
    }

    [Fact]
    public void BuildServerInfo_MergesFieldsAcrossInfoSections()
    {
        var sections = Section("server", ("redis_version", "7.2.4"))
            .Concat(Section("clients", ("connected_clients", "7")))
            .ToArray();

        var info = RedisClient.BuildServerInfo(sections, []);

        Assert.Equal("7.2.4", info.RedisVersion);
        Assert.Equal(7, info.ConnectedClients);
    }

    [Fact]
    public void BuildServerInfo_MissingFields_FallBackToDefaults()
    {
        var info = RedisClient.BuildServerInfo([], []);

        Assert.Equal(string.Empty, info.RedisVersion);
        Assert.Equal(string.Empty, info.UsedMemoryHuman);
        Assert.Equal(0, info.UptimeSeconds);
        Assert.Equal(0, info.MaxMemoryBytes);
        Assert.Empty(info.Databases);
    }

    [Fact]
    public void BuildServerInfo_NonNumericValue_BecomesZeroRatherThanThrowing()
    {
        var info = RedisClient.BuildServerInfo(Section("memory", ("used_memory", "not-a-number")), []);

        Assert.Equal(0, info.UsedMemoryBytes);
    }

    [Fact]
    public void BuildServerInfo_FieldLookupIsCaseInsensitive()
    {
        var info = RedisClient.BuildServerInfo(Section("server", ("Redis_Version", "7.0.0")), []);

        Assert.Equal("7.0.0", info.RedisVersion);
    }

    // ── BuildServerInfo: keyspace hit ratio ──

    [Fact]
    public void BuildServerInfo_ComputesTheKeyspaceHitRatio()
    {
        var info = RedisClient.BuildServerInfo(
            Section("stats", ("keyspace_hits", "75"), ("keyspace_misses", "25")),
            []);

        Assert.Equal(0.75, info.KeyspaceHitRatio, 10);
    }

    [Fact]
    public void BuildServerInfo_NoHitsAndNoMisses_ReportsZeroRatioNotNaN()
    {
        var info = RedisClient.BuildServerInfo(
            Section("stats", ("keyspace_hits", "0"), ("keyspace_misses", "0")),
            []);

        Assert.Equal(0, info.KeyspaceHitRatio);
        Assert.False(double.IsNaN(info.KeyspaceHitRatio));
    }

    [Fact]
    public void BuildServerInfo_AllHits_ReportsRatioOfOne()
    {
        var info = RedisClient.BuildServerInfo(
            Section("stats", ("keyspace_hits", "10"), ("keyspace_misses", "0")),
            []);

        Assert.Equal(1.0, info.KeyspaceHitRatio, 10);
    }

    // ── BuildServerInfo: keyspace database stats ──

    [Fact]
    public void BuildServerInfo_ParsesPerDatabaseKeyspaceStats()
    {
        var info = RedisClient.BuildServerInfo([], Section("keyspace", ("db0", "keys=1500,expires=42,avg_ttl=360000")));

        var db = Assert.Single(info.Databases);
        Assert.Equal(0, db.Index);
        Assert.Equal(1500, db.Keys);
        Assert.Equal(42, db.Expires);
        Assert.Equal(360_000, db.AvgTtl);
    }

    [Fact]
    public void BuildServerInfo_OrdersDatabasesByIndex()
    {
        var info = RedisClient.BuildServerInfo([], Section("keyspace",
            ("db9", "keys=1,expires=0,avg_ttl=0"),
            ("db2", "keys=1,expires=0,avg_ttl=0"),
            ("db11", "keys=1,expires=0,avg_ttl=0")));

        Assert.Equal([2, 9, 11], info.Databases.Select(d => d.Index));
    }

    [Fact]
    public void BuildServerInfo_IgnoresKeyspaceEntriesThatAreNotDatabases()
    {
        var info = RedisClient.BuildServerInfo([], Section("keyspace",
            ("db0", "keys=1,expires=0,avg_ttl=0"),
            ("dbxx", "keys=9,expires=0,avg_ttl=0"),
            ("something_else", "keys=9")));

        Assert.Single(info.Databases);
        Assert.Equal(0, info.Databases[0].Index);
    }

    [Fact]
    public void BuildServerInfo_MissingKeyspaceSubFields_DefaultToZero()
    {
        var info = RedisClient.BuildServerInfo([], Section("keyspace", ("db0", "keys=5")));

        var db = Assert.Single(info.Databases);
        Assert.Equal(5, db.Keys);
        Assert.Equal(0, db.Expires);
        Assert.Equal(0, db.AvgTtl);
    }

    [Fact]
    public void BuildServerInfo_MalformedKeyspacePairsAreSkipped()
    {
        var info = RedisClient.BuildServerInfo([], Section("keyspace", ("db0", "keys,=7,expires=,=3,avg_ttl=9")));

        var db = Assert.Single(info.Databases);
        Assert.Equal(0, db.Keys);
        Assert.Equal(0, db.Expires);
        Assert.Equal(9, db.AvgTtl);
    }

    // ── ParseSlowLogEntries ──

    [Fact]
    public void ParseSlowLogEntries_ProjectsAFullEntry()
    {
        var result = Arr(SlowLogEntry(
            id: 14,
            unixSeconds: 1_700_000_000,
            durationMicros: 12_345,
            commandArgs: ["GET", "session:abc", "extra"],
            clientAddress: "10.0.0.5:51234",
            clientName: "worker-1"));

        var entry = Assert.Single(RedisClient.ParseSlowLogEntries(result));

        Assert.Equal(14, entry.Id);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), entry.ExecutedAt);
        Assert.Equal(TimeSpan.FromMicroseconds(12_345), entry.Duration);
        Assert.Equal("GET", entry.Command);
        Assert.Equal("session:abc extra", entry.Arguments);
        Assert.Equal("worker-1", entry.ClientName);
    }

    [Fact]
    public void ParseSlowLogEntries_NullReply_ReturnsEmpty()
        => Assert.Empty(RedisClient.ParseSlowLogEntries(RedisResult.Create(RedisValue.Null)));

    [Fact]
    public void ParseSlowLogEntries_EmptyReply_ReturnsEmpty()
        => Assert.Empty(RedisClient.ParseSlowLogEntries(Arr()));

    [Fact]
    public void ParseSlowLogEntries_LegacyFourFieldEntry_HasNoClientName()
    {
        var result = Arr(Arr(
            Value(1), Value(1_700_000_000), Value(500),
            Arr(Value("PING"))));

        var entry = Assert.Single(RedisClient.ParseSlowLogEntries(result));

        Assert.Null(entry.ClientName);
        Assert.Equal("PING", entry.Command);
        Assert.Equal(string.Empty, entry.Arguments);
    }

    [Fact]
    public void ParseSlowLogEntries_BlankClientName_BecomesNull()
    {
        var result = Arr(SlowLogEntry(1, 1_700_000_000, 100, ["PING"], "10.0.0.5:1", string.Empty));

        Assert.Null(Assert.Single(RedisClient.ParseSlowLogEntries(result)).ClientName);
    }

    [Fact]
    public void ParseSlowLogEntries_TooFewFields_IsSkipped()
    {
        var result = Arr(Arr(Value(1), Value(2), Value(3)));

        Assert.Empty(RedisClient.ParseSlowLogEntries(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ParseSlowLogEntries_NonNumericHeaderField_IsSkipped(int corruptIndex)
    {
        var fields = new[] { Value(1), Value(1_700_000_000), Value(100), Arr(Value("PING")) };
        fields[corruptIndex] = Value("garbage");

        Assert.Empty(RedisClient.ParseSlowLogEntries(Arr(RedisResult.Create(fields))));
    }

    [Fact]
    public void ParseSlowLogEntries_CorruptRowDoesNotDiscardValidRows()
    {
        var result = Arr(
            Arr(Value("garbage"), Value(1), Value(1), Arr(Value("PING"))),
            SlowLogEntry(2, 1_700_000_000, 100, ["SET", "k"], "10.0.0.5:1", "cli"));

        var entry = Assert.Single(RedisClient.ParseSlowLogEntries(result));

        Assert.Equal(2, entry.Id);
    }

    [Fact]
    public void ParseSlowLogEntries_EmptyCommandArgs_YieldsBlankCommand()
    {
        var result = Arr(Arr(Value(1), Value(1_700_000_000), Value(100), Arr()));

        var entry = Assert.Single(RedisClient.ParseSlowLogEntries(result));

        Assert.Equal(string.Empty, entry.Command);
        Assert.Equal(string.Empty, entry.Arguments);
    }

    [Fact]
    public void ParseSlowLogEntries_PreservesReplyOrder()
    {
        var result = Arr(
            SlowLogEntry(3, 1_700_000_000, 1, ["A"], "c", "n"),
            SlowLogEntry(1, 1_700_000_000, 1, ["B"], "c", "n"));

        Assert.Equal([3L, 1L], RedisClient.ParseSlowLogEntries(result).Select(e => e.Id));
    }

    // ── ParseNumsubResult ──

    [Fact]
    public void ParseNumsubResult_PairsChannelsWithSubscriberCounts()
    {
        var result = Arr(Value("orders"), Value(3), Value("events"), Value(0));

        var infos = RedisClient.ParseNumsubResult(result, ["orders", "events"]);

        Assert.Equal(2, infos.Count);
        Assert.Equal(new RedisPubSubChannelInfo("orders", 3), infos[0]);
        Assert.Equal(new RedisPubSubChannelInfo("events", 0), infos[1]);
    }

    [Fact]
    public void ParseNumsubResult_NullReply_FallsBackToZeroCountsForEveryChannel()
    {
        var infos = RedisClient.ParseNumsubResult(RedisResult.Create(RedisValue.Null), ["a", "b"]);

        Assert.Equal(["a", "b"], infos.Select(i => i.Channel));
        Assert.All(infos, i => Assert.Equal(0, i.SubscriberCount));
    }

    [Fact]
    public void ParseNumsubResult_DanglingChannelWithoutACount_IsIgnored()
    {
        var result = Arr(Value("orders"), Value(3), Value("events"));

        var info = Assert.Single(RedisClient.ParseNumsubResult(result, ["orders", "events"]));

        Assert.Equal("orders", info.Channel);
    }

    [Fact]
    public void ParseNumsubResult_NonNumericCount_BecomesZero()
    {
        var result = Arr(Value("orders"), Value("many"));

        Assert.Equal(0, Assert.Single(RedisClient.ParseNumsubResult(result, ["orders"])).SubscriberCount);
    }

    [Fact]
    public void ParseNumsubResult_EmptyReply_YieldsNoChannels()
        => Assert.Empty(RedisClient.ParseNumsubResult(Arr(), ["orders"]));

    // ── IsPermissionError ──

    [Theory]
    [InlineData("NOPERM this user has no permissions to run the 'slowlog' command")]
    [InlineData("noperm")]
    [InlineData("ERR Permission denied")]
    [InlineData("access DENIED for this role")]
    [InlineData("This command is not allowed on this instance")]
    public void IsPermissionError_RecognisesAuthorizationFailures(string message)
        => Assert.True(RedisClient.IsPermissionError(message));

    [Theory]
    [InlineData("")]
    [InlineData("ERR unknown command 'SLOWLOG'")]
    [InlineData("LOADING Redis is loading the dataset in memory")]
    [InlineData("READONLY You can't write against a read only replica.")]
    public void IsPermissionError_LeavesOtherServerErrorsAlone(string message)
        => Assert.False(RedisClient.IsPermissionError(message));

    // ── Helpers ──

    private static IGrouping<string, KeyValuePair<string, string>>[] Section(
        string sectionName, params (string Key, string Value)[] pairs) =>
        pairs
            .Select(p => new KeyValuePair<string, string>(p.Key, p.Value))
            .GroupBy(_ => sectionName)
            .ToArray();

    private static RedisResult Value(string value) => RedisResult.Create((RedisValue)value);

    private static RedisResult Value(long value) => RedisResult.Create((RedisValue)value);

    private static RedisResult Arr(params RedisResult[] items) => RedisResult.Create(items);

    private static RedisResult SlowLogEntry(
        long id, long unixSeconds, long durationMicros,
        string[] commandArgs, string clientAddress, string clientName) =>
        Arr(
            Value(id),
            Value(unixSeconds),
            Value(durationMicros),
            Arr([.. commandArgs.Select(Value)]),
            Value(clientAddress),
            Value(clientName));
}
