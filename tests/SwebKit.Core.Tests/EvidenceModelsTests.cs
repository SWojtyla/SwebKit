using System.Text.Json;
using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

/// <summary>Evidence view whitelist + permissive item parsing (agent-colleague item 1):
/// only whitelisted kind/param pairs may survive into a report — the frontend maps them
/// onto in-app routes — and invalid hints degrade to plain text, never dead links.</summary>
public class EvidenceModelsTests
{
    private static readonly DateTimeOffset Now = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ── EvidenceViewValidator ────────────────────────────────────────────────

    [Theory]
    [InlineData("serviceBus")]
    [InlineData("sql")]
    [InlineData("aks")]
    [InlineData("monitoring")]
    [InlineData("redis")]
    public void TryValidate_KnownKinds_Accepted(string kind)
    {
        var view = new EvidenceView { Kind = kind };
        // Fill every required param for the kind so the spec can't reject it.
        view.Params = kind switch
        {
            "serviceBus" => new() { ["ns"] = "prod" },
            "sql" => new() { ["connection"] = "prod" },
            "redis" => new() { ["cache"] = "prod" },
            "aks" => new() { ["ns"] = "prod" },
            _ => new() { ["tab"] = "reports" }, // monitoring
        };

        Assert.True(EvidenceViewValidator.TryValidate(view, out var sanitized));
        Assert.NotNull(sanitized);
        Assert.Equal(kind, sanitized!.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aws-console")]
    [InlineData("external")]
    public void TryValidate_UnknownOrEmptyKind_Rejected(string kind)
    {
        Assert.False(EvidenceViewValidator.TryValidate(
            new EvidenceView { Kind = kind, Params = new() { ["ns"] = "x" } },
            out var sanitized));
        Assert.Null(sanitized);
    }

    [Fact]
    public void TryValidate_NullView_Rejected()
    {
        Assert.False(EvidenceViewValidator.TryValidate(null, out var sanitized));
        Assert.Null(sanitized);
    }

    [Theory]
    [InlineData("serviceBus")]
    [InlineData("sql")]
    [InlineData("redis")]
    public void TryValidate_MissingRequiredParam_Rejected(string kind)
    {
        var view = new EvidenceView { Kind = kind };
        Assert.False(EvidenceViewValidator.TryValidate(view, out _));
    }

    [Fact]
    public void TryValidate_NoRequiredParams_ButNoUsableParams_Rejected()
    {
        // aks/monitoring have no required params — but a hint with nothing to
        // navigate to is still useless.
        Assert.False(EvidenceViewValidator.TryValidate(
            new EvidenceView { Kind = "aks" }, out _));
        Assert.False(EvidenceViewValidator.TryValidate(
            new EvidenceView { Kind = "monitoring", Params = new() { ["bogus"] = "x" } },
            out _));
    }

    [Fact]
    public void TryValidate_UnknownParams_StrippedNotFatal()
    {
        var view = new EvidenceView
        {
            Kind = "sql",
            Params = new()
            {
                ["connection"] = "prod",
                ["evil"] = "javascript:alert(1)",
            },
        };

        Assert.True(EvidenceViewValidator.TryValidate(view, out var sanitized));
        var param = Assert.Single(sanitized!.Params);
        Assert.Equal("connection", param.Key);
        Assert.Equal("prod", param.Value);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("dlq", true)]
    [InlineData("bogus", false)]
    [InlineData("", false)]
    public void TryValidate_ServiceBusViewParam_EnumEnforced(string viewParam, bool keepsParam)
    {
        var view = new EvidenceView
        {
            Kind = "serviceBus",
            Params = new() { ["ns"] = "prod", ["view"] = viewParam },
        };

        Assert.True(EvidenceViewValidator.TryValidate(view, out var sanitized));
        Assert.Equal(keepsParam, sanitized!.Params.ContainsKey("view"));
    }

    [Theory]
    [InlineData("pods", true)]
    [InlineData("bogus-tab", false)]
    public void TryValidate_AksTabParam_EnumEnforced(string tab, bool keepsParam)
    {
        var view = new EvidenceView
        {
            Kind = "aks",
            Params = new() { ["ns"] = "prod", ["tab"] = tab },
        };

        Assert.True(EvidenceViewValidator.TryValidate(view, out var sanitized));
        Assert.Equal(keepsParam, sanitized!.Params.ContainsKey("tab"));
    }

    [Fact]
    public void TryValidate_KindCasing_Normalized()
    {
        var view = new EvidenceView
        {
            Kind = "SERVICEbus",
            Params = new() { ["ns"] = "prod" },
        };

        Assert.True(EvidenceViewValidator.TryValidate(view, out var sanitized));
        Assert.Equal("serviceBus", sanitized!.Kind);
    }

    [Fact]
    public void TryValidate_OversizedValue_Dropped()
    {
        var view = new EvidenceView
        {
            Kind = "serviceBus",
            Params = new() { ["ns"] = new string('x', 600) },
        };

        // Required param dropped → the whole view is unusable.
        Assert.False(EvidenceViewValidator.TryValidate(view, out _));
    }

    [Fact]
    public void TryValidate_ControlCharacters_Dropped()
    {
        var view = new EvidenceView
        {
            Kind = "sql",
            Params = new() { ["connection"] = "ok", ["table"] = "a\nb" },
        };

        Assert.True(EvidenceViewValidator.TryValidate(view, out var sanitized));
        Assert.False(sanitized!.Params.ContainsKey("table"));
    }

    // ── EvidenceItemParser ───────────────────────────────────────────────────

    [Fact]
    public void Parse_PlainStrings_BecomeTextOnlyItems()
    {
        var el = JsonDocument.Parse("""["one", "two"]""").RootElement;

        var items = EvidenceItemParser.Parse(el, Now);

        Assert.Equal(2, items.Count);
        Assert.Equal("one", items[0].Text);
        Assert.Null(items[0].View);
        Assert.Null(items[0].Watch);
        Assert.Equal(Now, items[0].CapturedAt);
    }

    [Fact]
    public void Parse_ObjectEntry_CarriesTextToolViewWatch()
    {
        var el = JsonDocument.Parse("""
            [{
                "text": "queue is red",
                "tool": "get_queue",
                "view": {"kind": "serviceBus", "params": {"ns": "prod", "entity": "orders"}},
                "watch": {"source": "ServiceBusDlqDepth",
                          "params": {"namespaceConnectionAlias": "prod-sb", "entityPath": "orders"}}
            }]
            """).RootElement;

        var item = Assert.Single(EvidenceItemParser.Parse(el, Now));
        Assert.Equal("queue is red", item.Text);
        Assert.Equal("get_queue", item.Tool);
        Assert.Equal("serviceBus", item.View!.Kind);
        Assert.Equal("prod", item.View.Params["ns"]);
        Assert.Equal("ServiceBusDlqDepth", item.Watch!.Source);
        Assert.Equal("prod-sb", item.Watch.Params["namespaceConnectionAlias"].GetString());
    }

    [Fact]
    public void Parse_InvalidView_DroppedButFindingKept()
    {
        var el = JsonDocument.Parse("""
            [{"text": "queue is red", "view": {"kind": "aws", "params": {}}}]
            """).RootElement;

        var item = Assert.Single(EvidenceItemParser.Parse(el, Now));
        Assert.Equal("queue is red", item.Text);
        Assert.Null(item.View);
    }

    [Fact]
    public void Parse_ObjectWithoutText_Skipped()
    {
        var el = JsonDocument.Parse("""[{"tool": "x"}]""").RootElement;
        Assert.Empty(EvidenceItemParser.Parse(el, Now));
    }

    [Fact]
    public void Parse_NonArrayInput_ReturnsEmpty()
    {
        Assert.Empty(EvidenceItemParser.Parse(JsonDocument.Parse("\"just a string\"").RootElement, Now));
        Assert.Empty(EvidenceItemParser.Parse(JsonDocument.Parse("{}").RootElement, Now));
    }

    [Fact]
    public void Parse_NumberEntry_DegradesToRawText()
    {
        var el = JsonDocument.Parse("""[42]""").RootElement;
        var item = Assert.Single(EvidenceItemParser.Parse(el, Now));
        Assert.Equal("42", item.Text);
    }

    private static JsonElement WatchJson(string source) =>
        JsonDocument.Parse(
            """[{"text":"t","watch":{"source":"__SRC__"}}]""".Replace("__SRC__", source)).RootElement;

    [Theory]
    [InlineData("ServiceBusDlqDepth")]
    [InlineData("AksPodRestartRate")]
    [InlineData("RedisMemoryUsage")]
    public void Parse_KnownAlertRuleSource_WatchAccepted(string source)
    {
        Assert.Equal(source, Assert.Single(EvidenceItemParser.Parse(WatchJson(source), Now)).Watch!.Source);
    }

    [Fact]
    public void Parse_WatchSourceCaseInsensitive_NormalizedToEnumCasing()
    {
        var el = JsonDocument.Parse("""[{"text":"t","watch":{"source":"servicebusdlqdepth"}}]""").RootElement;
        Assert.Equal("ServiceBusDlqDepth",
            Assert.Single(EvidenceItemParser.Parse(el, Now)).Watch!.Source);
    }

    [Theory]
    [InlineData("KqlQuery")]
    [InlineData("StorageBlobCount")]   // a real AlertRuleSource the dialog doesn't support →
                                      // kept here; the frontend whitelist narrows it out.
    public void Parse_WatchSource_DialogSupportIsTheFrontendsCall(string source)
    {
        var item = Assert.Single(EvidenceItemParser.Parse(WatchJson(source), Now));
        // StorageBlobCount is a real enum member → parses (frontend narrows); KqlQuery isn't → null.
        if (source == "KqlQuery")
            Assert.Null(item.Watch);
        else
            Assert.Equal("StorageBlobCount", item.Watch!.Source);
    }
}
