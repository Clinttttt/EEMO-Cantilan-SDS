namespace EEMOCantilanSDS.Application.Common.Onboarding
{
    /// <summary>
    /// Links to the public landing site, for the pages a municipality reaches BEFORE it has a portal to sign in to.
    ///
    /// <para>
    /// Separate from <see cref="OnboardingLinks"/> on purpose, because they are not interchangeable and confusing them was a real
    /// fault. <c>OnboardingLinks.Base</c> is only ever the stem of a TOKEN link — the landing site routes
    /// <c>onboarding/:token</c> and nothing else, so a redirect to the base alone falls through that site's wildcard route and
    /// renders its marketing home page. The office reported exactly that: choosing an unactivated municipality landed on
    /// "Run public market &amp; facility collections on one clean platform."
    /// </para>
    ///
    /// <para>
    /// A municipality that has not been activated belongs on its OWN page, which the landing site routes as
    /// <c>municipalities/:code</c>. That is where its status is shown and where an assessment can be requested — the step this
    /// system answers with <c>ApproveAssessmentRequestCommandHandler</c>. Onboarding proper opens later, from the token link the
    /// operator sends once the assessment is approved.
    /// </para>
    ///
    /// <para>
    /// Environment-driven like its sibling: set <c>LANDING_SITE_BASE</c> to point a deployment elsewhere. Unset, it falls back to
    /// the known production domain, so existing deployments are unchanged.
    /// </para>
    /// </summary>
    public static class LandingSiteLinks
    {
        private const string DefaultBase = "https://www.stalltrack.site";

        public static string Base =>
            Environment.GetEnvironmentVariable("LANDING_SITE_BASE") is { Length: > 0 } configured
                ? configured.TrimEnd('/')
                : DefaultBase;

        /// <summary>
        /// The municipality's public page. The code is lower-cased because that is how the landing site's own links are written
        /// — its selector sends people to <c>?lgu=carrascal</c>, not <c>?lgu=CARRASCAL</c> — and a case its router does not match
        /// would fall through to the same wildcard that caused the fault this exists to fix.
        /// </summary>
        public static string MunicipalityPage(string code) =>
            $"{Base}/municipalities/{Uri.EscapeDataString((code ?? string.Empty).Trim().ToLowerInvariant())}";
    }
}
