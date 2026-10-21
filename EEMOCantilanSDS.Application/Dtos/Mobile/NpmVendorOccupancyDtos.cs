using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>Head-confirmed tenancy creation only. This request never accepts or posts a payment.</summary>
public sealed record CreateNpmVendorOccupancyRequest(Guid ClientOperationId, Guid VendorRegistrationId,
    string ExpectedVendorName, string ConfirmationNote, CreateStallCommand Occupancy,
    bool DuplicateReviewConfirmed = false, bool ExceptionConfirmed = false);
public sealed record NpmVendorOccupancyResult(Guid StallId, Guid ContractId, Guid AssociationId, bool Replayed);
