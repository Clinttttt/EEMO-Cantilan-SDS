using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>Presentation and navigation only. Account identity and every balance remain source-owned.</summary>
public sealed record SpaceAccountPresentation(ObligationKind Kind, string Name, string Route)
{
    public static readonly SpaceAccountPresentation Kanmanggay = new(ObligationKind.KanmanggaySpaceRental, "Kanmanggay", "/operations/kanmanggay");
    public static readonly SpaceAccountPresentation VegetableFruit = new(ObligationKind.VegetableFruitSpaceRental, "Vegetable / Fruits", "/operations/vegetable-fruit");
    public static readonly SpaceAccountPresentation FiestaAraw = new(ObligationKind.FiestaArawLotRental, "Fiesta / Araw", "/operations/fiesta-araw");
    /// <summary>The presentation of a space-rental line by its kind (the three target operations).</summary>
    public static SpaceAccountPresentation For(ObligationKind kind) => kind switch
    {
        ObligationKind.VegetableFruitSpaceRental => VegetableFruit,
        ObligationKind.FiestaArawLotRental => FiestaAraw,
        _ => Kanmanggay
    };
    public string RosterHref => Route + "/space-holders";
    public string ManagementHref => Route + "/manage-space-holders";
    public string ImportHref => Route + "/import";
    public string ReportsHref => Route + "/report";
    /// <summary>The account's own profile page, keyed by its source id (never by a name).</summary>
    public string ProfileHref(Guid accountId) => $"{Route}/profile/{accountId}";
    public static string Occupant(ObligationAccountDto account) => account.ActualOccupant ?? account.PayerName ?? "—";
    public static string Basis(ObligationAccountDto account) => account.Arrangement switch
    {
        OccupancyArrangement.SignedContract => "Signed lease contract",
        OccupancyArrangement.SpaceOnly => "No contract (space only)",
        OccupancyArrangement.Extension => "No contract (extension)",
        _ => "Not recorded"
    };
    /// <summary>As in the stall registers, the contract column names the lessee on the signed contract; when none is recorded it states the term.</summary>
    public static string RegistryContract(ObligationAccountDto account)
    {
        if (account.Event is not null) return "Event lot";
        if (account.Arrangement == OccupancyArrangement.SignedContract && account.ContractName is { Length: > 0 } contractName) return contractName;
        if (account.ContractYears is { } years && account.ContractEffectiveDate is { } signedDate)
            return $"{years} {(years == 1 ? "year" : "years")} · expires {DomainRules.TermLastDay(signedDate, years):MMM yyyy}";
        return account.Arrangement == OccupancyArrangement.SignedContract ? "Signed lease · term not recorded" : Basis(account);
    }
    /// <summary>The code the registry's workspace picker knows this operation by.</summary>
    public string RegistryCode => Kind == ObligationKind.FiestaArawLotRental ? "FA" : Kind == ObligationKind.KanmanggaySpaceRental ? "KM" : "Vegetable/Fruits";
    public string Unit => Kind == ObligationKind.FiestaArawLotRental ? "Lot" : "Space";
    public static string Reference(ObligationAccountDto account) =>
        account.Arrangement == OccupancyArrangement.SignedContract ? account.ContractReference ?? "" : "";
}
