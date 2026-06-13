using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

public interface IVariableGeneratorService
{
    VariableGenerationResult Generate(
        VariableGeneratorDefinition definition,
        IReadOnlyDictionary<string, string?> scope);
}

public sealed record VariableGenerationResult(bool IsSuccess, string? Value, string? Warning)
{
    public static VariableGenerationResult Success(string value) => new(true, value, null);

    public static VariableGenerationResult Failure(string warning) => new(false, null, warning);
}
