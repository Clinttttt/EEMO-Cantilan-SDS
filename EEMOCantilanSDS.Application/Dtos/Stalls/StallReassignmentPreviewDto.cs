namespace EEMOCantilanSDS.Application.Dtos.Stalls;

/// <summary>
/// What the office needs to see before placing a returning payor in a stall of their own, prepared server-side so
/// the client never has to infer a facility's billing shape or guess which number is free.
/// </summary>
/// <param name="PreviousStallId">The stall the payor used to hold. It is only read — never modified.</param>
/// <param name="FacilityName">The facility, in the tenant's own wording.</param>
/// <param name="PreviousStallNo">The number of the stall they used to hold, for the confirmation sentence.</param>
/// <param name="SectionLabel">
/// The section the new stall will belong to, in the tenant's own wording; empty for facilities without sections.
/// </param>
/// <param name="Occupant">The payor being placed.</param>
/// <param name="NameOnContract">Whatever name the previous contract was written in, if it differed.</param>
/// <param name="MonthlyRate">The rate they were on, offered as the starting point.</param>
/// <param name="IsDailyBilled">
/// True when the facility collects a daily fee for this stall, so the form can label the rate field honestly.
/// </param>
/// <param name="SuggestedStallNo">
/// The next number after the highest in that facility and section — a SUGGESTION only. The create path re-checks
/// uniqueness, so if two clerks work at once one is told the number is taken rather than both registering it.
/// </param>
/// <param name="SuggestedDurationYears">The term length the payor was previously on.</param>
public sealed record StallReassignmentPreviewDto(
    Guid PreviousStallId,
    string FacilityName,
    string PreviousStallNo,
    string SectionLabel,
    string Occupant,
    string? NameOnContract,
    decimal MonthlyRate,
    bool IsDailyBilled,
    string SuggestedStallNo,
    int SuggestedDurationYears
);
