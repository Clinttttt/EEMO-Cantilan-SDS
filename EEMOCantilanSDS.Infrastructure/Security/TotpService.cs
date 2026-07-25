using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Application.Common.Interface.Services;

namespace EEMOCantilanSDS.Infrastructure.Security
{
    /// <summary>
    /// RFC 6238 TOTP over RFC 4226 HMAC-OTP, using the BCL's HMACSHA1 — deliberately dependency-free, and
    /// covered by the RFC's published test vectors so the implementation is provably standard-conformant.
    /// <para>
    /// HMAC-SHA1 is the algorithm authenticator apps implement by default; its use here is the OTP
    /// construction (a keyed MAC over a counter), not a collision-sensitive signature, so SHA-1 is correct
    /// and not a weakness in this context.
    /// </para>
    /// </summary>
    public sealed class TotpService : ITotpService
    {
        private const int StepSeconds = 30;
        private const int Digits = 6;

        /// <summary>How many steps of clock drift to accept either side of "now" (±1 step = ±30s).</summary>
        private const int DriftSteps = 1;

        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public string GenerateSecret()
        {
            // 20 bytes = 160 bits, the size RFC 4226/6238 recommend for HMAC-SHA1.
            var bytes = RandomNumberGenerator.GetBytes(20);
            return ToBase32(bytes);
        }

        public string BuildProvisioningUri(string secretBase32, string issuer, string account)
        {
            // otpauth://totp/{issuer}:{account}?secret=...&issuer=...&algorithm=SHA1&digits=6&period=30
            // The issuer is repeated in the label and the query, which is what apps expect for correct grouping.
            var label = Uri.EscapeDataString($"{issuer}:{account}");
            var query =
                $"secret={secretBase32}" +
                $"&issuer={Uri.EscapeDataString(issuer)}" +
                $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
            return $"otpauth://totp/{label}?{query}";
        }

        public long CurrentStep() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;

        public bool TryValidate(string secretBase32, string code, long? minimumStep, out long matchedStep)
        {
            matchedStep = 0;

            if (string.IsNullOrWhiteSpace(secretBase32) || string.IsNullOrWhiteSpace(code))
                return false;

            var entered = code.Trim().Replace(" ", string.Empty);
            if (entered.Length != Digits || !entered.All(char.IsDigit))
                return false;

            byte[] key;
            try { key = FromBase32(secretBase32); }
            catch (FormatException) { return false; }
            if (key.Length == 0) return false;

            var current = CurrentStep();
            for (var offset = -DriftSteps; offset <= DriftSteps; offset++)
            {
                var step = current + offset;

                // Replay guard: a code from an already-consumed step (or earlier) is refused even though it
                // is still inside its validity window.
                if (minimumStep is { } floor && step <= floor)
                    continue;

                var expected = ComputeCode(key, step);
                // Fixed-time comparison so a wrong code cannot be narrowed down by timing.
                if (CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(entered)))
                {
                    matchedStep = step;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// RFC 4226 HOTP truncation over the step counter. Public so the RFC 6238 published test vectors can
        /// verify conformance directly — it is the standard algorithm, and knowing it reveals nothing: the
        /// secret key is the sensitive input.
        /// </summary>
        public static string ComputeCode(byte[] key, long step)
        {
            var counter = BitConverter.GetBytes(step);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counter);                     // RFC 4226 uses a big-endian 8-byte counter

            using var hmac = new HMACSHA1(key);
            var hash = hmac.ComputeHash(counter);

            // Dynamic truncation: low 4 bits of the last byte select the 4-byte window.
            var offset = hash[^1] & 0x0F;
            var binary =
                ((hash[offset] & 0x7F) << 24) |
                ((hash[offset + 1] & 0xFF) << 16) |
                ((hash[offset + 2] & 0xFF) << 8) |
                (hash[offset + 3] & 0xFF);

            var otp = binary % (int)Math.Pow(10, Digits);
            return otp.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
        }

        /// <summary>RFC 4648 base32 encode (no padding) — the encoding authenticator apps expect for secrets.</summary>
        public static string ToBase32(byte[] data)
        {
            var sb = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bitsLeft = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;
                while (bitsLeft >= 5)
                {
                    sb.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                    bitsLeft -= 5;
                }
            }
            if (bitsLeft > 0)
                sb.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
            return sb.ToString();
        }

        /// <summary>RFC 4648 base32 decode; throws <see cref="FormatException"/> on an invalid character.</summary>
        public static byte[] FromBase32(string value)
        {
            var cleaned = value.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
            int buffer = 0, bitsLeft = 0;
            var output = new List<byte>(cleaned.Length * 5 / 8);

            foreach (var c in cleaned)
            {
                var index = Base32Alphabet.IndexOf(c);
                if (index < 0) throw new FormatException($"Invalid base32 character '{c}'.");

                buffer = (buffer << 5) | index;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    output.Add((byte)(buffer >> (bitsLeft - 8)));
                    bitsLeft -= 8;
                }
            }

            return output.ToArray();
        }
    }
}
