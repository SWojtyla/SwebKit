using System.Text.Json;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// The single error-response shape for the whole sidecar: <c>{ "error": "&lt;message&gt;" }</c>.
/// </summary>
/// <remarks>
/// Endpoints used to return at least four different shapes — bare JSON strings
/// (<c>Results.BadRequest("blobName is required")</c>), anonymous <c>{ error }</c> objects,
/// <c>ProblemDetails</c> from <c>Results.Problem</c>, and the global exception handler's own
/// <c>{ error }</c> — so the frontend's <c>extractErrorMessage</c> had to guess. <c>{ error }</c>
/// wins because the global exception handler in <c>Program.cs</c> already emits it and it is the
/// first key <c>extractErrorMessage</c> probes, so every failure path now reaches the UI as a real
/// message instead of a quoted JSON string or a ProblemDetails blob.
///
/// This is only for errors the endpoint *deliberately* returns (validation, not-found, disabled
/// mutations). Unexpected exceptions must propagate to the global exception handler, which logs
/// them and applies the same shape — an endpoint-level <c>catch (Exception)</c> that turns an
/// exception into a response downgrades intended 400/401s to 500, leaks <c>ex.Message</c>, and
/// skips logging entirely.
/// </remarks>
internal static class ApiErrors
{
    // Explicit web defaults rather than relying on the DI-registered JsonOptions, so a handler
    // invoked directly from a unit test serializes the property as "error" too.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IResult Status(int statusCode, string message) =>
        Results.Json(new ApiErrorResponse(message), SerializerOptions, statusCode: statusCode);

    public static IResult BadRequest(string message) => Status(StatusCodes.Status400BadRequest, message);

    public static IResult Unauthorized(string message) => Status(StatusCodes.Status401Unauthorized, message);

    public static IResult Forbidden(string message) => Status(StatusCodes.Status403Forbidden, message);

    public static IResult NotFound(string message) => Status(StatusCodes.Status404NotFound, message);
}

/// <summary>Body of every deliberate sidecar error response. See <see cref="ApiErrors"/>.</summary>
/// <param name="Error">Human-readable, secret-free description of what went wrong.</param>
internal sealed record ApiErrorResponse(string Error);
