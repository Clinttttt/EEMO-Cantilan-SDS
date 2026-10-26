using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

public sealed partial class CollectionSessionSources
{
    /// <summary>
    /// Names an unpaid space month that is Arrears on the collector's choice ("Space 4 · Oct 2024 · Arrears, expired contract"), by the same
    /// rule the office's follow-up uses (<see cref="SpaceArrears"/>). Display only: the choice's key, identity, amount and rules are untouched.
    /// </summary>
    private async Task<CollectionSessionDiscovery> LabelSpaceArrearsAsync(CollectionSessionDiscovery discovery, DateOnly date, CancellationToken ct)
    {
        var ids = discovery.Operations.SelectMany(o => o.Choices ?? [])
            .Where(c => c.Kind == CollectionSessionItemKind.Obligation && c.Identity.AccountId.HasValue && c.Identity.PeriodStart.HasValue)
            .Select(c => c.Identity.AccountId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return discovery;
        var accounts = await db.ObligationAccounts.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)
                && (x.Kind == ObligationKind.KanmanggaySpaceRental || x.Kind == ObligationKind.VegetableFruitSpaceRental || x.Kind == ObligationKind.FiestaArawLotRental))
            .ToDictionaryAsync(x => x.Id, ct);
        if (accounts.Count == 0) return discovery;
        var terms = (await db.SpaceContractTerms.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.ObligationAccountId)).ToListAsync(ct))
            .GroupBy(x => x.ObligationAccountId).ToDictionary(g => g.Key, g => SpaceTermSplit.Ordered(g));

        CollectionSessionSourceChoice Label(CollectionSessionSourceChoice c)
        {
            if (c.Kind != CollectionSessionItemKind.Obligation || c.Identity.AccountId is not { } id || c.Identity.PeriodStart is not { } period
                || !accounts.TryGetValue(id, out var account)) return c;
            var basis = SpaceArrears.Basis(account, terms.GetValueOrDefault(id) ?? [], period, date);
            return basis is null ? c : c with { Context = $"{c.Context} · Arrears, {basis.ToLowerInvariant()}" };
        }

        return discovery with { Operations = discovery.Operations.Select(o => o.Choices is null ? o : o with { Choices = o.Choices.Select(Label).ToArray() }).ToArray() };
    }
}
