namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Builds the one-time self-service password-reset link emailed when a user requests a reset.
    ///
    /// <para>
    /// The base points at the portal the account actually signs in to. A municipality's Head or clerk resets on the
    /// admin console; the PLATFORM OPERATOR has no municipality and signs in to the platform's own console, so sending
    /// them to an LGU's address would land them on a sign-in screen their account is refused by. Both are
    /// environment-driven — <c>PASSWORD_RESET_LINK_BASE</c> and <c>OPERATOR_PASSWORD_RESET_LINK_BASE</c> — falling back
    /// to the known production domains. Mirrors <see cref="ActivationLinks"/>.
    /// </para>
    /// </summary>
    public static class PasswordResetLinks
    {
        private const string DefaultBase = "https://console.stalltrack.site/reset-password";
        private const string DefaultOperatorBase = "https://admin.stalltrack.site/reset-password";

        public static string Base =>
            Environment.GetEnvironmentVariable("PASSWORD_RESET_LINK_BASE") is { Length: > 0 } configured
                ? configured.TrimEnd('/')
                : DefaultBase;

        /// <summary>Where the platform's own operator resets, which is not any municipality's console.</summary>
        public static string OperatorBase =>
            Environment.GetEnvironmentVariable("OPERATOR_PASSWORD_RESET_LINK_BASE") is { Length: > 0 } configured
                ? configured.TrimEnd('/')
                : DefaultOperatorBase;

        /// <summary>
        /// Builds the reset URL. The LGU code is carried as a query string so the reset page can render the
        /// correct municipality's branding (and so a scoped console deployment stays on its own tenant).
        /// </summary>
        public static string Build(string token, string? municipalityCode = null)
            => string.IsNullOrWhiteSpace(municipalityCode)
                ? $"{Base}/{token}"
                : $"{Base}/{token}?lgu={Uri.EscapeDataString(municipalityCode)}";

        /// <summary>
        /// The operator's own reset URL. No LGU code: the operator belongs to no municipality, and the platform's
        /// console has no tenant branding to choose.
        /// </summary>
        public static string BuildForOperator(string token) => $"{OperatorBase}/{token}";
    }
}
