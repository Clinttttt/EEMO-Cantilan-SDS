using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// An append-only, municipality-owned revision of an independent Fish/Meat weighing rate.
/// A disabled revision is explicit: a missing revision means legacy configuration was never adopted.
/// Earlier revisions and posted CollectionLine calculation snapshots are never rewritten.
/// </summary>
public sealed class WeighingRateVersion : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public FishMeatVendorType VendorType { get; private set; }
    public bool IsEnabled { get; private set; }
    public decimal? RatePerKilo { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string RecordedBy { get; private set; } = string.Empty;

    private WeighingRateVersion() { }

    public static WeighingRateVersion Create(Guid municipalityId, FishMeatVendorType type,
        bool enabled, decimal? rate, DateOnly effectiveDate, string actor)
    {
        if (municipalityId == Guid.Empty || !Enum.IsDefined(type) || effectiveDate == DateOnly.MinValue)
            throw new ArgumentException("A valid municipality, weighing type and effective date are required.");
        if (enabled && (rate is null || rate <= 0m || rate > 999999m || decimal.Round(rate.Value, 2) != rate)
            || !enabled && rate is not null)
            throw new ArgumentException("An enabled weighing service requires a positive two-decimal peso rate; disabled services have no rate.");
        return new WeighingRateVersion {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, VendorType = type,
            IsEnabled = enabled, RatePerKilo = rate, EffectiveDate = effectiveDate,
            RecordedAtUtc = DateTime.UtcNow, RecordedBy = string.IsNullOrWhiteSpace(actor) ? "Head" : actor.Trim()
        };
    }
}
