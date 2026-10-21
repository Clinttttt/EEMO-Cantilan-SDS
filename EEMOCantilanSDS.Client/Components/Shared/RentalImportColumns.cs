namespace EEMOCantilanSDS.Client.Components.Shared;

/// <summary>
/// The NCC/TCC rental-register column presentation shared with monthly Rent / Space imports.
/// Keys are adaptable because legacy backends use different field names. This is UI metadata:
/// no normalization of contract terms, invoices, or delinquency is performed here.
/// </summary>
public static class RentalImportColumns
{
    public static readonly ImportColumn Occupant = new("occupant", "Actual Occupant (Leasee)", MinWidth: 200, Placeholder: "Full name");
    public static readonly ImportColumn ContractName = new("contractName", "Name on Contract", MinWidth: 200, Placeholder: "Per signed contract");
    public static readonly ImportColumn Number = new("stallNo", "Stall / Space No.", MinWidth: 110, Placeholder: "e.g. 1");
    public static readonly ImportColumn Effectivity = new("effectivity", "Effectivity Date", "date", MinWidth: 150);
    public static readonly ImportColumn Years = new("years", "No. of Years", "number", MinWidth: 110, Placeholder: "3");
    public static readonly ImportColumn Area = new("area", "Area (sqm)", "number", MinWidth: 110, Placeholder: "0");
    public static readonly ImportColumn ContractMonthly = new("monthly", "Monthly Rental / Contract", "number", MinWidth: 160, Placeholder: "0.00");
    public static readonly ImportColumn ActualMonthly = new("actualMonthly", "Actual Mo. Rental", "number", MinWidth: 150, Placeholder: "0.00");
    public static readonly ImportColumn WholeYear = new("wholeYear", "Whole Year Rental", "number", MinWidth: 150, Placeholder: "0.00");
    public static readonly ImportColumn Delinquent = new("delinquent", "Delinquent (₱)", "number", MinWidth: 130, Placeholder: "0.00");
    public static readonly ImportColumn AreaLocation = new("areaLoc", "Area Loc.", MinWidth: 135, Placeholder: "e.g. Corner");
}
