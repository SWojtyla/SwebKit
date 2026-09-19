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

        // Inclusive bound via Int64 so max = int.MaxValue doesn't wrap (max + 1 overflows Int32).
        var value = min + Random.Shared.NextInt64((long)max - min + 1);
        return VariableGenerationResult.Success(value.ToString(CultureInfo.InvariantCulture));
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

        // Date categories carry user bounds, so they resolve to a result directly rather than a string.
        var dateResult = category switch
        {
            "date.past" => GenerateFakerDate(definition, DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow),
            "date.future" => GenerateFakerDate(definition, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1)),
            "date.recent" => GenerateFakerDate(definition, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow),
            "date.between" => GenerateFakerDate(definition, null, null),
            _ => null,
        };
        if (dateResult is not null)
        {
            return dateResult;
        }

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
            _ => null,
        };

        return value is null
            ? VariableGenerationResult.Failure($"Unsupported faker category '{category}'.")
            : VariableGenerationResult.Success(value);
    }

    /// <summary>Picks a random instant inside the user's <c>after</c>/<c>before</c> bounds, falling
    /// back to the category's default window for whichever bound is unset. Returns a failure string
    /// for <c>date.between</c> without both bounds or when after &gt; before.</summary>
    private VariableGenerationResult GenerateFakerDate(
        VariableGeneratorDefinition definition,
        DateTimeOffset? defaultAfter,
        DateTimeOffset? defaultBefore)
    {
        var after = definition.FakerDateAfter ?? defaultAfter;
        var before = definition.FakerDateBefore ?? defaultBefore;
        if (after is null || before is null)
        {
            return VariableGenerationResult.Failure("The 'date.between' category requires both an 'after' and a 'before' date.");
        }

        if (after > before)
        {
            return VariableGenerationResult.Failure("Date generator 'after' cannot be later than 'before'.");
        }

        var value = _faker.Date.Between(after.Value.UtcDateTime, before.Value.UtcDateTime);
        return VariableGenerationResult.Success(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
