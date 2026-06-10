using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;

public record AddVendorToMarketDayCommand(
    string VendorName,
    string Goods,
    DateOnly MarketDate,
    string? ORNumber = null
) : IRequest<Result<TpmVendorAttendanceDto>>;
