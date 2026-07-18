using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Municipalities.IssueMobileBindLink;

public class IssueMobileBindLinkCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser) : IRequestHandler<IssueMobileBindLinkCommand, Result<string>>
{
    public async Task<Result<string>> Handle(IssueMobileBindLinkCommand request, CancellationToken ct)
    {
        // A Head may only get their OWN LGU's bind link — the target is the caller's municipality.
        if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<string>.Forbidden();

        var municipality = await context.Municipalities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == municipalityId, ct);
        if (municipality is null)
            return Result<string>.NotFound();

        if (request.Rotate || string.IsNullOrWhiteSpace(municipality.MobileBindToken))
        {
            municipality.SetMobileBindToken(Municipality.GenerateBindToken(), currentUser.Username ?? "Head");
            await context.SaveChangesAsync(ct);
        }

        return Result<string>.Success(municipality.MobileBindToken!);
    }
}
