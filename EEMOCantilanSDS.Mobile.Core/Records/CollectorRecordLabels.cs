using System.Globalization;
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

    public static string? InstrumentCode(RevenueInstrumentType? kind) => kind switch
    {
        RevenueInstrumentType.OfficialReceipt => "OR",
        RevenueInstrumentType.CashTicket => "CT",
        _ => null
    };
}
