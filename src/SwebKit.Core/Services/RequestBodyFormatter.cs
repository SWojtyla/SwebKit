using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

public sealed class RequestBodyFormatter : IRequestBodyFormatter
{
    private static readonly JsonSerializerOptions JsonWriterOptions = new() { WriteIndented = true };

    public BodyFormatResult FormatRestBody(RequestBodyMode mode, string? content) => mode switch
    {
        RequestBodyMode.Json => FormatJson(content),
        RequestBodyMode.Xml => FormatXml(content),
        RequestBodyMode.Text => BodyFormatResult.Failure("Text bodies do not have a formatter."),
        _ => BodyFormatResult.Failure("This body type cannot be prettified."),
    };

    public BodyFormatResult FormatJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return BodyFormatResult.Failure("Nothing to prettify.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var formatted = JsonSerializer.Serialize(document.RootElement, JsonWriterOptions);
            return BodyFormatResult.Success(formatted);
        }
        catch (JsonException ex)
        {
            return BodyFormatResult.Failure($"Invalid JSON: {ex.Message}");
        }
    }

    public BodyFormatResult FormatGraphQlQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BodyFormatResult.Failure("Nothing to prettify.");
        }

        var formatted = FormatGraphQlDocument(query);
        return BodyFormatResult.Success(formatted);
    }

    private static BodyFormatResult FormatXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return BodyFormatResult.Failure("Nothing to prettify.");
        }

        try
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = document.Declaration is null,
            };

            using var writer = new StringWriter();
            using (var xmlWriter = XmlWriter.Create(writer, settings))
            {
                document.Save(xmlWriter);
            }

            return BodyFormatResult.Success(writer.ToString().TrimEnd());
        }
        catch (XmlException ex)
        {
            return BodyFormatResult.Failure($"Invalid XML: {ex.Message}");
        }
    }

    private static string FormatGraphQlDocument(string query)
    {
        var source = query.ReplaceLineEndings("\n").Trim();
        var output = new StringBuilder(source.Length + 32);
        var indentLevel = 0;
        var parenDepth = 0;
        var bracketDepth = 0;
        var pendingSpace = false;

        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];

            if (char.IsWhiteSpace(current))
            {
                if (ShouldBreakSelectionLine(source, index + 1, output, indentLevel, parenDepth, bracketDepth))
                {
                    AppendNewLine(output, indentLevel);
                    pendingSpace = false;
                }
                else
                {
                    pendingSpace = output.Length > 0 && !IsLineStart(output) && !IsWhitespace(output[^1]);
                }

                continue;
            }

            if (current == '#')
            {
                AppendPendingSpace(output, ref pendingSpace, current);
                AppendComment(source, output, ref index);
                AppendNewLine(output, indentLevel);
                continue;
            }

            if (current == '"')
            {
                AppendPendingSpace(output, ref pendingSpace, current);
                AppendQuotedString(source, output, ref index);
                continue;
            }

            switch (current)
            {
                case '{':
                    TrimTrailingWhitespace(output);
                    AppendSpaceBeforeBrace(output);
                    output.Append('{');
                    indentLevel++;
                    AppendNewLine(output, indentLevel);
                    pendingSpace = false;
                    break;

                case '}':
                    TrimTrailingWhitespaceAndNewLines(output);
                    indentLevel = Math.Max(0, indentLevel - 1);
                    AppendNewLine(output, indentLevel);
                    output.Append('}');
                    pendingSpace = false;
                    if (HasMoreTokens(source, index + 1))
                    {
                        AppendNewLine(output, indentLevel);
                    }
                    break;

                case '(':
                    parenDepth++;
                    TrimTrailingWhitespace(output);
                    output.Append(current);
                    pendingSpace = false;
                    break;

                case '[':
                    bracketDepth++;
                    TrimTrailingWhitespace(output);
                    output.Append(current);
                    pendingSpace = false;
                    break;

                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    TrimTrailingWhitespace(output);
                    output.Append(current);
                    pendingSpace = false;
                    break;

                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    TrimTrailingWhitespace(output);
                    output.Append(current);
                    pendingSpace = false;
                    break;

                case ':':
                    TrimTrailingWhitespace(output);
                    output.Append(": ");
                    pendingSpace = false;
                    break;

                case ',':
                    TrimTrailingWhitespace(output);
                    output.Append(", ");
                    pendingSpace = false;
                    break;

                default:
                    AppendPendingSpace(output, ref pendingSpace, current);
                    output.Append(current);
                    break;
            }
        }

        return output.ToString().Trim();
    }

    private static void AppendPendingSpace(StringBuilder output, ref bool pendingSpace, char current)
    {
        if (pendingSpace && output.Length > 0 && NeedsSpaceBetween(output[^1], current))
        {
            output.Append(' ');
        }

        pendingSpace = false;
    }

    private static bool NeedsSpaceBetween(char previous, char current) =>
        !IsWhitespace(previous) &&
        previous is not '(' and not '[' and not '{' and not ':' and not '@' and not '$' and not '.' &&
        current is not ')' and not ']' and not '}' and not ':' and not ',' and not '!' and not '.';

    private static bool ShouldBreakSelectionLine(
        string source,
        int nextStart,
        StringBuilder output,
        int indentLevel,
        int parenDepth,
        int bracketDepth)
    {
        if (indentLevel == 0 || parenDepth > 0 || bracketDepth > 0 || output.Length == 0 || IsLineStart(output))
        {
            return false;
        }

        var next = PeekNextNonWhitespace(source, nextStart);
        if (next is null or '{' or '}' or ')' or ']' or ',' or '@')
        {
            return false;
        }

        return output[^1] is not '(' and not '[' and not '{' and not ':' and not ',' and not '@' and not '$';
    }

    private static char? PeekNextNonWhitespace(string source, int start)
    {
        for (var index = start; index < source.Length; index++)
        {
            if (!char.IsWhiteSpace(source[index]))
            {
                return source[index];
            }
        }

        return null;
    }

    private static void AppendSpaceBeforeBrace(StringBuilder output)
    {
        if (output.Length == 0)
        {
            return;
        }

        var previous = output[^1];
        if (!IsWhitespace(previous) && previous is not '(' and not '[' and not '{')
        {
            output.Append(' ');
        }
    }

    private static void AppendQuotedString(string source, StringBuilder output, ref int index)
    {
        if (IsBlockStringStart(source, index))
        {
            output.Append("\"\"\"");
            index += 3;

            while (index < source.Length)
            {
                if (IsBlockStringStart(source, index))
                {
                    output.Append("\"\"\"");
                    index += 2;
                    return;
                }

                output.Append(source[index]);
                index++;
            }

            index--;
            return;
        }

        output.Append('"');
        index++;
        var escaped = false;

        while (index < source.Length)
        {
            var current = source[index];
            output.Append(current);

            if (current == '"' && !escaped)
            {
                return;
            }

            escaped = current == '\\' && !escaped;
            if (current != '\\')
            {
                escaped = false;
            }

            index++;
        }

        index--;
    }

    private static void AppendComment(string source, StringBuilder output, ref int index)
    {
        while (index < source.Length && source[index] != '\n')
        {
            output.Append(source[index]);
            index++;
        }
    }

    private static void AppendNewLine(StringBuilder output, int indentLevel)
    {
        TrimTrailingWhitespace(output);
        if (output.Length > 0 && output[^1] != '\n')
        {
            output.AppendLine();
        }

        output.Append(' ', indentLevel * 2);
    }

    private static bool HasMoreTokens(string source, int start)
    {
        for (var index = start; index < source.Length; index++)
        {
            if (!char.IsWhiteSpace(source[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlockStringStart(string source, int index) =>
        index + 2 < source.Length && source[index] == '"' && source[index + 1] == '"' && source[index + 2] == '"';

    private static bool IsLineStart(StringBuilder output)
    {
        for (var index = output.Length - 1; index >= 0; index--)
        {
            if (output[index] == '\n')
            {
                return true;
            }

            if (!char.IsWhiteSpace(output[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWhitespace(char value) => value is ' ' or '\t' or '\r' or '\n';

    private static void TrimTrailingWhitespace(StringBuilder output)
    {
        while (output.Length > 0 && output[^1] is ' ' or '\t')
        {
            output.Length--;
        }
    }

    private static void TrimTrailingWhitespaceAndNewLines(StringBuilder output)
    {
        while (output.Length > 0 && IsWhitespace(output[^1]))
        {
            output.Length--;
        }
    }
}
