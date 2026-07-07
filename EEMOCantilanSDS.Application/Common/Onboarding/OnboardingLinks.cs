namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Builds the public onboarding-workspace link the operator emails to an approved LGU. The base points
    /// at the landing site's onboarding route; kept as a constant here (single known deployment) — promote to
    /// configuration if the landing domain changes.
    /// </summary>
    public static class OnboardingLinks
    {
        public const string Base = "https://www.stalltrack.site/onboarding";

        public static string Build(string token) => $"{Base}/{token}";
    }
}
