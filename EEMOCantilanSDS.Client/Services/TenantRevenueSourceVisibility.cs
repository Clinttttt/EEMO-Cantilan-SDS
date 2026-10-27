using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Presentation-only filtering for the office's operational source lists. The server's
/// authoritative Monthly Income, source projection, group cash and revision evidence are unchanged.
/// </summary>
public static class TenantRevenueSourceVisibility
{
    public static bool HasEvidence(RevenueSourcePerformanceRowDto row) =>
        row.Collected != 0m || row.SystemAmount != 0m || row.AdjustmentAmount != 0m ||
        (row.TransactionCount ?? 0) > 0 || (row.DocumentCount ?? 0) > 0 ||
        row.AnnualTarget is not null;

    /// <summary>
    /// Existing (non-adopted) offices retain their previous catalog. For configured LGUs,
    /// list the selected operation or configured facility, retaining any row with
    /// historical financial evidence even after an operation is de-listed.
    /// A missing facility roster is unknown, not evidence that a facility is absent.
    /// </summary>
    public static bool Show(
        RevenueSourcePerformanceRowDto row,
        OperationNameService? names,
        IReadOnlyCollection<FacilityCode>? configuredFacilities = null)
    {
        if (names?.Adopted != true || HasEvidence(row)) return true;

        if (TenantOperationCatalog.FindBySourceKey(row.Key) is not null)
            return names.IsListed(row.Key);

        if (row.Facility is { } facility)
            return configuredFacilities is null || configuredFacilities.Contains(facility);

        return row.Key switch
        {
            "TABO" => HasFacility(FacilityCode.TPM, configuredFacilities),
            "SLAUGHTERHOUSE" => HasFacility(FacilityCode.SLH, configuredFacilities),
            RevenueClassificationCodes.TerminalComfortRoom or
                RevenueClassificationCodes.TerminalPullPulVansCargoVans or
                RevenueClassificationCodes.TerminalTricycad =>
                    HasFacility(FacilityCode.TRM, configuredFacilities),

            // An unallocated rental or receivable is evidence, not another enabled operation.
            "RENT_OTHER" or "ARREARS" or "PENALTIES_AND_FINES" => false,

            // An additional classification can be enabled by a real receipt policy;
            // never infer that an unmapped template row was adopted by this tenant.
            _ => !string.IsNullOrWhiteSpace(row.Instruments)
        };
    }

    private static bool HasFacility(FacilityCode code, IReadOnlyCollection<FacilityCode>? facilities) =>
        facilities is null || facilities.Contains(code);
}
