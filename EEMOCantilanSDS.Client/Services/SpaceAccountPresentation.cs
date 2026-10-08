using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>Presentation and navigation only. Account identity and every balance remain source-owned.</summary>
public sealed record SpaceAccountPresentation(ObligationKind Kind, string Name, string Route)
{
    public static readonly SpaceAccountPresentation Kanmanggay = new(ObligationKind.KanmanggaySpaceRental, "Kanmanggay", "/operations/kanmanggay");
    public static readonly SpaceAccountPresentation VegetableFruit = new(ObligationKind.VegetableFruitSpaceRental, "Vegetable / Fruits", "/operations/vegetable-fruit");
    public string RosterHref => Route + "/space-holders";
    public string ManagementHref => Route + "/manage-space-holders";
    public string ImportHref => Route + "/import";
    public string ReportsHref => Route + "/report";
    public static string Occupant(ObligationAccountDto account) => account.ActualOccupant ?? account.PayerName ?? "—";
    public static string Basis(ObligationAccountDto account) => account.Arrangement switch
    {
        OccupancyArrangement.SignedContract => "Signed lease contract",
        OccupancyArrangement.SpaceOnly => "No contract (space only)",
        OccupancyArrangement.Extension => "No contract (extension)",
        _ => "Not recorded"
    };
    public static string Reference(ObligationAccountDto account) =>
        account.Arrangement == OccupancyArrangement.SignedContract ? account.ContractReference ?? "" : "";
}
