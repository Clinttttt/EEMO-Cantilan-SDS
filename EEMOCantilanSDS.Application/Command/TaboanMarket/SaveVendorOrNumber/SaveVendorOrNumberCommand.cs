using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.SaveVendorOrNumber;

public record SaveVendorOrNumberCommand(
    Guid AttendanceId,
    string ORNumber
) : IRequest<Result<bool>>;
