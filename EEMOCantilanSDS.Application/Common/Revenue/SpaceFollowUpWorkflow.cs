using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed class SpaceFollowUpWorkflow(IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor tenant)
{
    public async Task<IReadOnlyList<ObligationFollowUpItemDto>> GetAsync(DateOnly asOf, string? operationCode, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin") || tenant.MunicipalityId == Guid.Empty ||
            user.MunicipalityId is { } claimed && claimed != tenant.MunicipalityId) return [];
        var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenant.MunicipalityId &&
            (x.Kind == ObligationKind.KanmanggaySpaceRental || x.Kind == ObligationKind.FiestaArawLotRental)).ToListAsync(ct);
        var byId = accounts.ToDictionary(x => x.Id);
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(tenant.MunicipalityId, accounts, asOf, ct);
        var month = new DateOnly(asOf.Year, asOf.Month, 1);
        return quotes.Where(x => x.OutstandingAmount > 0m && (x.Kind == ObligationKind.KanmanggaySpaceRental ? x.PeriodStart < month : x.PeriodStart < asOf))
            .Select(x =>
            {
                var account = byId[x.AccountId];
                var monthly = x.Kind == ObligationKind.KanmanggaySpaceRental;
                var code = monthly ? CollectorOperationCodes.KanmanggaySpaceRental : CollectorOperationCodes.FiestaArawLotRental;
                return new ObligationFollowUpItemDto(x.AccountId, x.PayorId, x.PayerName, x.SubjectLabel,
                    new(monthly ? FollowUpScopeKind.MonthlySpace : FollowUpScopeKind.EventLot, code, x.KindLabel),
                    x.PeriodStart, x.AssessedAmount, x.SettledAmount, x.OutstandingAmount,
                    monthly && x.PeriodStart <= month.AddMonths(-3) ? "Critical" : "Normal", account.Event,
                    monthly ? "/operations/kanmanggay" : "/operations/fiesta-araw");
            }).Where(x => operationCode is null || x.Scope.OperationCode == operationCode).ToArray();
    }
}
