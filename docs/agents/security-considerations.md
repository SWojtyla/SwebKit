# Security Considerations

## 🔒 Overview

This document outlines the **comprehensive security approach** for the SwebKit AI Agent, covering API key security, data protection, access control, and safe operation principles.

**Security is not an afterthought** - it must be designed into the system from Phase 0.

---

## 🎯 Security Principles

### 1. Least Privilege

Every component and user should have only the minimum permissions necessary to perform its function.

### 2. Defense in Depth

Multiple layers of security controls should protect against single points of failure.

### 3. Secure by Default

Security should be enabled and enforced by default, not optional.

### 4. Data Minimization

Only collect, store, and transmit the minimum data necessary for functionality.

### 5. Zero Trust

Never trust, always verify - authenticate and authorize every request.

### 6. Audit Everything

All security-relevant actions should be logged and auditable.

---

## 🔐 API Key Security

### Storage

**Requirements**:

- API keys must **never** be stored in plain text
- API keys must **never** be logged
- API keys must **never** be committed to source control

**Implementation Options**:

#### Option 1: Azure Key Vault (Recommended for Production)

```csharp
// Using existing SwebKit pattern
services.AddSingleton<IKeyVaultSecretResolver>(sp =>
{
    var config = sp.GetRequiredService<AppStateService>().Config;
    return new MultiVaultKeyVaultSecretResolver(config.KeyVaults, ...);
});

// Retrieval
public class MistralAgentService
{
    private readonly IKeyVaultSecretResolver _keyVault;

    public async Task<string> GetApiKeyAsync()
    {
        return await _keyVault.GetSecretAsync("Mistral-ApiKey");
    }
}
```

**Pros**:

- Centralized management
- Automatic rotation support
- Fine-grained access control
- Audit logging

**Cons**:

- Azure dependency
- Slightly higher latency

#### Option 2: Windows DPAPI (Recommended for Desktop)

```csharp
// Using existing ICredentialStore pattern
public interface ICredentialStore
{
    Task<string?> GetPasswordAsync(string service, string account);
    Task SetPasswordAsync(string service, string account, string password);
    Task DeletePasswordAsync(string service, string account);
}

// Usage
public class MistralAgentService
{
    private readonly ICredentialStore _credentialStore;

    public async Task<string> GetApiKeyAsync()
    {
        return await _credentialStore.GetPasswordAsync(
            "SwebKit-Agent",
            "Mistral-ApiKey");
    }
}
```

**Pros**:

- Machine-specific encryption
- No external dependencies
- Integrates with existing SwebKit pattern

**Cons**:

- Machine-specific (not portable)
- Limited to Windows

#### Option 3: Encrypted Configuration (Fallback)

```csharp
// Only if Key Vault and DPAPI are not available
public class EncryptedConfigProvider
{
    private readonly string _encryptionKey; // From secure source

    public string Decrypt(string encryptedValue)
    {
        // Use AES or similar
        return DecryptAes(encryptedValue, _encryptionKey);
    }
}
```

**Pros**:

- Works in all environments
- Portable across machines

**Cons**:

- Key management responsibility
- Less secure than dedicated solutions

### Usage

**API Key Masking**:

```csharp
public string MaskApiKey(string apiKey)
{
    if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 4)
        return "****";

    return apiKey[..2] + new string('*', apiKey.Length - 4) + apiKey[^2..];
}
// Example: "sk-1234567890abcdef" -> "sk-*************ef"
```

**Logging Protection**:

```csharp
// Custom logger filter
public class ApiKeyRedactionLogger : ILogger
{
    private readonly ILogger _innerLogger;

    public IDisposable BeginScope<TState>(TState state) => _innerLogger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter)
    {
        var message = formatter(state, exception);
        message = RedactApiKeys(message);
        _innerLogger.Log(logLevel, eventId, state, exception, (s, e) => message);
    }

    private string RedactApiKeys(string message)
    {
        // Redact any Mistral API keys
        return Regex.Replace(message, @"sk-[a-zA-Z0-9]{20,}", "sk-REDACTED");
    }
}
```

### Rotation

**Automated Rotation**:

