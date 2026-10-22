using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public enum VendorRegistryImportState { New = 1, PossibleDuplicateRequiresReview = 2, Invalid = 3, UseExisting = 4 }
public sealed record VendorRegistryImportRow(int RowNumber, RegisterFishMeatVendorRequest Registration, bool ConfirmSeparateRegistration = false,
    Guid? ReuseRegistrationId = null);
public sealed record VendorRegistryImportRequest(IReadOnlyList<VendorRegistryImportRow> Rows);
public sealed record VendorRegistryImportProblem(string Code, string Message);
public sealed record VendorRegistryImportExistingMatch(Guid Id, string DisplayName, string? BusinessName, FishMeatVendorType VendorType,
    VendorRegistrationStatus Status);
public sealed record VendorRegistryImportNpmMatch(Guid ContractId, Guid StallId, string Occupant, string StallNo, string Area, bool AlreadyLinked);
public sealed record VendorRegistryImportPreviewRow(int RowNumber, RegisterFishMeatVendorRequest Registration,
    VendorRegistryImportState State, IReadOnlyList<VendorRegistryImportProblem> Problems, IReadOnlyList<Guid> PossibleDuplicateIds,
    IReadOnlyList<VendorRegistryImportNpmMatch>? PossibleNpmOccupancies = null,
    IReadOnlyList<VendorRegistryImportExistingMatch>? PossibleExistingRegistrations = null);
public sealed record VendorRegistryImportPreview(IReadOnlyList<VendorRegistryImportPreviewRow> Rows)
{
    public bool CanSave => Rows.Count > 0 && Rows.All(x => x.State is VendorRegistryImportState.New or VendorRegistryImportState.UseExisting);
}
public sealed record VendorRegistryImportSavedRow(int RowNumber, FishMeatVendorRegistrationDto Registration, bool Reused = false);
public sealed record VendorRegistryImportResult(IReadOnlyList<VendorRegistryImportSavedRow> Rows);
public sealed record VendorRegistrySummary(int TaxYear, int CurrentTaxYear, DateOnly ActivityFrom, DateOnly ActivityTo, int TotalRegistered, int FishCount, int MeatCount,
    IReadOnlyList<FishMeatVendorRegistrationDto> Registrations, IReadOnlyList<SourceNativeActivityDto> CurrentMonthCollections);
