namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public enum VendorRegistryImportState { New = 1, PossibleDuplicateRequiresReview = 2, Invalid = 3 }
public sealed record VendorRegistryImportRow(int RowNumber, RegisterFishMeatVendorRequest Registration, bool ConfirmSeparateRegistration = false);
public sealed record VendorRegistryImportRequest(IReadOnlyList<VendorRegistryImportRow> Rows);
public sealed record VendorRegistryImportProblem(string Code, string Message);
public sealed record VendorRegistryImportPreviewRow(int RowNumber, RegisterFishMeatVendorRequest Registration,
    VendorRegistryImportState State, IReadOnlyList<VendorRegistryImportProblem> Problems, IReadOnlyList<Guid> PossibleDuplicateIds);
public sealed record VendorRegistryImportPreview(IReadOnlyList<VendorRegistryImportPreviewRow> Rows)
{
    public bool CanSave => Rows.Count > 0 && Rows.All(x => x.State == VendorRegistryImportState.New);
}
public sealed record VendorRegistryImportSavedRow(int RowNumber, FishMeatVendorRegistrationDto Registration);
public sealed record VendorRegistryImportResult(IReadOnlyList<VendorRegistryImportSavedRow> Rows);
public sealed record VendorRegistrySummary(int TaxYear, int CurrentTaxYear, DateOnly ActivityFrom, DateOnly ActivityTo, int TotalRegistered, int FishCount, int MeatCount,
    IReadOnlyList<FishMeatVendorRegistrationDto> Registrations, IReadOnlyList<SourceNativeActivityDto> CurrentMonthCollections);
