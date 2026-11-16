using System.Globalization;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Mobile.Records;

/// <summary>Presentation names only; canonical source identifiers remain unchanged.</summary>
public static class CollectorRecordLabels
{
    public static string Display(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Collection";
        var value = code.Trim();
        return value.ToUpperInvariant() switch
        {
            "VEGETABLE_FRUIT_SPACE_RENTAL" => "Vegetable / Fruit Space Rental",
            "KANMANGGAY_SPACE_RENTAL" => "Kanmanggay Space Rental",
            "FIESTA_ARAW_LOT_RENTAL" => "Fiesta / Araw Lot Rental",
            "FISH_MEAT_VENDOR_FEE" => "Fish / Meat Vendor Fee",
            "WEIGHT_AND_MEASURE" => "Weight & Measure",
            "TRANSPORTATION_PARKING" => "Transportation / Parking",
            "ECF" => "Electricity",
            "WCF" => "Water",
            _ => value.Contains('_')
                ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').ToLowerInvariant())
                : value
        };
    }

    /// <summary>The office's own short name for a facility ("DTC"), as its Web portal shows it; the code when the server sent none.</summary>
    public static string FacilityShortName(IEnumerable<MobileFacilityMenuItemDto>? facilities, FacilityCode code) =>
        facilities?.FirstOrDefault(f => f.Code == code)?.ShortName is { Length: > 0 } name ? name : code.ToString();

    public static string? InstrumentCode(RevenueInstrumentType? kind) => kind switch
    {
        RevenueInstrumentType.OfficialReceipt => "OR",
        RevenueInstrumentType.CashTicket => "CT",
        _ => null
    };
}
