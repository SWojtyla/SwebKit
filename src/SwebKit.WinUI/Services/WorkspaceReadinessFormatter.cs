namespace SwebKit.WinUI.Services;

public sealed record WorkspaceReadinessState(string Title, string Message);

public static class WorkspaceReadinessFormatter
{
    public static bool TryFormatPipelines(Exception exception, string? organization, out WorkspaceReadinessState state)
    {
        if (exception is InvalidOperationException
            && (exception.Message.Contains("connection test did not succeed", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("organization URL", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("organization input", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("organization is invalid", StringComparison.OrdinalIgnoreCase)))
        {
            var organizationLabel = string.IsNullOrWhiteSpace(organization)
                ? "the configured Azure DevOps organization"
                : $"'{organization.Trim()}'";

            state = new WorkspaceReadinessState(
                "Azure DevOps access needs attention",
                $"The Pipelines route could not verify read-only access for {organizationLabel}. Check the organization URL, PAT scope, or Azure DevOps access permissions in Settings, then refresh.");
            return true;
        }

        state = null!;
        return false;
    }

    public static bool TryFormatObservability(Exception exception, string? resourceName, out WorkspaceReadinessState state)
    {
        if (!ContainsAzureIdentityFailure(exception))
        {
            state = null!;
            return false;
        }

        var target = string.IsNullOrWhiteSpace(resourceName)
            ? "Application Insights resource discovery"
            : $"Application Insights access for '{resourceName}'";

        state = new WorkspaceReadinessState(
            "Azure sign-in is required",
            $"The Observability route could not complete {target} with the current Azure credential chain. Run az login or refresh your Azure sign-in outside SwebKit, then try again. Demo mode remains available for route validation.");
        return true;
    }

    private static bool ContainsAzureIdentityFailure(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            var typeName = current.GetType().FullName ?? string.Empty;
            var message = current.Message ?? string.Empty;

            if (typeName.Contains("Azure.Identity", StringComparison.Ordinal)
                || typeName.Contains("AuthenticationFailedException", StringComparison.Ordinal)
                || message.Contains("DefaultAzureCredential", StringComparison.OrdinalIgnoreCase)
                || message.Contains("CredentialUnavailableException", StringComparison.OrdinalIgnoreCase)
                || message.Contains("AzureCliCredential", StringComparison.OrdinalIgnoreCase)
                || message.Contains("AzurePowerShellCredential", StringComparison.OrdinalIgnoreCase)
                || message.Contains("AzureDeveloperCliCredential", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ManagedIdentityCredential", StringComparison.OrdinalIgnoreCase)
                || message.Contains("VisualStudioCodeCredential", StringComparison.OrdinalIgnoreCase)
                || message.Contains("BrokerCredential", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        var queue = new Queue<Exception>();
        queue.Enqueue(exception);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            if (current is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    queue.Enqueue(innerException);
                }

                continue;
            }

            if (current.InnerException is not null)
            {
                queue.Enqueue(current.InnerException);
            }
        }
    }
}