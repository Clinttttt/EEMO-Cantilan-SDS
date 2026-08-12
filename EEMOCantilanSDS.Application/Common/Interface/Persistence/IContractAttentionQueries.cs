using EEMOCantilanSDS.Application.Dtos.Facilities;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Contracts the office needs to act on: terms lapsing soon, and terms already lapsed.
///
/// <para>
/// A read, and the counterpart to <see cref="IClosedStallAccountQueries"/> — together they are what the follow-up work
/// is built from: which agreements need renewing, and which accounts are still owed money after they ended. Neither
/// grants the ability to let, transfer, renew or close a stall, which is the point of separating them from
/// <see cref="IStallRepository"/>: a report should not be able to change what it reports on.
/// </para>
///
/// <para>
/// The as-of reading is not the same question as the current one and must not stand in for it. A report for a past month
/// has to judge lapsing against THAT month, or a contract that has since expired would be described as expiring soon.
/// </para>
/// </summary>
public interface IContractAttentionQueries
{
    /// <summary>Contracts lapsing within <paramref name="withinMonths"/>, or already lapsed, judged as of today.</summary>
    Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsync(int withinMonths, CancellationToken ct);

    /// <summary>
    /// The same reading judged as of the end of <paramref name="year"/>/<paramref name="month"/>, so a report for a past
    /// period describes the contracts as they stood then.
    /// </summary>
    Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsOfAsync(int year, int month, int withinMonths, CancellationToken ct);
}
