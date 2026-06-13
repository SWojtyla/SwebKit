using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Core.Tests;

public sealed class RequestBodyFormatterTests
{
    private readonly RequestBodyFormatter _formatter = new();

    [Fact]
    public void FormatRestBody_Json_PrettyPrintsObject()
    {
        var result = _formatter.FormatRestBody(RequestBodyMode.Json, "{\"name\":\"api\",\"enabled\":true}");

        Assert.True(result.IsSuccess);
        Assert.Equal("""
            {
              "name": "api",
              "enabled": true
            }
            """.ReplaceLineEndings("\n"), result.FormattedContent?.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void FormatRestBody_InvalidJson_ReturnsErrorWithoutContent()
    {
        var result = _formatter.FormatRestBody(RequestBodyMode.Json, "{bad json}");

        Assert.False(result.IsSuccess);
        Assert.Null(result.FormattedContent);
        Assert.StartsWith("Invalid JSON:", result.ErrorMessage);
    }

    [Fact]
    public void FormatRestBody_Xml_PrettyPrintsDocument()
    {
        var result = _formatter.FormatRestBody(RequestBodyMode.Xml, "<root><item id=\"1\">value</item></root>");

        Assert.True(result.IsSuccess);
        Assert.Contains("<root>", result.FormattedContent);
        Assert.Contains("  <item id=\"1\">value</item>", result.FormattedContent);
        Assert.Contains("</root>", result.FormattedContent);
    }

    [Fact]
    public void FormatRestBody_Text_ReturnsUnsupportedFormatterError()
    {
        var result = _formatter.FormatRestBody(RequestBodyMode.Text, "hello");

        Assert.False(result.IsSuccess);
        Assert.Equal("Text bodies do not have a formatter.", result.ErrorMessage);
    }

    [Fact]
    public void FormatGraphQlQuery_PrettyPrintsSelectionSets()
    {
        var result = _formatter.FormatGraphQlQuery("query GetUser($id: ID!){user(id:$id){id name}}");

        Assert.True(result.IsSuccess);
        Assert.Equal("""
            query GetUser($id: ID!) {
              user(id: $id) {
                id
                name
              }
            }
            """.ReplaceLineEndings("\n"), result.FormattedContent?.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void FormatGraphQlQuery_PreservesStringsAndComments()
    {
        var result = _formatter.FormatGraphQlQuery("""
            query Search { # keep this comment
            search(text:"a { literal }") { label }
            }
            """);

        Assert.True(result.IsSuccess);
        Assert.Contains("# keep this comment", result.FormattedContent);
        Assert.Contains("\"a { literal }\"", result.FormattedContent);
        Assert.Contains("label", result.FormattedContent);
    }

    [Fact]
    public void FormatJson_EmptyContent_ReturnsNothingToPrettify()
    {
        var result = _formatter.FormatJson(" ");

        Assert.False(result.IsSuccess);
        Assert.Equal("Nothing to prettify.", result.ErrorMessage);
    }
}
