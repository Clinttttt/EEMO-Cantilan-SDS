namespace EEMOCantilanSDS.Application.Common.Authorization
{
    /// <summary>
    /// Who the <b>platform operator</b> is, decided in ONE place.
    ///
    /// <para>
    /// The operator is an account carrying the <c>IsPlatformOperator</c> flag: somebody who belongs to no
    /// municipality's office and holds no municipal post. Nothing else qualifies.
    /// </para>
    ///
    /// <para>
    /// It used to also accept the DEFAULT municipality's Head, as a documented fallback from when that
    /// municipality was the only one on the platform and its Head genuinely was the operator. That clause made one
    /// municipality's Head the operator over all of them: it carried the power to trigger a restore of the whole
    /// shared database, every LGU's records included, and to approve and activate other municipalities' onboarding.
    /// The office raised it itself. A dedicated operator account now exists, which is the condition the clause was
    /// always written to be deleted on, so it is gone: the default municipality's Head is a municipal officer like
    /// any other, and keeps its own office's powers and no more.
    /// </para>
    ///
    /// <para>
    /// The inputs differ by caller: the API has claims, a handler has the database. So the DECISION is stated here
    /// as a pure function of the fact that matters, and each caller supplies it from whatever it holds. One rule,
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
        /// The account carries the <c>IsPlatformOperator</c> flag. This is the only thing that qualifies: an
        /// operator belongs to no LGU and holds no municipal office.
        /// </param>
        public static bool IsOperator(bool isDedicatedOperator) => isDedicatedOperator;
    }
}
