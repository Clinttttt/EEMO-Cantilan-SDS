namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Builds the public onboarding-workspace link the operator emails to an approved LGU. The base points
    /// at the landing site's onboarding route. It is environment-driven: set the <c>ONBOARDING_LINK_BASE</c>
    /// environment variable / app setting to override per deployment; when unset it falls back to the known
    /// production landing domain, so existing deployments are unchanged.
    /// </summary>
    public static class OnboardingLinks
    {
        private const string DefaultBase = "https://www.stalltrack.site/onboarding";

        public static string Base =>
            Environment.GetEnvironmentVariable("ONBOARDING_LINK_BASE") is { Length: > 0 } configured
                ? configured.TrimEnd('/')
                : DefaultBase;

        public static string Build(string token) => $"{Base}/{token}";
    }
}
