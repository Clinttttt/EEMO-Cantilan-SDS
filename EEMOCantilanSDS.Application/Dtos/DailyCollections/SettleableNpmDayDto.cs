namespace EEMOCantilanSDS.Application.Dtos.DailyCollections;

/// <summary>A single NPM day that can still be settled (unpaid, under contract, not closed, not future).</summary>
public sealed record SettleableNpmDayDto(DateOnly Date, decimal Fee);
