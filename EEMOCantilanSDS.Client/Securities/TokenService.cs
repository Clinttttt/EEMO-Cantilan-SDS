namespace EEMOCantilanSDS.Client.Securities;

public class TokenService
{
    private string? _accessToken;

    public void SetToken(string accessToken) => _accessToken = accessToken;
    public string? GetToken() => _accessToken;
}
