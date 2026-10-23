using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record WeighingTypeConfigurationDto(FishMeatVendorType VendorType, bool Enabled, decimal? RatePerKilo,
    DateOnly? EffectiveDate, bool LegacyRate);
public sealed record WeighingConfigurationDto(DateOnly AsOf, DateOnly NextEffectiveDate,
    IReadOnlyList<WeighingTypeConfigurationDto> Current, IReadOnlyList<WeighingTypeConfigurationDto> Next);
public sealed record SetWeighingConfigurationRequest(bool FishEnabled, decimal? FishRatePerKilo,
    bool MeatEnabled, decimal? MeatRatePerKilo);
public sealed record WeighingCollectionRowDto(DateOnly BusinessDate, string ReferenceCode, string Vendor,
    FishMeatVendorType VendorType, decimal Kilograms, decimal RatePerKilo, decimal NetAmount, string Status);
public sealed record WeighingCollectionReportDto(int Year, int Month, IReadOnlyList<WeighingCollectionRowDto> Rows)
{
    public decimal FishTotal => Rows.Where(x => x.VendorType == FishMeatVendorType.Fish).Sum(x => x.NetAmount);
    public decimal MeatTotal => Rows.Where(x => x.VendorType == FishMeatVendorType.Meat).Sum(x => x.NetAmount);
    public decimal Total => FishTotal + MeatTotal;
}
