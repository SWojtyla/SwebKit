using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwebKit.App.Services;
using SwebKit.Azure;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Services;

namespace SwebKit.App.Hosting;

/// <summary>
/// Extension methods for API Client services: HTTP execution, variable substitution,
/// auth, GraphQL, WebSocket, import/export, and Key Vault resolution.
/// </summary>
public static partial class SwebKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers API Client variable substitution, HTTP execution, auth builders,
    /// GraphQL schema/subscription, WebSocket, import/export, and Key Vault resolver.
    /// </summary>
    public static IServiceCollection AddSwebKitApiClient(this IServiceCollection services)
    {
        services.AddSingleton<IVariableGeneratorService, VariableGeneratorService>();
        services.AddSingleton<IVariableSubstitutionService, VariableSubstitutionService>();
        services.AddSingleton<IVariablePreviewService, VariablePreviewService>();
        services.AddSingleton<ApiClientWorkflowService>();
        services.AddSingleton<IRequestBodyFormatter, RequestBodyFormatter>();
        services.AddSingleton<IPostRequestCaptureExecutor, PostRequestCaptureExecutor>();
        services.AddSingleton<IKeyVaultSecretResolver>(sp =>
        {
            var config = sp.GetRequiredService<AppStateService>().Config;

            // Multi-vault takes precedence; fall back to legacy single KeyVaultUrl for existing configs.
            if (config.KeyVaults.Count > 0)
                return new MultiVaultKeyVaultSecretResolver(
                    config.KeyVaults,
                    sp.GetRequiredService<ILogger<MultiVaultKeyVaultSecretResolver>>());

#pragma warning disable CS0618 // KeyVaultUrl obsolete — backward-compat path
            if (!string.IsNullOrWhiteSpace(config.KeyVaultUrl))
                return new AzureKeyVaultSecretResolver(
                    config.KeyVaultUrl,
                    sp.GetRequiredService<ILogger<AzureKeyVaultSecretResolver>>());
#pragma warning restore CS0618

            return new NoopKeyVaultSecretResolver();
        });
        services.AddTransient<IHttpRequestExecutor, HttpRequestExecutor>();
        services.AddSingleton<IOAuth2TokenManager, OAuth2TokenManager>();
        services.AddSingleton<IAuthHeaderBuilder, AuthHeaderBuilder>();
        services.AddSingleton<IAuthInheritanceResolver, AuthInheritanceResolver>();
        services.AddSingleton<IGraphQlSchemaService, GraphQlSchemaService>();
        services.AddTransient<IWebSocketClientService, WebSocketClientService>();
        services.AddTransient<Func<IWebSocketClientService>>(sp =>
            () => sp.GetRequiredService<IWebSocketClientService>());
        services.AddTransient<IGraphQlSubscriptionService, GraphQlSubscriptionService>();
        services.AddSingleton<LinkedGitService>();
        services.AddSingleton<LinkedCollectionFileService>();

        // API Client — export/import
        services.AddSingleton<SwebKitCollectionExporter>();
        services.AddSingleton<SwebKitCollectionImporter>();
        services.AddSingleton<SwebKitEnvironmentImporter>();
        services.AddSingleton<PostmanCollectionExporter>();
        services.AddSingleton<PostmanCollectionImporter>();
        services.AddSingleton<BrunoCollectionExporter>();
        services.AddSingleton<BrunoFolderImporter>();
        services.AddSingleton<CollectionImportService>();
        services.AddHttpClient(HttpRequestExecutor.ClientName)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<UserSettingsRepository>().Settings;
                return new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    ServerCertificateCustomValidationCallback =
                        settings.VerifyApiClientSsl
                            ? null
                            : (_, _, _, _) => true,
                };
            });

        return services;
    }
}
