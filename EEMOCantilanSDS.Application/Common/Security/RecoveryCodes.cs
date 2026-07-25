using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EEMOCantilanSDS.Application.Common.Security
{
    /// <summary>
    /// Generates and hashes two-factor recovery codes — the fallback when an authenticator device is lost.
    /// <para>
    /// Codes are cryptographically random, shown to the user exactly once, and stored only as SHA-256
    /// hashes. A plain hash (no salt/KDF) is correct here, unlike for passwords: these are high-entropy
    /// random values, not user-chosen secrets, so they are not guessable or rainbow-table-able.
    /// </para>
    /// </summary>
    public static class RecoveryCodes
    {
        /// <summary>How many codes are issued per set.</summary>
        public const int SetSize = 8;

        // Crockford-style alphabet: no I, L, O, U, 0 or 1, so codes can be read off paper without ambiguity.
        private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
        private const int GroupLength = 5;

        /// <summary>
        /// Creates a fresh set. Returns the plaintext codes (to display once) alongside their hashes
        /// (to persist), in matching order.
        /// </summary>
        public static (IReadOnlyList<string> Plain, IReadOnlyList<string> Hashes) Generate(int count = SetSize)
        {
            var plain = new List<string>(count);
            for (var i = 0; i < count; i++)
                plain.Add($"{RandomGroup()}-{RandomGroup()}");

            return (plain, plain.Select(Hash).ToList());
        }

        /// <summary>Hashes a user-entered code for comparison against the stored set.</summary>
        public static string Hash(string code)
        {
            // Normalised so formatting differences (case, spaces, missing dash) don't reject a valid code.
            var normalised = Normalise(code);
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
        }

        /// <summary>Strips separators/whitespace and upper-cases, so "abcde fghij" == "ABCDE-FGHIJ".</summary>
        public static string Normalise(string code) =>
            new string((code ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());

        private static string RandomGroup()
        {
            var chars = new char[GroupLength];
            for (var i = 0; i < GroupLength; i++)
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            return new string(chars);
        }
    }
}
