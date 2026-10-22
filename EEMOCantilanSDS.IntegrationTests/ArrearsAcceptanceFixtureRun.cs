using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Run-only fixture: seeds synthetic arrears scenarios into the SYNTHETIC sample municipality of a disposable database so the real API can be
/// read end to end (Arrears page, Financial Report, Follow-up Queue, Whole-time History, dashboard). Skipped in every normal run: it needs
/// STALLTRACK_ARR_CONNECTION/DATABASE/PORT/TENANT, refuses the retained municipal database, the accepted NCC preview, the working copy and the
/// accepted monthly-market preview database, and it writes only stalls, contracts and payment records for that tenant.
/// </summary>
public sealed class ArrearsAcceptanceFixtureRun
{
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static PaymentRecord Paid(Guid stall, int y, int m, decimal rent) { var r = PaymentRecord.Create(stall, y, m, rent, "fixture"); r.UpdateStatus(PaymentStatus.Paid, 0m, updatedBy: "fixture"); return r; }
    private static PaymentRecord Part(Guid stall, int y, int m, decimal rent, decimal paid) { var r = PaymentRecord.Create(stall, y, m, rent, "fixture"); r.UpdateStatus(PaymentStatus.Partial, paid, updatedBy: "fixture"); return r; }

    [SkippableFact]
    public async Task Seed_the_synthetic_arrears_scenarios()
    {
        var connection = Environment.GetEnvironmentVariable("STALLTRACK_ARR_CONNECTION");
        var database = Environment.GetEnvironmentVariable("STALLTRACK_ARR_DATABASE");
        var portText = Environment.GetEnvironmentVariable("STALLTRACK_ARR_PORT");
        var tenantText = Environment.GetEnvironmentVariable("STALLTRACK_ARR_TENANT");
        Skip.If(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(portText) || string.IsNullOrWhiteSpace(tenantText), "Not requested.");
        Assert.DoesNotContain(int.Parse(portText!), new[] { 5432, 55489, 55491 });
        Assert.DoesNotContain(database!, new[] { "EEMOCantilanSDS.DB", "stalltrack_preview", "stalltrack_workcopy_main", "stalltrack_monthly_market" });
        Assert.Equal(database, new NpgsqlConnectionStringBuilder(connection).Database);
        var tenant = Guid.Parse(tenantText!);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor()).Options;
        await using var ctx = new AppDbContext(options, new Tenant(tenant));
        Assert.Equal("SAMPLEM", (await new AppDbContext(options, new Tenant(Guid.Empty)).Municipalities.AsNoTracking().SingleAsync(m => m.Id == tenant)).Code);
        Skip.If(await ctx.Stalls.AnyAsync(s => s.StallNo.StartsWith("ARR-")), "Already seeded.");

        var tcc = await ctx.Facilities.FirstOrDefaultAsync(f => f.Code == FacilityCode.TCC);
        if (tcc is null) { tcc = Facility.Create(FacilityCode.TCC, "TCC", "TCC", municipalityId: tenant); ctx.Facilities.Add(tcc); }
        var npm = await ctx.Facilities.SingleAsync(f => f.Code == FacilityCode.NPM);

        // S1 live tenant: Nov25 none, Dec25 400/1000, Jan-Mar26 paid, Apr26 none, May26 300/1000, Jun-Sep26 paid. Arrears 2/1,600; Delinquent 2/1,700.
        var s1 = Stall.Create(tcc.Id, "ARR-1", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        var c1 = Contract.Create(s1.Id, "Fixture Mixed", "Fixture Mixed", new DateOnly(2025, 11, 1), 5, 1_000m);
        ctx.Stalls.Add(s1); ctx.Contracts.Add(c1);
        ctx.PaymentRecords.AddRange(Part(s1.Id, 2025, 12, 1_000m, 400m), Paid(s1.Id, 2026, 1, 1_000m), Paid(s1.Id, 2026, 2, 1_000m), Paid(s1.Id, 2026, 3, 1_000m),
            Part(s1.Id, 2026, 5, 1_000m, 300m), Paid(s1.Id, 2026, 6, 1_000m), Paid(s1.Id, 2026, 7, 1_000m), Paid(s1.Id, 2026, 8, 1_000m), Paid(s1.Id, 2026, 9, 1_000m));

        // S2 expired contract (Oct 2025, 1 year), never paid: every month Arrears.
        var s2 = Stall.Create(tcc.Id, "ARR-2", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        ctx.Stalls.Add(s2); ctx.Contracts.Add(Contract.Create(s2.Id, "Fixture Expired", "Fixture Expired", new DateOnly(2025, 10, 1), 1, 1_000m));

        // S3 re-let: A Jan 2025 -> Mar 2026 (ended, unpaid), B from Apr 2026 (live, unpaid: Delinquent only).
        var s3 = Stall.Create(tcc.Id, "ARR-3", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        ctx.Stalls.Add(s3);
        ctx.Contracts.AddRange(Contract.Create(s3.Id, "Fixture Former", "Fixture Former", new DateOnly(2025, 1, 1), 5, 1_000m), Contract.Create(s3.Id, "Fixture Newcomer", "Fixture Newcomer", new DateOnly(2026, 4, 1), 5, 1_000m));

        // S4 current month only: nothing past October owed -> in no category.
        var s4 = Stall.Create(tcc.Id, "ARR-4", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        ctx.Stalls.Add(s4); ctx.Contracts.Add(Contract.Create(s4.Id, "Fixture Current", "Fixture Current", new DateOnly(2026, 9, 1), 5, 1_000m));
        ctx.PaymentRecords.Add(Paid(s4.Id, 2026, 9, 1_000m));

        // S5 Public Market (monthly archetype): same shape as S1.
        var s5 = Stall.Create(npm.Id, "ARR-5", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        ctx.Stalls.Add(s5); ctx.Contracts.Add(Contract.Create(s5.Id, "Fixture Market", "Fixture Market", new DateOnly(2025, 11, 1), 5, 1_000m));
        ctx.PaymentRecords.AddRange(Part(s5.Id, 2025, 12, 1_000m, 400m), Paid(s5.Id, 2026, 1, 1_000m), Paid(s5.Id, 2026, 2, 1_000m), Paid(s5.Id, 2026, 3, 1_000m),
            Part(s5.Id, 2026, 5, 1_000m, 300m), Paid(s5.Id, 2026, 6, 1_000m), Paid(s5.Id, 2026, 7, 1_000m), Paid(s5.Id, 2026, 8, 1_000m), Paid(s5.Id, 2026, 9, 1_000m));

        await ctx.SaveChangesAsync();
    }
}
