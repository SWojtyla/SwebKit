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

    [Fact]
    public void Generate_FakerFirstName_ReturnsValue()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Faker,
            FakerCategory = "person.firstName",
        }, new Dictionary<string, string?>());

        Assert.True(result.IsSuccess, result.Warning);
        Assert.False(string.IsNullOrWhiteSpace(result.Value));
    }

    [Fact]
    public void Generate_Template_ComposesScopeValues()
    {
        var result = new VariableGeneratorService().Generate(new VariableGeneratorDefinition
        {
            Kind = VariableGeneratorKind.Template,
            Template = "{{first}}.{{last}}@example.com",
        }, new Dictionary<string, string?>
        {
            ["first"] = "nora",
            ["last"] = "swift",
        });

        Assert.True(result.IsSuccess, result.Warning);
        Assert.Equal("nora.swift@example.com", result.Value);
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
        ], null);

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

        var scope = service.BuildScope([new CollectionVariable { Key = "id", Value = "fixed" }], env);

        Assert.NotEqual("fixed", scope["id"]);
        Assert.True(Guid.TryParse(scope["id"], out _));
    }
}
