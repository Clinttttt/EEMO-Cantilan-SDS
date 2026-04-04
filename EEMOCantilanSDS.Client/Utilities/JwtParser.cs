using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Client.Utilities
{
    public class JwtParser
    {
        public static ClaimsPrincipal? ParseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            token = token.Trim().Trim('"');
            var handler = new JwtSecurityTokenHandler();

            if (handler.CanReadToken(token))
            {
                try
                {
                    var jwt = handler.ReadJwtToken(token);
                    if (jwt.ValidTo < DateTime.UtcNow) return null;

                    return CreateClaimsPrincipal(jwt);
                }
                catch { }
            }


            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return null;

                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                var doc = JsonDocument.Parse(payloadJson);
                var claims = new List<Claim>();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is string value)
                    {
                        claims.Add(prop.Name switch
                        {
                            "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" => new Claim(ClaimTypes.Role, value),
                            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name" => new Claim(ClaimTypes.Name, value),
                            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" => new Claim(ClaimTypes.NameIdentifier, value),
                            _ => new Claim(prop.Name, value)
                        });
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Number && prop.Name == "exp")
                    {
                        var exp = prop.Value.GetInt64();
                        if (DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime < DateTime.UtcNow)
                            return null;
                    }
                }

                return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
            }
            catch
            {
                return null;
            }
        }

        private static ClaimsPrincipal CreateClaimsPrincipal(JwtSecurityToken jwt)
        {
            var claims = jwt.Claims.Select(c => c.Type switch
            {
                "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" => new Claim(ClaimTypes.Role, c.Value),
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name" => new Claim(ClaimTypes.Name, c.Value),
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier" => new Claim(ClaimTypes.NameIdentifier, c.Value),
                _ => c
            }).ToList();

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var output = input.Replace('-', '+').Replace('_', '/');
            output += new string('=', (4 - output.Length % 4) % 4);
            return Convert.FromBase64String(output);
        }


    }
}
