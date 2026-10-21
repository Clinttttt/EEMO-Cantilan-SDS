using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Pages.Shared;

namespace EEMOCantilanSDS.Client.Components.Shared;

/// <summary>
/// Transfers an office-review request's proposed stall into the editable NPM form.
/// This is a clerical prefill only, never tenancy evidence, an assignment or a reservation.
/// </summary>
public static class NpmRequestStallPrefill
{
    public static bool TryApply(
        AddVendorModal.VendorModalForm form,
        FishMeatVendorRegistrationDto selectedVendor,
        Guid? requestedVendorId,
        int? requestedYear,
        string? proposedStall,
        string? proposedSection)
    {
        if (requestedVendorId != selectedVendor.Id || requestedYear != selectedVendor.TaxYear
            || string.IsNullOrWhiteSpace(proposedStall))
            return false;

        var stall = proposedStall.Trim();
        if (stall.Length > 40) return false;

        var section = proposedSection switch
        {
            "FishSection" => "Fish",
            "MeatSection" => "Meat",
            "VegetableArea" => "Vegetable",
            _ => null
        };

        if (section is not null) form.SelectedSection = section;
        form.StallNo = stall;
        return true;
    }
}
