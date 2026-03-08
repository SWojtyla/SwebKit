using SwebKit.Core.Models;

namespace SwebKit.Core.Tests;

public class RemapRulesTests
{
    [Fact]
    public void IsEmpty_DefaultInstance_IsTrue()
    {
        var rules = new RemapRules();
        Assert.True(rules.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithOverrideSubject_IsFalse()
    {
        var rules = new RemapRules { OverrideSubject = "new-subject" };
        Assert.False(rules.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithOverrideCorrelationId_IsFalse()
    {
        var rules = new RemapRules { OverrideCorrelationId = "corr-xyz" };
        Assert.False(rules.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithPropertyRename_IsFalse()
    {
        var rules = new RemapRules();
        rules.PropertyRenames["old"] = "new";
        Assert.False(rules.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithPropertyRemove_IsFalse()
    {
        var rules = new RemapRules();
        rules.PropertyRemoves.Add("remove-me");
        Assert.False(rules.IsEmpty);
    }

    [Fact]
    public void IsEmpty_AfterClearingAllFields_IsTrue()
    {
        var rules = new RemapRules
        {
            OverrideSubject = "x",
            OverrideCorrelationId = "y"
        };
        rules.PropertyRenames["a"] = "b";
        rules.PropertyRemoves.Add("c");

        rules.OverrideSubject = null;
        rules.OverrideCorrelationId = null;
        rules.PropertyRenames.Clear();
        rules.PropertyRemoves.Clear();

        Assert.True(rules.IsEmpty);
    }
}
