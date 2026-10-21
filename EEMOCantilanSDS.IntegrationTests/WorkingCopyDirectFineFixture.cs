using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// An explicitly SYNTHETIC direct-fine authority for a throwaway working copy only, so the Head can record walk-up cash fines (Office-origin
/// canonical SRCs, payer text only, no real payor) through the normal <c>DirectFineCollectionWorkflow</c>. It writes one
/// <see cref="DirectFineAuthority"/> through its domain factory and nothing else: no Collection, SRC, line or allocation is ever written here.
/// It never weakens the workflow: the penalty version must already exist, be active and resolve for today, the authority is time-limited and
/// demands payer particulars only (the Office requires no reason or supporting reference), and the database must be the exact named loopback database. The accepted
/// preview and the retained municipal database are refused by name.
/// </summary>
public static class WorkingCopyDirectFineFixture
{
    public const string Marker = "SYNTHETIC WORKING-COPY FIXTURE";
    public const int ValidDays = 14;

    /// <summary>Not a user: a visibly artificial actor so no real person is recorded as having verified this authority.</summary>
    public static readonly Guid FixtureActor = Guid.Parse("00000000-0000-0000-0000-00000000571c");

    public sealed record Outcome(Guid AuthorityId, Guid PenaltyVersionId, string PenaltyCode, bool Created, DateOnly EffectiveFrom, DateOnly EffectiveTo);

    public static async Task<Outcome> EnsureAsync(AppDbContext context, string connectionString, string expectedDatabase, int expectedPort,
        Guid tenant, string penaltyCode, DateOnly today, CancellationToken ct = default)
    {
        var refusals = new List<string>(PreviewMonthlyRentGate.DatabaseRefusals(connectionString, expectedDatabase, expectedPort));
        if (string.Equals(expectedDatabase, "stalltrack_preview", StringComparison.OrdinalIgnoreCase) || expectedPort == 55489)
            refusals.Add("The accepted NCC preview never receives synthetic authorities.");
        if (refusals.Count == 0)
        {
            var actual = await context.Database.SqlQuery<string>($"select current_database() as \"Value\"").SingleAsync(ct);
            if (!string.Equals(actual, expectedDatabase, StringComparison.Ordinal)) refusals.Add("The live session is not connected to the named database.");
            if (!await context.Municipalities.AnyAsync(x => x.Id == tenant, ct)) refusals.Add("The tenant does not exist in this database.");
        }
        if (refusals.Count > 0) throw new InvalidOperationException("Refused: " + string.Join(" ", refusals));

        var versions = await context.PenaltyDefinitions.Where(x => x.MunicipalityId == tenant && x.Code == penaltyCode).ToListAsync(ct);
        var version = PenaltyDefinition.Resolve(versions, penaltyCode, today);
        if (version is null || !version.IsActive || version.Basis is not (GovernedServiceBasis.FixedAmount or GovernedServiceBasis.DirectApprovedAmount))
            throw new InvalidOperationException("Refused: the penalty category must exist, be active, resolve for today and allow a direct amount. Nothing was created.");

        var existing = await context.DirectFineAuthorities.AsNoTracking().FirstOrDefaultAsync(x => x.MunicipalityId == tenant
            && x.PenaltyVersionId == version.Id && x.GoverningReference.StartsWith(Marker) && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today), ct);
        if (existing is not null)
            return new(existing.Id, version.Id, penaltyCode, false, existing.EffectiveFrom, existing.EffectiveTo ?? existing.EffectiveFrom);

        // An uncapped authority is required exactly when the category has no ceiling of its own; otherwise the category's ceiling or fixed amount governs.
        var uncapped = version.Basis == GovernedServiceBasis.DirectApprovedAmount && version.MaximumAmount is null;
        var to = today.AddDays(ValidDays - 1);
        var authority = DirectFineAuthority.Record(tenant, version.Id, Marker + ": not an Office-approved authority",
            Marker + ": acceptance testing only, database " + expectedDatabase, today, to, uncapped,
            payerRequired: true, reasonRequired: false, referenceRequired: false, FixtureActor, DateTime.UtcNow);
        context.DirectFineAuthorities.Add(authority);
        await context.SaveChangesAsync(ct);
        return new(authority.Id, version.Id, penaltyCode, true, today, to);
    }
}