- Key Vault: Use built-in rotation with Azure Function
- Manual: Document rotation procedure
- Frequency: Every 90 days or after potential compromise

**Rotation Procedure**:

1. Generate new API key in Mistral console
2. Update Key Vault / Credential Store with new key
3. Test with new key
4. Remove old key from Mistral console
5. Update audit logs

---

## 🛡️ Data Protection

### Sensitive Data Handling

**Never Send to Mistral**:

- API keys and credentials
- Personal Identifiable Information (PII)
- Financial data
- Health records
- Passwords and secrets
- Internal IP addresses and network details
- Sensitive business information

**Filtering Pipeline**:

```
User Query + Context
       │
       ▼
┌─────────────────────┐
│  PII Detection      │  ◄── Check for personal data
└─────────────────────┘
       │
       ▼
┌─────────────────────┐
│  Secret Detection   │  ◄── Check for API keys, passwords
└─────────────────────┘
       │
       ▼
┌─────────────────────┐
│  Internal Data      │  ◄── Check for internal addresses, etc.
│  Filtering          │
└─────────────────────┘
       │
       ▼
┌─────────────────────┐
│  Token Budget       │  ◄── Truncate to fit context window
│  Management         │
└─────────────────────┘
       │
       ▼
┌─────────────────────┐
│  Format for Mistral │  ◄── JSON structure
└─────────────────────┘
```

### Data Filtering Implementation

```csharp
public class DataFilterService
{
    private static readonly Regex[] PiiPatterns = [
        new Regex(@"\b\d{3}-\d{2}-\d{4}\b"), // SSN
        new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b"), // Email
        new Regex(@"\b\d{16}\b"), // Credit card (simplified)
    ];

    private static readonly Regex[] SecretPatterns = [
        new Regex(@"sk-[a-zA-Z0-9]{20,}"), // Mistral key
        new Regex(@"AKIA[0-9A-Z]{16}"), // AWS key
        new Regex(@"\bpassword\b.*:.*", RegexOptions.IgnoreCase),
    ];

    private static readonly Regex[] InternalPatterns = [
        new Regex(@"\b10\.\d{1,3}\.\d{1,3}\.\d{1,3}\b"), // Private IP
        new Regex(@"\b192\.168\.\d{1,3}\.\d{1,3}\b"), // Private IP
        new Regex(@"\b172\.(1[6-9]|2[0-9]|3[0-1])\.\d{1,3}\.\d{1,3}\b"), // Private IP
    ];

    public string FilterForAi(string input)
    {
        var filtered = input;

        // Filter PII
        filtered = PiiPatterns.Aggregate(filtered, (current, pattern) =>
            pattern.Replace(current, "[PII-REDACTED]"));

        // Filter secrets
        filtered = SecretPatterns.Aggregate(filtered, (current, pattern) =>
            pattern.Replace(current, "[SECRET-REDACTED]"));

        // Filter internal data
        filtered = InternalPatterns.Aggregate(filtered, (current, pattern) =>
            pattern.Replace(current, "[INTERNAL-REDACTED]"));

        return filtered;
    }
}
```

### Data Retention

**Conversation Data**:

- Stored locally in encrypted form
- Retention period: Configurable (default: 30 days)
- Auto-deletion after retention period
- Manual deletion option

**Audit Logs**:

- Retention period: Configurable (default: 90 days)
- Separate from conversation data
- Read-only after creation

**Tool Execution Data**:

- Results cached temporarily (default: 5 minutes)
- Not persisted long-term by default
- Can be configured to persist for debugging

---

## 🔐 Access Control

### Permission Model

**Principle**: The agent should start with **read-only access** and only perform write operations with explicit user approval.

#### Permission Levels

| Level         | Description                  | Capabilities             | Default    |
| ------------- | ---------------------------- | ------------------------ | ---------- |
| **None**      | No agent access              | Agent disabled           | New users  |
| **Read-Only** | Basic query capabilities     | Query tools only         | Default    |
| **Standard**  | Full query + limited actions | Query + safe write tools | Opt-in     |
| **Advanced**  | Full access + automation     | All tools + workflows    | Admin only |

