namespace EEMOCantilanSDS.Application.Dtos.Payments;

public sealed record PayAheadMonthDto(int Year, int Month, decimal Amount);

/// <summary>The months the office can take ahead now, soonest first, and how many of them one receipt may cover. <paramref name="HeldBy"/> says what must be paid first.</summary>
public sealed record PayAheadOptionsDto(IReadOnlyList<PayAheadMonthDto> Months, IReadOnlyList<int> Counts, string? HeldBy);

/// <summary>One month a held online payment would pay: what the month still owes and how much of the payment goes to it (less only on the last month).</summary>
public sealed record HeldPaymentMonthDto(int Year, int Month, decimal Due, decimal Applied);

/// <summary>Where a held online payment would go if applied to the space's rent, oldest month first. <paramref name="Refusal"/> says why it cannot be applied.</summary>
public sealed record HeldPaymentPlanDto(decimal Amount, IReadOnlyList<HeldPaymentMonthDto> Months, string? Refusal);
