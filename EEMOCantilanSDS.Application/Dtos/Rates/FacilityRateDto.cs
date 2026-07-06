using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Rates
{
    /// <summary>
    /// The caller LGU's currently-effective fixed rate for one facility + rate key (read-back for the portal
    /// to display/pre-fill fee and metered-utility-default forms).
    /// </summary>
    public record FacilityRateDto(
        FacilityCode FacilityCode,
        FeeRateKey Key,
        decimal Amount,
        DateOnly EffectiveDate);
}
