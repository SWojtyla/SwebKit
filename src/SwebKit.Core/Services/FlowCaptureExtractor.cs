using System.Text.Json;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Extracts captured values from request results based on capture mappings.
/// </summary>
public sealed class FlowCaptureExtractor
{
    /// <summary>
    /// Extracts captured values from a step result based on the step's capture mappings.
    /// </summary>
    public async Task<Dictionary<string, string>> ExtractAsync(
        ApiFlowStep step,
        HttpRequestResult requestResult)
    {
        var captures = new Dictionary<string, string>();

        foreach (var mapping in step.CaptureMappings.Where(m => m.IsEnabled))
        {
            try
            {
                string? value = null;

                switch (mapping.Source)
                {
                    case ApiFlowCaptureSource.BodyJsonPath:
                        value = ExtractJsonPathValue(requestResult.ResponseBody, mapping.JsonPath, single: true);
                        break;

                    case ApiFlowCaptureSource.BodyJsonPathArray:
                        value = ExtractJsonPathValue(requestResult.ResponseBody, mapping.JsonPath, single: false);
                        break;

                    case ApiFlowCaptureSource.ResponseHeader:
                        value = ExtractHeaderValue(requestResult, mapping.HeaderName);
                        break;

                    case ApiFlowCaptureSource.StatusCode:
                        value = requestResult.StatusCode?.ToString();
                        break;

                    case ApiFlowCaptureSource.ResponseBody:
                        value = requestResult.ResponseBody;
                        break;
                }

                if (value is not null)
                {
                    captures[mapping.TargetVariable] = value;
                }
                else if (mapping.DefaultValue is not null)
                {
                    captures[mapping.TargetVariable] = mapping.DefaultValue;
                }
            }
            catch
            {
                // Capture failed - skip silently
            }
        }

        return captures;
    }

    private static string? ExtractJsonPathValue(string? responseBody, string? jsonPath, bool single)
    {
        if (string.IsNullOrEmpty(responseBody) || string.IsNullOrEmpty(jsonPath))
            return null;

        try
        {
            var jsonDoc = JsonDocument.Parse(responseBody);
            var result = jsonDoc.RootElement.Select(jsonPath);

            if (single)
            {
                return result.FirstOrDefault()?.GetRawText();
            }
            else
            {
                return string.Join(",", result.Select(r => r.GetRawText()));
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractHeaderValue(HttpRequestResult requestResult, string? headerName)
    {
        if (string.IsNullOrEmpty(headerName))
            return null;

        var header = requestResult.Headers.FirstOrDefault(h =>
            string.Equals(h.Key, headerName, StringComparison.OrdinalIgnoreCase));
        return header.Value;
    }
}
