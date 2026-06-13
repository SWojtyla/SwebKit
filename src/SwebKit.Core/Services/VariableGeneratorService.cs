using System.Globalization;
using System.Text.RegularExpressions;
using Bogus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

public sealed partial class VariableGeneratorService : IVariableGeneratorService
{
    private readonly Faker _faker = new();

    public VariableGenerationResult Generate(
        VariableGeneratorDefinition definition,
        IReadOnlyDictionary<string, string?> scope)
    {
        return definition.Kind switch
        {
            VariableGeneratorKind.Integer => GenerateInteger(definition),
            VariableGeneratorKind.Decimal => GenerateDecimal(definition),
            VariableGeneratorKind.Boolean => GenerateBoolean(definition),
            VariableGeneratorKind.Guid => VariableGenerationResult.Success(Guid.NewGuid().ToString("D")),
            VariableGeneratorKind.DateTime => VariableGenerationResult.Success(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            VariableGeneratorKind.List => GenerateListValue(definition),
            VariableGeneratorKind.Faker => GenerateFakerValue(definition),
            VariableGeneratorKind.Template => GenerateTemplate(definition, scope),
            _ => VariableGenerationResult.Failure($"Unsupported generator kind '{definition.Kind}'."),
        };
    }

    private static VariableGenerationResult GenerateInteger(VariableGeneratorDefinition definition)
    {
        var min = definition.MinInt ?? 0;
        var max = definition.MaxInt ?? 100;
        if (min > max)
        {
            return VariableGenerationResult.Failure("Integer generator min cannot be greater than max.");
        }

        return VariableGenerationResult.Success(Random.Shared.Next(min, max + 1).ToString(CultureInfo.InvariantCulture));
    }

    private static VariableGenerationResult GenerateDecimal(VariableGeneratorDefinition definition)
    {
        var min = definition.MinDecimal ?? 0m;
        var max = definition.MaxDecimal ?? 100m;
        if (min > max)
        {
            return VariableGenerationResult.Failure("Decimal generator min cannot be greater than max.");
        }

        var value = min + (decimal)Random.Shared.NextDouble() * (max - min);
        var rounded = Math.Round(value, Math.Clamp(definition.DecimalPlaces, 0, 8));
        return VariableGenerationResult.Success(rounded.ToString(CultureInfo.InvariantCulture));
    }

    private static VariableGenerationResult GenerateBoolean(VariableGeneratorDefinition definition)
    {
        var trueWeight = Math.Clamp(definition.TrueWeightPercent ?? 50, 0, 100);
        return VariableGenerationResult.Success(Random.Shared.Next(0, 100) < trueWeight ? "true" : "false");
    }

    private static VariableGenerationResult GenerateListValue(VariableGeneratorDefinition definition)
    {
        var values = definition.Values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (values.Count == 0)
        {
            return VariableGenerationResult.Failure("List generator requires at least one value.");
        }

        return VariableGenerationResult.Success(values[Random.Shared.Next(values.Count)]);
    }

    private VariableGenerationResult GenerateFakerValue(VariableGeneratorDefinition definition)
    {
        var category = definition.FakerCategory?.Trim() ?? "person.firstName";
        var value = category switch
        {
            "person.firstName" => _faker.Name.FirstName(),
            "person.lastName" => _faker.Name.LastName(),
            "person.fullName" => _faker.Name.FullName(),
            "internet.email" => _faker.Internet.Email(),
            "phone.number" => _faker.Phone.PhoneNumber(),
            "company.name" => _faker.Company.CompanyName(),
            _ => null,
        };

        return value is null
            ? VariableGenerationResult.Failure($"Unsupported faker category '{category}'.")
            : VariableGenerationResult.Success(value);
    }

    private static VariableGenerationResult GenerateTemplate(
        VariableGeneratorDefinition definition,
        IReadOnlyDictionary<string, string?> scope)
    {
        if (string.IsNullOrWhiteSpace(definition.Template))
        {
            return VariableGenerationResult.Failure("Template generator requires a template.");
        }

        var result = TokenPattern().Replace(definition.Template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            return scope.TryGetValue(key, out var value) && value is not null
                ? value
                : match.Value;
        });

        return VariableGenerationResult.Success(result);
    }

    [GeneratedRegex(@"\{\{([^{}]+?)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
