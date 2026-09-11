import type { AuthType, AuthConfig } from "@/lib/types";

const authTypes: { value: AuthType; label: string }[] = [
  { value: "None", label: "None" },
  { value: "Inherited", label: "Inherited" },
  { value: "BearerToken", label: "Bearer Token" },
  { value: "Basic", label: "Basic" },
  { value: "ApiKey", label: "API Key" },
  { value: "OAuth2", label: "OAuth 2.0" },
];

interface AuthPanelProps {
  auth: AuthConfig;
  /**
   * The secret value, held by `RequestEditor` because it is persisted to the secret
   * store on save and blur, not only when this panel is mounted.
   */
  secretInput: string;
  onAuthTypeChange: (type: AuthType) => void;
  onAuthPatch: (patch: Partial<AuthConfig>) => void;
  onSecretChange: (value: string) => void;
  onSecretBlur: () => void;
}

export function AuthPanel({
  auth,
  secretInput,
  onAuthTypeChange,
  onAuthPatch,
  onSecretChange,
  onSecretBlur,
}: AuthPanelProps) {
  return (
    <div data-testid="auth-tab">
      <select
        data-testid="auth-type-select"
        value={auth.type}
        onChange={(e) => onAuthTypeChange(e.target.value as AuthType)}
        className="mb-2 rounded border bg-background px-2 py-1 text-sm"
      >
        {authTypes.map((t) => (
          <option key={t.value} value={t.value}>{t.label}</option>
        ))}
      </select>

      {auth.type === "BearerToken" && (
        <input
          data-testid="auth-bearer-input"
          type="password"
          value={secretInput}
          onChange={(e) => onSecretChange(e.target.value)}
          onBlur={onSecretBlur}
          placeholder="Bearer token"
          className="w-full rounded border bg-background px-2 py-1 text-sm"
        />
      )}

      {auth.type === "Basic" && (
        <div className="flex gap-2">
          <input
            data-testid="auth-basic-username"
            type="text"
            value={auth.basicUsername ?? ""}
            onChange={(e) => onAuthPatch({ basicUsername: e.target.value })}
            placeholder="Username"
            className="flex-1 rounded border bg-background px-2 py-1 text-sm"
          />
          <input
            data-testid="auth-basic-password"
            type="password"
            value={secretInput}
            onChange={(e) => onSecretChange(e.target.value)}
            onBlur={onSecretBlur}
            placeholder="Password"
            className="flex-1 rounded border bg-background px-2 py-1 text-sm"
          />
        </div>
      )}

      {auth.type === "ApiKey" && (
        <div className="flex gap-2">
          <input
            data-testid="auth-apikey-name"
            type="text"
            value={auth.apiKeyParamName ?? ""}
            onChange={(e) => onAuthPatch({ apiKeyParamName: e.target.value })}
            placeholder="Key name"
            className="w-32 rounded border bg-background px-2 py-1 text-sm"
          />
          <select
            data-testid="auth-apikey-location"
            value={auth.apiKeyLocation}
            onChange={(e) => onAuthPatch({ apiKeyLocation: e.target.value as "Header" | "QueryParam" })}
            className="rounded border bg-background px-2 py-1 text-sm"
          >
            <option value="Header">Header</option>
            <option value="QueryParam">Query</option>
          </select>
          <input
            data-testid="auth-apikey-value"
            type="password"
            value={secretInput}
            onChange={(e) => onSecretChange(e.target.value)}
            onBlur={onSecretBlur}
            placeholder="API key value"
            className="flex-1 rounded border bg-background px-2 py-1 text-sm"
          />
        </div>
      )}

      {auth.type === "OAuth2" && (
        <div className="space-y-2">
          <div>
            <label className="mb-1 block text-xs font-medium text-muted-foreground">Grant Type</label>
            <select
              data-testid="auth-oauth2-grant"
              value={auth.oAuth2GrantType}
              onChange={(e) => onAuthPatch({ oAuth2GrantType: e.target.value as "ClientCredentials" | "AuthorizationCode" })}
              className="w-full rounded border bg-background px-2 py-1 text-sm"
            >
              <option value="ClientCredentials">Client Credentials</option>
              <option value="AuthorizationCode">Authorization Code</option>
            </select>
          </div>
          <input
            data-testid="auth-oauth2-client-id"
            type="text"
            value={auth.oAuth2ClientId ?? ""}
            onChange={(e) => onAuthPatch({ oAuth2ClientId: e.target.value })}
            placeholder="Client ID"
            className="w-full rounded border bg-background px-2 py-1 text-sm"
          />
          <input
            data-testid="auth-oauth2-token-url"
            type="text"
            value={auth.oAuth2TokenUrl ?? ""}
            onChange={(e) => onAuthPatch({ oAuth2TokenUrl: e.target.value })}
            placeholder="Token URL"
            className="w-full rounded border bg-background px-2 py-1 text-sm"
          />
          {auth.oAuth2GrantType === "AuthorizationCode" && (
            <input
              data-testid="auth-oauth2-auth-url"
              type="text"
              value={auth.oAuth2AuthUrl ?? ""}
              onChange={(e) => onAuthPatch({ oAuth2AuthUrl: e.target.value })}
              placeholder="Authorization URL"
              className="w-full rounded border bg-background px-2 py-1 text-sm"
            />
          )}
          <input
            data-testid="auth-oauth2-scopes"
            type="text"
            value={auth.oAuth2Scopes ?? ""}
            onChange={(e) => onAuthPatch({ oAuth2Scopes: e.target.value })}
            placeholder="Scopes (space-separated)"
            className="w-full rounded border bg-background px-2 py-1 text-sm"
          />
          <input
            data-testid="auth-oauth2-secret"
            type="password"
            value={secretInput}
            onChange={(e) => onSecretChange(e.target.value)}
            onBlur={onSecretBlur}
            placeholder="Client Secret"
            className="w-full rounded border bg-background px-2 py-1 text-sm"
          />
        </div>
      )}

      {auth.type === "Inherited" && (
        <div className="text-xs text-muted-foreground">
          This request inherits authentication from its parent collection.
        </div>
      )}
    </div>
  );
}
