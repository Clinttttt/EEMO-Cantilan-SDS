using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;

public record MarkVendorPaidCommand(
    Guid AttendanceId,
    bool IsPaid
) : IRequest<Result<bool>>;
