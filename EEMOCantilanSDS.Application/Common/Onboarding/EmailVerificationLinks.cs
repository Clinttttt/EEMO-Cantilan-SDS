namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Builds the one-time email-verification link. Environment-driven: set
    /// <c>EMAIL_VERIFY_LINK_BASE</c> to override per deployment; when unset it falls back to the known
    /// production console domain. Mirrors <see cref="ActivationLinks"/> / <see cref="PasswordResetLinks"/>.
    /// </summary>
    public static class EmailVerificationLinks
    {
        private const string DefaultBase = "https://console.stalltrack.site/verify-email";

        public static string Base =>
            Environment.GetEnvironmentVariable("EMAIL_VERIFY_LINK_BASE") is { Length: > 0 } configured
                ? configured.TrimEnd('/')
                : DefaultBase;

        /// <summary>
        /// Builds the verification URL. The LGU code rides along as a query string so the page can render
        /// the correct municipality's branding.
        /// </summary>
        public static string Build(string token, string? municipalityCode = null)
            => string.IsNullOrWhiteSpace(municipalityCode)
                ? $"{Base}/{token}"
                : $"{Base}/{token}?lgu={Uri.EscapeDataString(municipalityCode)}";
    }
}
