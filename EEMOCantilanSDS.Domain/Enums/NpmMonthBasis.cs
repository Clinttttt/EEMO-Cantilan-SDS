namespace EEMOCantilanSDS.Domain.Enums
{
    /// <summary>
    /// How an office measures what a daily-collected market month OWES.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stated by the office, never inferred from which municipality it is. A tenant code must not decide behaviour: a
    /// request that carries no municipality claim falls back to the default tenant, so a code comparison would hand the
    /// wrong rule to the very office that most needed the right one. Cantilan bills a monthly goal because its
    /// configuration says so, and any other office that says so bills identically.
    /// </para>
    /// <para>
    /// <see cref="RentGoal"/> is the default, so an office that has stated nothing keeps the behaviour this platform has
    /// always had.
    /// </para>
    /// </remarks>
    public enum NpmMonthBasis
    {
        /// <summary>
        /// A month is LET FOR A RENT and collected in daily installments. February owes the same rent as August, a
        /// thirty-first day adds no installment, and a month whose installments fall short of its rent carries a month-end
        /// adjustment for the difference. This is Cantilan's ordinance and the platform's default.
        /// </summary>
        RentGoal = 1,

        /// <summary>
        /// A month owes THE DAYS IT HAS, one daily fee each. A 31-day month owes thirty-one fees and February owes
        /// twenty-eight, so no two months owe the same and there is nothing to adjust. An office on this basis states no
        /// monthly amount at all, because a monthly amount would be a figure no month actually owes.
        /// </summary>
        PureDays = 2,
    }
}
