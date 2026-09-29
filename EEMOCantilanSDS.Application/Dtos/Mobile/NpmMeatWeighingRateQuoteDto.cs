namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>A display-only tenant rate quote for one NPM collection business date.</summary>
public sealed record NpmMeatWeighingRateQuoteDto(
    DateOnly BusinessDate,
    bool IsAvailable,
    decimal? RatePerKilo,
    DateOnly? EffectiveDate);
