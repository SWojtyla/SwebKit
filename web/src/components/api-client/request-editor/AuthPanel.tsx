import { useEffect, useRef, useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import type { AuthType, AuthConfig } from "@/lib/types";
import { VariableInput } from "../VariableInput";
import {
  startOAuth2Authorize,
  getOAuth2Result,
} from "@/lib/api";
import { openExternal } from "@/lib/tauri-bridge";

const authTypes: { value: AuthType; label: string }[] = [
  { value: "None", label: "None" },
  { value: "Inherited", label: "Inherited" },
  { value: "BearerToken", label: "Bearer Token" },
  { value: "Basic", label: "Basic" },
  { value: "ApiKey", label: "API Key" },
  { value: "OAuth2", label: "OAuth 2.0" },
];

interface SecretFieldProps {
  value: string;
  revealed: boolean;
  onToggleReveal: () => void;
  onChange: (value: string) => void;
  onBlur: () => void;
  scope: Record<string, string | null>;
  placeholder: string;
  testId: string;
  className?: string;
}

/**
 * A secret input with a reveal toggle. Masked by default, but never write-only: a token set up
 * weeks ago could not be checked, corrected or even compared against the environment without
 * retyping it blind, which is also how a `{{VARIABLE}}` in an auth field stayed invisible.
 *
 * Revealed, it becomes a {@link VariableInput} rather than a plain text box, so a token written as
 * `{{AUTH_API_KEY}}` is coloured by whether it will actually resolve — the same three-state
 * treatment the URL bar and the body editor give their variables.
 */
function SecretField({
  value,
  revealed,
  onToggleReveal,
  onChange,
  onBlur,
  scope,
  placeholder,
  testId,
  className = "flex-1",
}: SecretFieldProps) {
  return (
    <div className={`flex min-w-0 items-center gap-1 ${className}`}>
      {revealed ? (
        <VariableInput
          testId={testId}
          ariaLabel={placeholder}
          value={value}
          onChange={onChange}
          onBlur={onBlur}
          scope={scope}
          placeholder={placeholder}
          metricsClassName="px-2 py-1 text-sm"
        />
      ) : (
        <input
          data-testid={testId}
          type="password"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onBlur={onBlur}
          placeholder={placeholder}
          aria-label={placeholder}
          className="min-w-0 flex-1 rounded border bg-background px-2 py-1 text-sm"
        />
      )}
      <button
        type="button"
        data-testid={`${testId}-reveal`}
        onClick={onToggleReveal}
        aria-pressed={revealed}
        aria-label={revealed ? "Hide value" : "Show value"}
        title={revealed ? "Hide value" : "Show value"}
        className="shrink-0 rounded border p-1.5 text-muted-foreground hover:bg-accent hover:text-foreground"
      >
        {revealed ? <EyeOff className="h-3.5 w-3.5" /> : <Eye className="h-3.5 w-3.5" />}
      </button>
    </div>
  );
}

interface AuthPanelProps {
  auth: AuthConfig;
  /**
   * The secret value, held by `RequestEditor` because it is persisted to the secret
   * store on save and blur, not only when this panel is mounted.
   */
  secretInput: string;
  /** Merged collection + environment variables, for highlighting a revealed `{{token}}`. */
  variableScope?: Record<string, string | null>;
  onAuthTypeChange: (type: AuthType) => void;
  onAuthPatch: (patch: Partial<AuthConfig>) => void;
  onSecretChange: (value: string) => void;
  onSecretBlur: () => void;
}

export function AuthPanel({
  auth,
  secretInput,
  variableScope = {},
  onAuthTypeChange,
  onAuthPatch,
  onSecretChange,
  onSecretBlur,
}: AuthPanelProps) {
  // Deliberately local and unpersisted: a revealed secret should not survive switching request,
  // and only one secret field is visible at a time, so a single flag covers every auth type.
  const [revealed, setRevealed] = useState(false);
  const toggleReveal = () => setRevealed((r) => !r);

  // ── Authorization-code + PKCE sign-in ──────────────────────────────────────
  // The flow is browser-mediated and async: "Sign in" hands the user to their provider, the
  // sidecar's loopback callback does the code exchange, and this polls for the outcome. The
  // attempt counter cancels an in-flight poll when the panel unmounts or a new sign-in starts.
  const [signInWaiting, setSignInWaiting] = useState(false);
  const [signInError, setSignInError] = useState<string | null>(null);
  const signInAttempt = useRef(0);
  useEffect(() => () => { signInAttempt.current++; }, []);

  const signedIn = !!auth.oAuth2TokenCredentialKey;

  const startSignIn = async () => {
    const attempt = ++signInAttempt.current;
    setSignInWaiting(true);
    setSignInError(null);
    try {
      const { transactionId, authorizeUrl } = await startOAuth2Authorize({
        authUrl: auth.oAuth2AuthUrl ?? "",
        tokenUrl: auth.oAuth2TokenUrl ?? "",
        clientId: auth.oAuth2ClientId ?? "",
        credentialKey: auth.credentialKey,
        scopes: auth.oAuth2Scopes,
      });
      await openExternal(authorizeUrl);

      const deadline = Date.now() + 10 * 60 * 1000;
      while (Date.now() < deadline) {
        await new Promise((r) => setTimeout(r, 1500));
        if (attempt !== signInAttempt.current) return;
        const result = await getOAuth2Result(transactionId);
        if (result.status === "done") {
          onAuthPatch({ oAuth2TokenCredentialKey: result.credentialKey });
          setSignInWaiting(false);
          return;
        }
        if (result.status === "error" || result.status === "expired") {
          setSignInWaiting(false);
          setSignInError(
            result.error ??
              (result.status === "expired"
                ? "The sign-in window expired — try again."
                : "Sign-in failed."),
          );
          return;
        }
      }
      setSignInWaiting(false);
      setSignInError("Timed out waiting for the browser sign-in to finish.");
    } catch (err) {
      if (attempt !== signInAttempt.current) return;
      setSignInWaiting(false);
      setSignInError(String(err));
    }
  };

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
        <SecretField
          testId="auth-bearer-input"
          value={secretInput}
          revealed={revealed}
          onToggleReveal={toggleReveal}
          onChange={onSecretChange}
          onBlur={onSecretBlur}
          scope={variableScope}
          placeholder="Bearer token"
          className="w-full"
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
          <SecretField
            testId="auth-basic-password"
            value={secretInput}
            revealed={revealed}
            onToggleReveal={toggleReveal}
            onChange={onSecretChange}
            onBlur={onSecretBlur}
            scope={variableScope}
            placeholder="Password"
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
          <SecretField
            testId="auth-apikey-value"
            value={secretInput}
            revealed={revealed}
            onToggleReveal={toggleReveal}
            onChange={onSecretChange}
            onBlur={onSecretBlur}
            scope={variableScope}
            placeholder="API key value"
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
            <>
              <input
                data-testid="auth-oauth2-auth-url"
                type="text"
                value={auth.oAuth2AuthUrl ?? ""}
                onChange={(e) => onAuthPatch({ oAuth2AuthUrl: e.target.value })}
                placeholder="Authorization URL"
                className="w-full rounded border bg-background px-2 py-1 text-sm"
              />
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  data-testid="auth-oauth2-signin"
                  onClick={() => void startSignIn()}
                  disabled={
                    signInWaiting ||
                    !auth.oAuth2AuthUrl ||
                    !auth.oAuth2TokenUrl ||
                    !auth.oAuth2ClientId
                  }
                  title={
                    !auth.oAuth2AuthUrl || !auth.oAuth2TokenUrl || !auth.oAuth2ClientId
                      ? "Fill in the authorization URL, token URL and client ID first"
                      : "Sign in via your browser — the token lands in the OS credential store"
                  }
                  className="rounded border px-2 py-1 text-xs hover:bg-accent disabled:opacity-50"
                >
                  {signInWaiting
                    ? "Waiting for sign-in…"
                    : signedIn
                      ? "Re-authorize"
                      : "Sign in"}
                </button>
                {signedIn && !signInWaiting && (
                  <span
                    className="text-xs text-success"
                    data-testid="auth-oauth2-signed-in"
                  >
                    Signed in — token in credential store
                  </span>
                )}
                {signInError && (
                  <span
                    className="text-xs text-destructive"
                    data-testid="auth-oauth2-signin-error"
                  >
                    {signInError}
                  </span>
                )}
              </div>
            </>
          )}
          <input
            data-testid="auth-oauth2-scopes"
            type="text"
            value={auth.oAuth2Scopes ?? ""}
            onChange={(e) => onAuthPatch({ oAuth2Scopes: e.target.value })}
            placeholder="Scopes (space-separated)"
            className="w-full rounded border bg-background px-2 py-1 text-sm"
          />
          <SecretField
            testId="auth-oauth2-secret"
            value={secretInput}
            revealed={revealed}
            onToggleReveal={toggleReveal}
            onChange={onSecretChange}
            onBlur={onSecretBlur}
            scope={variableScope}
            placeholder="Client Secret"
            className="w-full"
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