#### Implementation

```csharp
public enum AgentPermissionLevel
{
    None,
    ReadOnly,
    Standard,
    Advanced
}

public class AgentAuthorizationService
{
    private readonly UserSettingsRepository _settings;
    private readonly IAgentToolRegistry _toolRegistry;

    public AgentPermissionLevel GetUserPermissionLevel(string userId)
    {
        return _settings.GetUserSettings(userId).AgentPermissionLevel;
    }

    public bool CanExecuteTool(string userId, string toolName)
    {
        var permissionLevel = GetUserPermissionLevel(userId);
        var tool = _toolRegistry.GetTool(toolName);

        if (tool == null) return false;

        return tool.RequiredPermission <= permissionLevel;
    }
}
```

### Tool Classification

```csharp
public enum ToolPermissionLevel
{
    ReadOnly,    // Always safe (queries only)
    SafeWrite,   // Safe write operations (with confirmation)
    Restricted,  // Requires explicit approval
    Admin        // Admin-only operations
}

public interface IAgentTool
{
    // ... existing properties ...

    /// <summary>Minimum permission level required to execute this tool</summary>
    ToolPermissionLevel RequiredPermission { get; }

    /// <summary>Whether this tool requires user confirmation before execution</summary>
    bool RequiresConfirmation { get; }
}
```

### Feature Flags

```csharp
public class AgentFeatureFlags
{
    // Core features
    public bool EnableAgent { get; set; } = false;

    // Phase-specific features
    public bool EnableBasicTools { get; set; } = false;
    public bool EnableAdvancedTools { get; set; } = false;
    public bool EnableAutomation { get; set; } = false;

    // Experimental features
    public bool EnableProactiveMonitoring { get; set; } = false;
    public bool EnableContextAwareness { get; set; } = true;

    // Security features
    public bool EnableAuditLogging { get; set; } = true;
    public bool EnableDataFiltering { get; set; } = true;
}
```

---

## 🔍 Audit Logging

### What to Log

**Always Log**:

- Agent API requests (with masked keys)
- Tool executions (name, parameters masked, result status)
- User queries (sanitized)
- AI responses (sanitized)
- Authentication and authorization attempts
- Configuration changes
- Errors and exceptions

**Conditionally Log** (configurable):

- Full conversation history
- Detailed tool execution results
- Performance metrics

**Never Log**:

- API keys or credentials
- Raw sensitive data
- Full PII

### Log Structure

```json
{
  "timestamp": "2026-06-29T12:34:56.789Z",
  "level": "Information",
  "source": "SwebKit.Agent",
  "userId": "user123",
  "sessionId": "sess_abc456",
  "eventType": "ToolExecuted",
  "data": {
    "toolName": "GetPodStatus",
    "parameters": { "namespace": "default", "podName": "my-pod-abc123" },
    "executionTimeMs": 245,
    "isSuccess": true,
    "error": null
  },
  "metadata": {
    "ipAddress": "192.168.1.100",
    "userAgent": "SwebKit/1.0",
    "correlationId": "corr-xyz789"
  }
}
```

### Implementation

```csharp
public class AgentAuditLogger
{
    private readonly ILogger<AgentAuditLogger> _logger;
    private readonly DataFilterService _filterService;

    public void LogToolExecution(ToolExecutionAudit audit)
    {
        var sanitizedAudit = Sanitize(audit);
        _logger.LogInformation(
            "Tool execution: {ToolName}, User: {UserId}, Status: {Status}",
            sanitizedAudit.ToolName,
            audit.UserId,
            audit.IsSuccess ? "Success" : "Failed");
    }

    public void LogAgentRequest(AgentRequestAudit audit)
    {
        var sanitizedAudit = Sanitize(audit);
        _logger.LogInformation(
            "Agent request: User: {UserId}, Tokens: {TokenCount}",
            audit.UserId,
            audit.TokenCount);
    }

    private TAudit Sanitize<TAudit>(TAudit audit) where TAudit : IAuditRecord
    {
        // Apply filtering to all string fields
        // Remove sensitive data
        // Mask PII
        return audit;
    }
}
```

