namespace EEMOCantilanSDS.Client.Securities;

public class TokenService
{
    private string? _accessToken;
    private string? _refreshToken;

    public void SetToken(string accessToken) => _accessToken = accessToken;
    public string? GetToken() => _accessToken;

    public void SetRefreshToken(string refreshToken) => _refreshToken = refreshToken;
    public string? GetRefreshToken() => _refreshToken;

    public void Clear()
    {
        _accessToken = null;
        _refreshToken = null;
    }
}
