using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// One month of legacy-authoritative cash for one official Monthly Income row (IA-051). Only money whose authoritative
/// record is still the legacy source: a row that has gone canonical is excluded here and counted from its Collection.
/// </summary>
public sealed record LegacyIncomeFact(
    int Month,
    string ClassificationCode,
    FacilityCode? Facility,
    decimal Amount,
    string Source);

/// <summary>Reads legacy-authoritative collected cash for a year, never a canonical or shadow representation.</summary>
public interface ILegacyMonthlyIncomeReader
{
    Task<IReadOnlyList<LegacyIncomeFact>> GetAsync(int year, CancellationToken ct = default);
}
