using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class VariableGeneratorServiceTests
{
    [Fact]
    public void Generate_IntegerRange_StaysWithinBounds()
    {
        var service = new VariableGeneratorService();
        var definition = new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Integer,
            MinInt = 10,
            MaxInt = 20,
        };

        for (var index = 0; index < 50; index++)
        {
            var result = service.Generate(definition, new Dictionary<string, string?>());
            Assert.True(result.IsSuccess, result.Warning);
            var value = Assert.IsType<string>(result.Value);
            var number = int.Parse(value);
            Assert.InRange(number, 10, 20);
        }
    }

    [Fact]
    public void Generate_InvalidIntegerRange_ReturnsWarning()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Integer,
            MinInt = 20,
            MaxInt = 10,
        }, new Dictionary<string, string?>());

        Assert.False(result.IsSuccess);
        Assert.Contains("min", result.Warning);
    }

    [Theory]
    [InlineData("person.firstName")]
    [InlineData("person.lastName")]
    [InlineData("person.fullName")]
    [InlineData("person.jobTitle")]
    [InlineData("internet.email")]
    [InlineData("internet.username")]
    [InlineData("internet.url")]
    [InlineData("internet.ip")]
    [InlineData("phone.number")]
    [InlineData("company.name")]
    [InlineData("company.catchPhrase")]
    [InlineData("address.city")]
    [InlineData("address.streetAddress")]
    [InlineData("address.zipCode")]
    [InlineData("address.country")]
    [InlineData("address.fullAddress")]
    [InlineData("lorem.word")]
    [InlineData("lorem.sentence")]
    [InlineData("lorem.paragraph")]
    [InlineData("commerce.productName")]
    [InlineData("commerce.price")]
    [InlineData("date.past")]
    [InlineData("date.future")]
    [InlineData("date.recent")]
    public void Generate_FakerCategory_ReturnsValue(string category)
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = category,
        }, new Dictionary<string, string?>());

        Assert.True(result.IsSuccess, result.Warning);
        Assert.False(string.IsNullOrWhiteSpace(result.Value));
    }

    [Fact]
    public void Generate_UnknownFakerCategory_ReturnsWarning()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "address.city.does-not-exist",
        }, new Dictionary<string, string?>());

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported faker category", result.Warning);
    }

    // ── Bounded faker dates ───────────────────────────────────────────────────

    [Fact]
    public void Generate_DateBetween_WithinExplicitBounds()
    {
        var after = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new DateTimeOffset(2024, 6, 30, 23, 59, 59, TimeSpan.Zero);

        for (var index = 0; index < 50; index++)
        {
            var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
            {
                Kind = VariableGeneratorKind.Faker,
                FakerCategory = "date.between",
                FakerDateAfter = after,
                FakerDateBefore = before,
            }, new Dictionary<string, string?>());

            Assert.True(result.IsSuccess, result.Warning);
            var value = DateTimeOffset.Parse(result.Value!);
            Assert.InRange(value, after, before);
        }
    }

    [Fact]
    public void Generate_DateBetween_WithoutBounds_ReturnsWarning()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "date.between",
        }, new Dictionary<string, string?>());

        Assert.False(result.IsSuccess);
        Assert.Contains("date.between", result.Warning);
    }

    [Fact]
    public void Generate_DateBetween_OnlyOneBound_ReturnsWarning()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "date.between",
            FakerDateAfter = DateTimeOffset.UtcNow.AddDays(-10),
        }, new Dictionary<string, string?>());

        Assert.False(result.IsSuccess);
        Assert.Contains("date.between", result.Warning);
    }

    [Fact]
    public void Generate_DateCategory_ReversedBounds_ReturnsWarning()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "date.between",
            FakerDateAfter = DateTimeOffset.UtcNow,
            FakerDateBefore = DateTimeOffset.UtcNow.AddDays(-1),
        }, new Dictionary<string, string?>());

        Assert.False(result.IsSuccess);
        Assert.Contains("after", result.Warning);
    }

    [Fact]
    public void Generate_DatePast_NoBounds_WithinPrecedingYear()
    {
        var earliest = DateTimeOffset.UtcNow.AddYears(-1).AddMinutes(-1);
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "date.past",
        }, new Dictionary<string, string?>());

        Assert.True(result.IsSuccess, result.Warning);
        var value = DateTimeOffset.Parse(result.Value!);
        Assert.InRange(value, earliest, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Generate_DateFuture_NoBounds_WithinFollowingYear()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "date.future",
        }, new Dictionary<string, string?>());

        Assert.True(result.IsSuccess, result.Warning);
        var value = DateTimeOffset.Parse(result.Value!);
        Assert.InRange(value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1).AddMinutes(1));
    }

    [Fact]
    public void Generate_DateRecent_NoBounds_WithinPreviousDay()
    {
        var earliest = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-1);
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "date.recent",
        }, new Dictionary<string, string?>());

        Assert.True(result.IsSuccess, result.Warning);
        var value = DateTimeOffset.Parse(result.Value!);
        Assert.InRange(value, earliest, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Generate_DatePast_ExplicitBounds_OverrideTheDefaultWindow()
    {
        var after = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new DateTimeOffset(2020, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "date.past",
            FakerDateAfter = after,
            FakerDateBefore = before,
        }, new Dictionary<string, string?>());

        Assert.True(result.IsSuccess, result.Warning);
        var value = DateTimeOffset.Parse(result.Value!);
        Assert.InRange(value, after, before);
    }

    // ── Integer edge cases ────────────────────────────────────────────────────

    [Fact]
    public void Generate_IntegerRange_MaxIntMaxValue_DoesNotOverflow()
    {
        var service = new VariableGeneratorService();
        var definition = new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Integer,
            MinInt = int.MaxValue - 5,
            MaxInt = int.MaxValue,
        };

        for (var index = 0; index < 50; index++)
        {
            var result = service.Generate(definition, new Dictionary<string, string?>());
            Assert.True(result.IsSuccess, result.Warning);
            var number = int.Parse(result.Value!);
            Assert.InRange(number, int.MaxValue - 5, int.MaxValue);
        }
    }

    [Fact]
    public void Generate_IntegerRange_FullIntRange_StaysWithinBounds()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Integer,
            MinInt = int.MinValue,
            MaxInt = int.MaxValue,
        }, new Dictionary<string, string?>());

        Assert.True(result.IsSuccess, result.Warning);
        Assert.True(int.TryParse(result.Value, out _));
    }

    [Fact]
    public void BuildScope_GeneratedCollectionVariable_ResolvesValue()
    {
        var service = new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false));
        var scope = service.BuildScope([
            new CollectionVariable
            {
                Key = "age",
                Generator = new VariableGeneratorDefinition
                {
                    Kind = VariableGeneratorKind.Integer,
                    MinInt = 10,
                    MaxInt = 20,
                },
            },
        ], []);

        var age = int.Parse(scope["age"]!);
        Assert.InRange(age, 10, 20);
    }

    [Fact]
    public void BuildScope_GeneratedEnvironmentVariable_OverridesCollectionVariable()
    {
        var service = new VariableSubstitutionService(new StubCredentialStore(), new StubKeyVaultResolver(available: false));
        var env = new ApiEnvironment
        {
            Variables =
            [
                new EnvironmentVariable
                {
                    Key = "id",
                    SecretSource = EnvironmentVariableSecretSource.Generated,
                    Generator = new VariableGeneratorDefinition { Kind = VariableGeneratorKind.Guid },
                    IsEnabled = true,
                },
            ],
        };

        var scope = service.BuildScope([new CollectionVariable { Key = "id", Value = "fixed" }], [env]);

        Assert.NotEqual("fixed", scope["id"]);
        Assert.True(Guid.TryParse(scope["id"], out _));
    }
}
