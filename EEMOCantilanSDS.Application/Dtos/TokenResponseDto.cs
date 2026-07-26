using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Dtos
{
    public class TokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// True when the password was correct but two-factor is still outstanding. In that case NO tokens are
        /// issued (both token properties stay empty) and <see cref="MfaChallengeToken"/> carries the
        /// short-lived challenge to submit with the authenticator code.
        /// <para>
        /// Additive and optional, so existing clients (including the collector app, which is not
        /// MFA-gated) keep working unchanged: they simply never see it set.
        /// </para>
        /// </summary>
        public bool MfaRequired { get; set; }

        /// <summary>Short-lived, single-use challenge proving the password step just succeeded.</summary>
        public string? MfaChallengeToken { get; set; }
    }
}
