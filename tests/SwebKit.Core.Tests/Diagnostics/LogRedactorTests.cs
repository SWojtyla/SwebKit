using SwebKit.Core.Diagnostics;

namespace SwebKit.Core.Tests.Diagnostics;

public class LogRedactorTests
{
    [Fact]
    public void Redact_SharedAccessKeyInConnectionString_ValueRedacted()
    {
        var message = "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=NOT-A-REAL-SECRET-abcdef";

        var result = LogRedactor.Redact(message);

        Assert.Contains("SharedAccessKey=***REDACTED***", result);
        Assert.DoesNotContain("NOT-A-REAL-SECRET-abcdef", result);
    }

    [Fact]
    public void Redact_AccountKeyInConnectionString_ValueRedacted()
    {
        var message = "DefaultEndpointsProtocol=https;AccountName=contoso;AccountKey=superSecretAccountKeyValue123==;EndpointSuffix=core.windows.net";

        var result = LogRedactor.Redact(message);

        Assert.Contains("AccountKey=***REDACTED***", result);
        Assert.DoesNotContain("superSecretAccountKeyValue123==", result);
    }

    [Fact]
    public void Redact_BearerToken_TokenRedactedPrefixRetained()
    {
        var message = "Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.payload.signature";

        var result = LogRedactor.Redact(message);

        Assert.Contains("Bearer ***REDACTED***", result);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9", result);
    }

    [Fact]
    public void RedactScopeValue_DenylistedKey_ValueAlwaysRedactedRegardlessOfShape()
    {
        var result = LogRedactor.RedactScopeValue("Pat", "not-shaped-like-a-secret");

        Assert.Equal("***REDACTED***", result);
    }

    [Fact]
    public void RedactScopeValue_NonDenylistedKey_ValuePassedThroughUnchanged()
    {
        var result = LogRedactor.RedactScopeValue("Namespace", "contoso-sb");

        Assert.Equal("contoso-sb", result);
    }

    [Fact]
    public void Redact_ExceptionTextWithEmbeddedConnectionString_Redacted()
    {
        var exceptionText = "System.Exception: Send failed. Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKey=topsecretvalue1234\n   at SwebKit.Azure.ServiceBus.AzureServiceBusClient.SendAsync()";

        var result = LogRedactor.Redact(exceptionText);

        Assert.Contains("SharedAccessKey=***REDACTED***", result);
        Assert.DoesNotContain("topsecretvalue1234", result);
    }

    [Fact]
    public void Redact_PlainMessageWithNoSecrets_PassedThroughUnchanged()
    {
        var message = "Connected to namespace contoso-sb successfully";

        var result = LogRedactor.Redact(message);

        Assert.Equal(message, result);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsSameValue()
    {
        Assert.Null(LogRedactor.Redact(null));
        Assert.Equal(string.Empty, LogRedactor.Redact(string.Empty));
    }
}
