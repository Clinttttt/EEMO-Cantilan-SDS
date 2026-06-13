using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Components.Pages.Payor;

/// <summary>Presentation helpers for the payor portal (currency, facility names, status badges).</summary>
public static class PayorFormat
{
    public static string Currency(decimal value) => $"₱{value:N2}";

    public static string FacilityName(FacilityCode code) => code switch
    {
        FacilityCode.NPM => "New Public Market",
        FacilityCode.TCC => "Tampak Commercial Center",
        FacilityCode.NCC => "New Commercial Center",
        FacilityCode.BBQ => "Barbecue Stand",
        FacilityCode.ICE => "Iceplant",
        FacilityCode.SLH => "Slaughterhouse",
        FacilityCode.TRM => "Transport Terminal",
        FacilityCode.TPM => "Tabo-an Public Market",
        _ => code.ToString()
    };

    public static string StatusText(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "Paid",
        PaymentStatus.Partial => "Partial",
        _ => "Unpaid"
    };

    public static string StatusClass(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "badge-paid",
        PaymentStatus.Partial => "badge-partial",
        _ => "badge-unpaid"
    };
}
