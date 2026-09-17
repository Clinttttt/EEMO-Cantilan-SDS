using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.SetSlaughterAnimalLabels;

public sealed class SetSlaughterAnimalLabelsCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext)
    : IRequestHandler<SetSlaughterAnimalLabelsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetSlaughterAnimalLabelsCommand request, CancellationToken ct)
    {
        if (currentUser.Role != "SuperAdmin") return Result<bool>.Forbidden();

        var desired = new Dictionary<AnimalType, string>
        {
            [AnimalType.Hog] = request.Hog.Trim(),
            [AnimalType.Carabao] = request.Carabao.Trim(),
            [AnimalType.Cow] = request.Cow.Trim(),
        };
        var existing = await context.SlaughterAnimalLabels.ToDictionaryAsync(x => x.AnimalType, ct);
        var actor = currentUser.Username ?? "Head";

        foreach (var (type, label) in desired)
        {
            if (existing.TryGetValue(type, out var row))
                row.Rename(label, actor);
            else if (!string.Equals(label, SlaughterAnimalNames.Canonical(type), StringComparison.Ordinal))
                context.SlaughterAnimalLabels.Add(SlaughterAnimalLabel.Create(type, label, createdBy: actor));
        }

        await context.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateTenantAsync(tenantContext.TenantCode, ct);
        return Result<bool>.Success(true);
    }
}
