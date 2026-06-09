using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;

public record MarkVendorPaidCommand(
    Guid AttendanceId,
    bool IsPaid,
    string? ORNumber = null
) : IRequest<Result<bool>>;
