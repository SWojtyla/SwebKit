using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Pins the sidecar's single error envelope. The frontend's <c>extractErrorMessage</c> (web/src/lib/api.ts)
/// probes <c>error</c> first, so every deliberate failure must serialize to <c>{"error": "..."}</c> —
/// a bare JSON string reached the UI as a quoted blob, and ProblemDetails only worked by accident.
/// </summary>
public class ApiErrorsTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public void Status_CarriesTheRequestedStatusCode(int statusCode)
    {
        var result = ApiErrors.Status(statusCode, "boom");

        Assert.Equal(statusCode, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public void Helpers_UseTheConventionalStatusCodes()
    {
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(ApiErrors.BadRequest("x")).StatusCode);
        Assert.Equal(401, Assert.IsAssignableFrom<IStatusCodeHttpResult>(ApiErrors.Unauthorized("x")).StatusCode);
        Assert.Equal(403, Assert.IsAssignableFrom<IStatusCodeHttpResult>(ApiErrors.Forbidden("x")).StatusCode);
        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(ApiErrors.NotFound("x")).StatusCode);
    }

    [Fact]
    public void Body_SerializesToTheCamelCaseErrorEnvelope()
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(ApiErrors.NotFound("Cache not found")).Value;

        var json = JsonSerializer.Serialize(value, WebOptions);

        Assert.Equal("{\"error\":\"Cache not found\"}", json);
    }
}
