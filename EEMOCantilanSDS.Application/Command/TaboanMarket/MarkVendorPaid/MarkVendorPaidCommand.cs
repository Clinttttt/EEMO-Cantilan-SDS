using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;

public record MarkVendorPaidCommand(
    Guid AttendanceId,
    bool IsPaid,
    Guid CollectorId
) : IRequest<Result<bool>>;
