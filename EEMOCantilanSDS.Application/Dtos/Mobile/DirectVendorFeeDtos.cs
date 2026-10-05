using EEMOCantilanSDS.Domain.Enums;
namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record DirectVendorFeeRequest(Guid ClientOperationId, DateOnly BusinessDate, Guid StallId, Guid PayorId, decimal AmountReceived);
public enum VendorFeeSourceStatus { Available = 1, NeedsPayor = 2, Unavailable = 3 }
public enum VendorFeeSourceAction { None = 0, LinkBusinessPayor = 1, OfficeReview = 2 }
public sealed record DirectVendorFeeSource(Guid StallId, string StallNo, string Section, Guid? PayorId, string PayerName,
    bool CanCollect, string? Reason, Guid? OccupancyId = null, bool HasExistingPayorLink = false)
{
    public VendorFeeSourceStatus Status => CanCollect ? VendorFeeSourceStatus.Available
        : PayorId is null && !HasExistingPayorLink ? VendorFeeSourceStatus.NeedsPayor : VendorFeeSourceStatus.Unavailable;
    public string? ReasonCode => Status switch
    {
        VendorFeeSourceStatus.Available => null,
        VendorFeeSourceStatus.NeedsPayor => "RequiresPayorLink",
        _ => HasExistingPayorLink && PayorId is null ? "PayorLinkUnavailable" : "SourceNotAvailable"
    };
    public VendorFeeSourceAction RequiredAction => Status == VendorFeeSourceStatus.NeedsPayor ? VendorFeeSourceAction.LinkBusinessPayor
        : Status == VendorFeeSourceStatus.Unavailable ? VendorFeeSourceAction.OfficeReview : VendorFeeSourceAction.None;
}
public sealed record DirectVendorFeeQuote(DirectVendorFeeSource Source, decimal Amount, RevenueInstrumentType Instrument, string Version);
