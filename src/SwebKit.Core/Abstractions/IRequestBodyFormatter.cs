using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>Formats request editor payloads without mutating invalid input.</summary>
public interface IRequestBodyFormatter
{
    BodyFormatResult FormatRestBody(RequestBodyMode mode, string? content);
    BodyFormatResult FormatGraphQlQuery(string? query);
    BodyFormatResult FormatJson(string? json);
}

public sealed record BodyFormatResult(bool IsSuccess, string? FormattedContent, string? ErrorMessage)
{
    public static BodyFormatResult Success(string formattedContent) => new(true, formattedContent, null);

    public static BodyFormatResult Failure(string errorMessage) => new(false, null, errorMessage);
}
