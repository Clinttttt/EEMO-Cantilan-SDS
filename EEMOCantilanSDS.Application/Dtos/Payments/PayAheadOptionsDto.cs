namespace EEMOCantilanSDS.Application.Dtos.Payments;

public sealed record PayAheadMonthDto(int Year, int Month, decimal Amount);

/// <summary>The months the office can take ahead now, soonest first, and how many of them one receipt may cover. <paramref name="HeldBy"/> says what must be paid first.</summary>
public sealed record PayAheadOptionsDto(IReadOnlyList<PayAheadMonthDto> Months, IReadOnlyList<int> Counts, string? HeldBy);
