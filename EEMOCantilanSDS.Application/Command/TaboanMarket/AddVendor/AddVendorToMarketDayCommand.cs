using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;

public record AddVendorToMarketDayCommand(
    string VendorName,
    string Goods,
    DateOnly MarketDate,
    string? ORNumber = null,
    // Offline-sync idempotency key (set when replaying a queued offline attendance); null online.
    Guid? ClientOperationId = null
) : IRequest<Result<TpmVendorAttendanceDto>>;
