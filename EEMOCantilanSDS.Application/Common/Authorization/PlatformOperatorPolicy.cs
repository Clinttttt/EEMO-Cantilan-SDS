namespace EEMOCantilanSDS.Application.Common.Authorization
{
    /// <summary>
    /// Who the <b>platform operator</b> is, decided in ONE place.
    ///
    /// <para>
    /// The rule lived in three: the API's <c>PlatformOperator</c> policy (SuperAdmin of the default tenant),
    /// <see cref="PlatformOperatorGuard"/> (the dedicated <c>IsPlatformOperator</c> flag, or that same fallback), and
    /// an inlined copy inside the activation handler (the fallback only). They disagreed, and not harmlessly: a
    /// DEDICATED operator account — the mechanism intended to replace the fallback — was accepted by the handlers and
    /// refused by the API policy and by activation, so the account could approve an LGU's onboarding and then not
    /// activate it.
    /// </para>
    ///
    /// <para>
    /// The inputs differ by caller: the API has claims, a handler has the database. So the DECISION is stated here as
    /// a pure function of the two facts that matter, and each caller supplies them from whatever it holds. One rule,
    /// two ways in, no third opinion.
    /// </para>
    /// </summary>
    public static class PlatformOperatorPolicy
    {
        /// <summary>The role a municipality's own Head holds. Compared case-insensitively.</summary>
        public const string SuperAdminRole = "SuperAdmin";

        /// <summary>
        /// Whether the caller may act as the platform operator.
        /// </summary>
        /// <param name="isDedicatedOperator">
        /// The account carries the <c>IsPlatformOperator</c> flag. This is the intended mechanism: an operator who
        /// belongs to no LGU and holds no municipal office.
        /// </param>
        /// <param name="role">The caller's role.</param>
        /// <param name="isDefaultTenant">The caller belongs to the DEFAULT municipality.</param>
        /// <remarks>
        /// The second clause is a documented backward-compatible fallback, not a second rule: while Cantilan was the
        /// only LGU, its Head WAS the operator, and removing it would lock the office out of its own onboarding before
        /// a dedicated account exists. It is deliberately narrow — the default tenant's SuperAdmin and nobody else —
        /// and is the clause to delete once every deployment has a dedicated operator.
        /// </remarks>
        public static bool IsOperator(bool isDedicatedOperator, string? role, bool isDefaultTenant) =>
            isDedicatedOperator
            || (isDefaultTenant && string.Equals(role, SuperAdminRole, System.StringComparison.OrdinalIgnoreCase));
    }
}
