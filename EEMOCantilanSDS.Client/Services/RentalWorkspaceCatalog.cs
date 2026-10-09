using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

// Presentation identities are deliberately separate from persisted facility/source identifiers.
public enum RentalWorkspaceKey { Ice, Kanmanggay, FiestaAraw }
public abstract record RentalWorkspaceSource;
public sealed record FacilityRentalSource(FacilityCode Code) : RentalWorkspaceSource;
public sealed record MonthlySpaceRentalSource(ObligationKind Kind) : RentalWorkspaceSource;
public sealed record EventLotRentalSource(ObligationKind Kind) : RentalWorkspaceSource;
public enum RentalNumberingPolicy { FacilitySuggestionValidatedByServer, ServerAssignedSpace, EventScopedLot }
public enum RentalContractPolicy { ScopeUnconfirmed, ReportedThreeYearStandardPendingLifecycleApproval, EventOnly }
[Flags]
public enum RentalSection { None = 0, Occupant = 1, ContractEvidence = 2, Unit = 4, Area = 8, MonthlyRate = 16, Event = 32, EventAmount = 64 }
[Flags]
public enum RentalAdministration { None = 0, Periods = 1, ChangeAmount = 2, Close = 4, EditRegistration = 8 }
[Flags]
public enum RentalReporting { ExistingReport = 1, Monthly = 2, Yearly = 4, Status = 8, AggregateHistory = 16 }

/// <summary>UI availability only; callers and servers still enforce permissions and source rules.</summary>
public sealed record RentalWorkspaceDefinition(
    RentalWorkspaceKey Key, string DisplayCode, string? OperationName, RentalWorkspaceSource Source,
    string OverviewRoute, string? RosterRoute, string? ManagementRoute, string ReportsRoute, string? ImportRoute,
    bool RegistrationAvailable, RentalAdministration Administration, RentalSection Sections,
    RentalNumberingPolicy Numbering, RentalReporting Reporting, RentalContractPolicy ContractPolicy,
    string TableTitle)
{
    public string DisplayName(FacilityState facilities) => Source is FacilityRentalSource facility
        ? facilities.NameOf(facility.Code) : OperationName!;
    public string Unit(FacilityState facilities) => Source is FacilityRentalSource facility
        ? FacilityState.HolderUnitOf(facility.Code) : Source is EventLotRentalSource ? "Lot" : "Space";
}

/// <summary>Bounded Phase 1 catalog. Further sources can join without changing their financial identity.</summary>
public static class RentalWorkspaceCatalog
{
    public static readonly RentalWorkspaceDefinition Ice = new(
        RentalWorkspaceKey.Ice, "ICE", null, new FacilityRentalSource(FacilityCode.ICE),
        "/facility/ice", "/list-stall-holders?facility=ice", "/vendors?facility=ice", "/ice/reports", "/import/ice",
        true, RentalAdministration.EditRegistration,
        RentalSection.Occupant | RentalSection.ContractEvidence | RentalSection.Unit | RentalSection.Area | RentalSection.MonthlyRate,
        RentalNumberingPolicy.FacilitySuggestionValidatedByServer,
        RentalReporting.ExistingReport | RentalReporting.Monthly | RentalReporting.Yearly | RentalReporting.Status | RentalReporting.AggregateHistory,
        RentalContractPolicy.ScopeUnconfirmed, "All Spaces");
    public static readonly RentalWorkspaceDefinition Kanmanggay = new(
        RentalWorkspaceKey.Kanmanggay, "KM", SpaceAccountPresentation.Kanmanggay.Name, new MonthlySpaceRentalSource(SpaceAccountPresentation.Kanmanggay.Kind),
        SpaceAccountPresentation.Kanmanggay.Route, SpaceAccountPresentation.Kanmanggay.RosterHref, SpaceAccountPresentation.Kanmanggay.ManagementHref,
        SpaceAccountPresentation.Kanmanggay.ReportsHref, SpaceAccountPresentation.Kanmanggay.ImportHref,
        true, RentalAdministration.Periods | RentalAdministration.ChangeAmount | RentalAdministration.Close,
        RentalSection.Occupant | RentalSection.ContractEvidence | RentalSection.Unit | RentalSection.MonthlyRate,
        RentalNumberingPolicy.ServerAssignedSpace, RentalReporting.ExistingReport,
        RentalContractPolicy.ReportedThreeYearStandardPendingLifecycleApproval, "Space Rentals");
    public static readonly RentalWorkspaceDefinition FiestaAraw = new(
        RentalWorkspaceKey.FiestaAraw, "FA", "Fiesta / Araw", new EventLotRentalSource(ObligationKind.FiestaArawLotRental),
        "/operations/fiesta-araw", null, null, "/operations/fiesta-araw/report", "/operations/fiesta-araw/import",
        true, RentalAdministration.Periods | RentalAdministration.Close,
        RentalSection.Occupant | RentalSection.Unit | RentalSection.Event | RentalSection.EventAmount,
        RentalNumberingPolicy.EventScopedLot, RentalReporting.ExistingReport, RentalContractPolicy.EventOnly, "Event Lots");

    public static IReadOnlyList<RentalWorkspaceDefinition> Pilot { get; } = Array.AsReadOnly(new[] { Ice, Kanmanggay, FiestaAraw });
    public static RentalWorkspaceDefinition Get(RentalWorkspaceKey key) => Pilot.Single(x => x.Key == key);
}
