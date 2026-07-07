using System;
using System.Security.Cryptography;

namespace EEMOCantilanSDS.Application.Common.Security
{
    /// <summary>Generates cryptographically-random, URL-safe capability tokens.</summary>
    public static class SecureToken
    {
        public static string NewUrlToken(int bytes = 32)
        {
            var buffer = new byte[bytes];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToBase64String(buffer).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
