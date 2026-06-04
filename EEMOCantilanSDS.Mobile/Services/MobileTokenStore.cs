using global::EEMOCantilanSDS.Application.Dtos;

namespace EEMOCantilanSDS.Mobile.Services;

public sealed class MobileTokenStore
{
    private const string AccessTokenKey = "collector_access_token";
    private const string RefreshTokenKey = "collector_refresh_token";

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);
    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);

    public async Task InitializeAsync()
    {
        AccessToken ??= await SecureStorage.Default.GetAsync(AccessTokenKey);
        RefreshToken ??= await SecureStorage.Default.GetAsync(RefreshTokenKey);
    }

    public async Task SaveAsync(TokenResponseDto tokens)
    {
        AccessToken = tokens.AccessToken;
        RefreshToken = tokens.RefreshToken;

        await SecureStorage.Default.SetAsync(AccessTokenKey, tokens.AccessToken);
        await SecureStorage.Default.SetAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;

        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
    }
}