---

## 🛡️ Safe Operation

### Read-Only by Default

**Principle**: All agent operations should be read-only by default. Write operations require explicit configuration and user approval.

**Implementation**:

```csharp
public class SafeOperationService
{
    private readonly AgentAuthorizationService _auth;

    public async Task<SafeOperationResult> ExecuteSafeAsync(
        string userId,
        IAgentTool tool,
        AgentToolRequest request)
    {
        // Check permission
        if (!_auth.CanExecuteTool(userId, tool.Name))
        {
            return SafeOperationResult.Denied("Insufficient permissions");
        }

        // Check if confirmation is required
        if (tool.RequiresConfirmation)
        {
            var confirmation = await RequestUserConfirmation(userId, tool, request);
            if (!confirmation.Confirmed)
            {
                return SafeOperationResult.Cancelled();
            }
        }

        // Execute with safety checks
        try
        {
            var result = await tool.Execute(request);
            return SafeOperationResult.Success(result);
        }
        catch (Exception ex)
        {
            LogError(userId, tool, request, ex);
            return SafeOperationResult.Error(ex);
        }
    }

    private async Task<UserConfirmation> RequestUserConfirmation(
        string userId, IAgentTool tool, AgentToolRequest request)
    {
        // Show confirmation dialog to user
        // Include: tool name, description, parameters, potential impact
        // Return user's decision
    }
}
```

### Sandboxing

**Principle**: Limit the scope of what the agent can access and modify.

**Implementation Options**:

1. **Resource Scoping**:
   - Limit tools to specific namespaces/clusters
   - Configurable per-user or per-team
   - Default: Current selection only

2. **Time Limits**:
   - Maximum execution time per tool
   - Maximum total session time
   - Automatic timeout

3. **Rate Limiting**:
   - Per-user rate limits
   - Per-tool rate limits
   - Global rate limits

4. **Result Limiting**:
   - Maximum number of results returned
   - Maximum data size returned
   - Automatic truncation

```csharp
public class OperationSandbox
{
    private readonly SandboxConfig _config;

    public SandboxConfig GetConfig(string userId)
    {
        // Get user-specific or default sandbox config
        return _config;
    }
}

public class SandboxConfig
{
    public IReadOnlyList<string> AllowedNamespaces { get; set; } = [];
    public IReadOnlyList<string> AllowedClusters { get; set; } = [];
    public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxResults { get; set; } = 100;
    public int MaxDataSizeKB { get; set; } = 1024;
    public int RequestsPerMinute { get; set; } = 60;
}
```

### User Confirmation

**When to Require Confirmation**:

| Action Type          | Confirmation Required | Example                       |
| -------------------- | --------------------- | ----------------------------- |
| Read-only query      | No                    | Get pod status                |
| Safe write           | Yes                   | Restart pod                   |
| Configuration change | Yes                   | Update deployment             |
| Resource deletion    | Yes + reason          | Delete pod                    |
| Bulk operation       | Yes                   | Restart all pods in namespace |

**Confirmation Dialog**:

```
┌─────────────────────────────────────────────────────────────┐
│  Confirm Action                                              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  The agent wants to:                                         │
│  ▶ Restart pod "my-app-abc123"                              │
│                                                             │
│  Potential impact:                                          │
│  • Brief service interruption                               │
│  • Pod will be rescheduled to another node                  │
│                                                             │
│  [Confirm]  [Cancel]                                         │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔗 Related Documents

### Phase Documents

- [Phase 0: Proof of Concept](../phase-0-poc.md) - Security validation requirements
- [Phase 1: Foundation](../phase-1-foundation.md) - Implementation security
- [Phase 2: Intelligence](../phase-2-intelligence.md) - Context filtering security
- [Phase 3: Automation](../phase-3-automation.md) - Safe automation security

### Supporting Documents

- [Architecture](architecture.md) - Components that need security
- [Testing Strategy](testing-strategy.md) - Security testing approach
- [Performance Optimization](performance-optimization.md)
- [Metrics and Monitoring](metrics-and-monitoring.md) - Security monitoring
- [README - Overview](../README.md)
