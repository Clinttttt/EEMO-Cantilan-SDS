using EEMOCantilanSDS.Application.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.RecordCollectorRemittance;

/// <summary>
/// Records cash a collector has turned over to the office, for the collection days it answers for.
///
/// <para>
/// The Head and Administrators record this, being the officers accountable on the portal. A collector never records their
/// own remittance: the whole point of the record is that someone else received the money.
/// </para>
/// </summary>
/// <param name="ReceivedAt">
/// When the office received it. Null means now, which is the ordinary case; the office may state an earlier time where the
/// money was handed over before it could be entered.
/// </param>
/// <param name="ReferenceNo">
/// Report of collections or deposit slip number. Optional at the office's instruction, and its absence is reported back so
/// the officer is told plainly rather than left to notice.
/// </param>
public sealed record RecordCollectorRemittanceCommand(
    Guid CollectorId,
    decimal Amount,
    DateOnly CoversFrom,
    DateOnly CoversTo,
    DateTime? ReceivedAt = null,
    string? ReferenceNo = null,
    string? Notes = null) : IRequest<Result<RemittanceRecordedDto>>;

/// <summary>
/// What the office is told after a remittance is filed: the figures it was checked against, so the officer sees the
/// position they have just created rather than only that the save worked.
/// </summary>
public sealed record RemittanceRecordedDto(
    Guid RemittanceId,
    decimal Amount,
    decimal FeeCollectionsInPeriod,
    decimal RemittedInPeriod,
    decimal NotYetRemittedInPeriod,
    bool ReferenceNoMissing);
