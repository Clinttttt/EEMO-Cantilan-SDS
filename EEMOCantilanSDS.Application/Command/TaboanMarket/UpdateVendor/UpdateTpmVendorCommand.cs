using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.UpdateVendor;

/// <summary>
/// Edits a vendor's attendance for a market day. Vendor name/goods are vendor-level and therefore
/// apply to every market day this vendor appears in; IsPaid/ORNumber are specific to this attendance.
/// </summary>
public record UpdateTpmVendorCommand(
    Guid AttendanceId,
    string VendorName,
    string Goods,
    bool IsPaid,
    string? ORNumber = null
) : IRequest<Result<bool>>;
