namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Builds the one-time self-service password-reset link emailed when a user requests a reset. The base
    /// points at the admin console's reset route. It is environment-driven: set the
    /// <c>PASSWORD_RESET_LINK_BASE</c> environment variable / app setting to override per deployment; when
    /// unset it falls back to the known production console domain. Mirrors <see cref="ActivationLinks"/>.
    /// </summary>
    public static class PasswordResetLinks
    {
        private const string DefaultBase = "https://console.stalltrack.site/reset-password";

        public static string Base =>
            Environment.GetEnvironmentVariable("PASSWORD_RESET_LINK_BASE") is { Length: > 0 } configured
                ? configured.TrimEnd('/')
                : DefaultBase;

        /// <summary>
        /// Builds the reset URL. The LGU code is carried as a query string so the reset page can render the
        /// correct municipality's branding (and so a scoped console deployment stays on its own tenant).
        /// </summary>
        public static string Build(string token, string? municipalityCode = null)
            => string.IsNullOrWhiteSpace(municipalityCode)
                ? $"{Base}/{token}"
                : $"{Base}/{token}?lgu={Uri.EscapeDataString(municipalityCode)}";
    }
}
