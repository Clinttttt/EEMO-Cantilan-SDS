namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record NpmDailyBatchItem(Guid ClientItemId, Guid StallId);
public sealed record NpmDailyBatchIntent(Guid ClientCollectionSessionId, DateOnly BusinessDate, IReadOnlyList<NpmDailyBatchItem> Items);
public sealed record NpmDailyBatchSource(Guid StallId, Guid OccupancyId, string StallNumber, string Occupant,
    decimal EffectiveCharge, bool CanCollect, string? ReasonCode);
public sealed record NpmDailyBatchQuote(NpmDailyBatchIntent Intent, IReadOnlyList<NpmDailyBatchSource> Sources,
    decimal Total, string? QuoteFingerprint, IReadOnlyList<CollectionSessionProblem> Problems)
{
    public int ItemCount => Sources.Count(x => x.CanCollect);
    public bool CanRecord => Problems.Count == 0 && ItemCount > 0;
}
public sealed record RecordNpmDailyBatchRequest(NpmDailyBatchIntent Intent, string QuoteFingerprint);
