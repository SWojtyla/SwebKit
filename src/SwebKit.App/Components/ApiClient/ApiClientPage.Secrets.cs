using SwebKit.Core.Domain;

namespace SwebKit.App.Components.ApiClient;

/// <summary>
/// Secrets concern for <see cref="ApiClientPage"/>.
/// </summary>
/// <remarks>
/// Slice 2 of the decomposition tracked in
/// docs/features/active/api-client-page-decomposition/. Pure file-boundary move: no behavior
/// change. These members still mutate the page-owned <c>_state</c>/dialog fields and call other
/// partial-class members (<c>ActiveEnvironment</c>, <c>IsLinkedCollection</c>,
/// <c>FindLinkedRootForEnvironment</c>, <c>LoadLinkedRootsAsync</c>) directly, by design
/// (DEC-PD-1 in this feature's decisions.md).
/// </remarks>
public partial class ApiClientPage
{
    private bool _showConfigureSecretDialog;
    private string? _secretNameToConfigure;
    private string _secretValueToConfigure = string.Empty;
    private string? _secretConfigError;

    private IReadOnlyList<string> MissingSecretNames =>
        _state.SelectedRequest is not null && _state.ActiveCollection is not null &&
        IsLinkedCollection(_state.ActiveCollection.Id)
            ? GetMissingSecretNames(_state.SelectedRequest, ActiveEnvironment)
            : [];

    private void OpenConfigureSecretDialog()
    {
        _secretNameToConfigure = MissingSecretNames.Count > 0 ? MissingSecretNames[0] : null;
        _secretValueToConfigure = string.Empty;
        _secretConfigError = null;
        _showConfigureSecretDialog = true;
    }

    private async Task SaveConfiguredSecretAsync()
    {
        if (string.IsNullOrWhiteSpace(_secretNameToConfigure) || string.IsNullOrEmpty(_secretValueToConfigure))
            return;

        if (ActiveEnvironment is null)
        {
            _secretConfigError = "Select a linked environment before configuring this secret.";
            return;
        }

        var linkedEnv = FindLinkedRootForEnvironment(ActiveEnvironment.Id);
        if (linkedEnv is null)
        {
            _secretConfigError = "Select a linked environment before configuring this secret.";
            return;
        }

        var (linkedRoot, linkedEnvironmentFile) = linkedEnv.Value;

        var variableKey = $"secret:{_secretNameToConfigure}";
        var variable = ActiveEnvironment.Variables.FirstOrDefault(v => string.Equals(v.Key, variableKey,
            StringComparison.OrdinalIgnoreCase));
        if (variable is null)
        {
            variable = new EnvironmentVariable
            {
                Key = variableKey,
                IsEnabled = true,
                SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore,
                CredentialKey = $"swebkit/linked/{linkedRoot.Config.Id}/{ActiveEnvironment.Name}/{_secretNameToConfigure}",
            };
            ActiveEnvironment.Variables.Add(variable);
        }

        if (string.IsNullOrWhiteSpace(variable.CredentialKey))
        {
            variable.CredentialKey = $"swebkit/linked/{linkedRoot.Config.Id}/{ActiveEnvironment.Name}/{_secretNameToConfigure}";
        }

        variable.SecretSource = EnvironmentVariableSecretSource.WindowsCredentialStore;
        CredentialStore.Save(variable.CredentialKey, _secretValueToConfigure);
        await LinkedFileService.SaveEnvironmentAsync(linkedEnvironmentFile.EnvironmentFilePath, ActiveEnvironment);
        await LoadLinkedRootsAsync();
        _showConfigureSecretDialog = false;
        _secretValueToConfigure = string.Empty;
        _secretConfigError = null;
        await InvokeAsync(StateHasChanged);
    }

    private IReadOnlyList<string> GetMissingSecretNames(HttpRequestEntry request, ApiEnvironment? activeEnvironment)
    {
        var allSecretNames = ExtractSecretNames(request);
        if (allSecretNames.Count == 0)
            return [];

        if (activeEnvironment is null)
            return allSecretNames;

        return allSecretNames.Where(name => !IsSecretConfigured(activeEnvironment, name)).ToList();
    }

    private bool IsSecretConfigured(ApiEnvironment activeEnvironment, string secretName)
    {
        var variable = activeEnvironment.Variables.FirstOrDefault(v => string.Equals(v.Key, $"secret:{secretName}",
            StringComparison.OrdinalIgnoreCase));
        if (variable is null || !variable.IsEnabled)
            return false;

        if (variable.SecretSource == EnvironmentVariableSecretSource.AzureKeyVault)
            return !string.IsNullOrWhiteSpace(variable.CredentialKey);

        return !string.IsNullOrWhiteSpace(variable.CredentialKey) && CredentialStore.Get(variable.CredentialKey) is not null;
    }

    private static List<string> ExtractSecretNames(HttpRequestEntry request)
    {
        var values = new List<string?>
        {
            request.Url,
            request.Body.RawContent,
            request.GraphQlQuery,
            request.GraphQlVariables,
        };
        values.AddRange(request.Headers.Select(header => header.Value));
        values.AddRange(request.QueryParams.Select(param => param.Value));

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(static value => System.Text.RegularExpressions.Regex.Matches(value!,
                "\\{\\{secret:([^}]+)\\}\\}").Select(match => match.Groups[1].Value.Trim()))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
