using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetPaymentSettings;

public class GetMunicipalityPaymentSettingsQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser) : IRequestHandler<GetMunicipalityPaymentSettingsQuery, Result<PaymentSettingsDto>>
{
    public async Task<Result<PaymentSettingsDto>> Handle(GetMunicipalityPaymentSettingsQuery request, CancellationToken ct)
    {
        if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<PaymentSettingsDto>.Forbidden();

        var municipality = await context.Municipalities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == municipalityId, ct);
        if (municipality is null)
            return Result<PaymentSettingsDto>.NotFound();

        return Result<PaymentSettingsDto>.Success(
            new PaymentSettingsDto(municipality.HasOwnPayMongoAccount, municipality.PayMongoPublicKey));
    }
}
