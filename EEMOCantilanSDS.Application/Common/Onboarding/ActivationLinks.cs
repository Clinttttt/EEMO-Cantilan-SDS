namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Builds the Head's one-time account-activation (set-password) link emailed at activation. The base
    /// points at the operator console's activation route; kept as a constant here (single known deployment) —
    /// promote to configuration if the console domain changes. Mirrors <see cref="OnboardingLinks"/>.
    /// </summary>
    public static class ActivationLinks
    {
        public const string Base = "https://console.stalltrack.site/activate";

        public static string Build(string token) => $"{Base}/{token}";
    }
}
