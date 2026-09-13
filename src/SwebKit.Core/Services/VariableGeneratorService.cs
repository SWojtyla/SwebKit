using System.Globalization;
using Bogus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

public sealed class VariableGeneratorService : IVariableGeneratorService
{
    private readonly Faker _faker = new();

    // `scope` is unused now that the Template generator kind (the only one that referenced other
    // variables' values) has been removed, but it stays on the interface — VariableSubstitutionService
    // still builds and passes a real scope, and a future generator kind may want it again.
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
            "person.jobTitle" => _faker.Name.JobTitle(),
            "internet.email" => _faker.Internet.Email(),
            "internet.username" => _faker.Internet.UserName(),
            "internet.url" => _faker.Internet.Url(),
            "internet.ip" => _faker.Internet.Ip(),
            "phone.number" => _faker.Phone.PhoneNumber(),
            "company.name" => _faker.Company.CompanyName(),
            "company.catchPhrase" => _faker.Company.CatchPhrase(),
            "address.city" => _faker.Address.City(),
            "address.streetAddress" => _faker.Address.StreetAddress(),
            "address.zipCode" => _faker.Address.ZipCode(),
            "address.country" => _faker.Address.Country(),
            "address.fullAddress" => _faker.Address.FullAddress(),
            "lorem.word" => _faker.Lorem.Word(),
            "lorem.sentence" => _faker.Lorem.Sentence(),
            "lorem.paragraph" => _faker.Lorem.Paragraph(),
            "commerce.productName" => _faker.Commerce.ProductName(),
            "commerce.price" => _faker.Commerce.Price(),
            "date.past" => _faker.Date.Past().ToString("O", CultureInfo.InvariantCulture),
            "date.future" => _faker.Date.Future().ToString("O", CultureInfo.InvariantCulture),
            "date.recent" => _faker.Date.Recent().ToString("O", CultureInfo.InvariantCulture),
            _ => null,
        };

        return value is null
            ? VariableGenerationResult.Failure($"Unsupported faker category '{category}'.")
            : VariableGenerationResult.Success(value);
    }
}
