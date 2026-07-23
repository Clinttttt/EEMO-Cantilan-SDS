namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Builds the Head's one-time account-activation (set-password) link emailed at activation. The base
    /// points at the operator console's activation route. It is environment-driven: set the
    /// <c>ACTIVATION_LINK_BASE</c> environment variable / app setting to override per deployment; when unset
    /// it falls back to the known production console domain, so existing deployments are unchanged.
    /// Mirrors <see cref="OnboardingLinks"/>.
    /// </summary>
    public static class ActivationLinks
    {
        private const string DefaultBase = "https://console.stalltrack.site/activate";

        public static string Base =>
            Environment.GetEnvironmentVariable("ACTIVATION_LINK_BASE") is { Length: > 0 } configured
                ? configured.TrimEnd('/')
                : DefaultBase;

        public static string Build(string token) => $"{Base}/{token}";
    }
}
