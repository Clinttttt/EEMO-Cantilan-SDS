using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace EEMOCantilanSDS.Client.Securities;

public class AuthService(
    IJSRuntime js,
    NavigationManager navigation,
    AuthStateProvider authStateProvider,
    TokenService tokenService,
    ILogger<AuthService> logger)
{
    /// <summary>
    /// Outcome of the password step. <paramref name="MfaRequired"/> means the password was correct but an
    /// authenticator code is still needed — no session exists yet, and <paramref name="ChallengeToken"/> must
    /// be presented with the code.
    /// </summary>
    public record LoginOutcome(bool Success, bool MfaRequired, string? ChallengeToken = null, string? Error = null);

    public async Task<LoginOutcome> LoginAsync(string username, string password, string? municipalityCode = null)
    {
        try
        {
            // When a municipality is specified (scoped login URL ?lgu={code}) it is sent so the API can
            // enforce that the account belongs to that LGU. When absent the payload is unchanged.
            object loginData = string.IsNullOrWhiteSpace(municipalityCode)
                ? new { username, password }
                : new { username, password, municipalityCode };
            var json = JsonSerializer.Serialize(loginData);

            // loginWithMfa always returns a JSON envelope: ok / mfaRequired+challengeToken / error.
            var raw = await js.InvokeAsync<string?>("loginWithMfa", "/api/authproxy/login", json);
            var outcome = ParseOutcome(raw);

            if (outcome.MfaRequired)
            {
                logger.LogInformation("Login requires two-factor for user: {Username}", username);
                return outcome;                        // caller shows the code step; no navigation yet
            }

            if (!outcome.Success)
            {
                logger.LogWarning("Login failed for user: {Username}", username);
                return outcome;
            }

            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/menu", forceLoad: true);
            return outcome;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during login for user: {Username}", username);
            return new LoginOutcome(false, false, Error: "Unable to sign in. Please try again.");
        }
    }

    /// <summary>
    /// Completes a two-factor sign-in with the challenge from the password step plus the authenticator code
    /// (or a recovery code). On success the auth cookie is set and the app navigates in.
    /// </summary>
    public async Task<LoginOutcome> VerifyMfaAsync(string challengeToken, string code)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { challengeToken, code });
            var raw = await js.InvokeAsync<string?>("loginWithMfa", "/api/authproxy/mfa/verify-login", json);
            var outcome = ParseOutcome(raw);

            if (!outcome.Success)
                return outcome;

            await authStateProvider.MarkUserAsAuthenticated();
            navigation.NavigateTo("/menu", forceLoad: true);
            return outcome;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during two-factor verification");
            return new LoginOutcome(false, false, Error: "Unable to verify the code. Please try again.");
        }
    }

    /// <summary>
    /// The signed-in administrator replaces their own password. The proxy rebuilds the cookie session from the new tokens,
    /// so the requirement to change — which travels as a claim inside that cookie — is actually lifted.
    /// </summary>
    /// <remarks>
    /// Reuses the same <c>loginWithMfa</c> JS envelope as sign-in: it posts JSON, includes credentials, and always resolves
    /// with ok / error. A second helper doing the same thing would be one more place for the cookie handling to drift.
    /// </remarks>
    public async Task<LoginOutcome> ChangeMyPasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { currentPassword, newPassword });
            var raw = await js.InvokeAsync<string?>("loginWithMfa", "/api/authproxy/change-my-password", json);
            var outcome = ParseOutcome(raw);

            if (!outcome.Success)
            {
                logger.LogWarning("Password change refused for the signed-in administrator");
                return outcome;
            }

            await authStateProvider.MarkUserAsAuthenticated();
            return outcome;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while changing the signed-in administrator's password");
            return new LoginOutcome(false, false, Error: "Unable to change your password. Please try again.");
        }
    }

    private static LoginOutcome ParseOutcome(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new LoginOutcome(false, false, Error: "Authentication failed.");

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("mfaRequired", out var mfa) && mfa.ValueKind == JsonValueKind.True)
            {
                var challenge = root.TryGetProperty("challengeToken", out var t) ? t.GetString() : null;
                return new LoginOutcome(false, true, challenge);
            }
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return new LoginOutcome(true, false);
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                return new LoginOutcome(false, false, Error: err.GetString());
        }
        catch { /* fall through to the generic failure */ }

        return new LoginOutcome(false, false, Error: "Authentication failed.");
    }

    public async Task LogoutAsync()
    {
        try
        {
            await js.InvokeVoidAsync("fetch", "/api/authproxy/logout", new { method = "POST" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during logout");
        }
        finally
        {
            // Always complete the local logout even if the server call failed: drop the in-memory
            // tokens and force a full reload so the circuit (and its TokenService) is torn down.
            tokenService.Clear();
            await authStateProvider.MarkUserAsLoggedOut();
            navigation.NavigateTo("/login", forceLoad: true);
        }
    }
}
