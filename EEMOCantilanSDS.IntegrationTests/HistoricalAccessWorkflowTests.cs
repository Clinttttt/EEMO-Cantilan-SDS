using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// ST-19: verified access to historical accounts. A code is bound to the ORIGINAL contract (stall) or obligation account (space), is tenant-bound,
/// single-use and expiring, is redeemed only with the number it was issued for, and shows the payor only their OWN unpaid obligations, read-only.
/// </summary>
public partial class HistoricalArrearsReaderTests
{
    private sealed class FakeTokens : ITokenService
    {
        public Task<TokenResponseDto> CreateTokenResponse(BaseUser user) => Task.FromResult(new TokenResponseDto { AccessToken = "t", RefreshToken = "r" });
        public string CreateAccessToken(BaseUser user) => "t";
        public Task<BaseUser> ValidateRefreshToken(string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FakeHasher : IPasswordHasher
    {
        public HashedPassword Hash(string password) => new("hash:" + password);
        public PasswordCheck Check(string hash, string password) => throw new NotSupportedException();
    }

    private HistoricalAccessWorkflow Access(AppDbContext ctx, Caller caller, Guid? requestTenant = null)
    {
        var tenant = new Tenant(requestTenant ?? caller.TenantId);
        var clock = new FixedClock(Now);
        return new(ctx, caller, tenant, Reader(ctx, caller), clock, new RequestTenantScope(), new MunicipalityRepository(ctx), new FakeTokens(), new FakeHasher(), new PaymentRepository(ctx));
    }

    private static IssueHistoricalAccessRequest ForStall(Guid contractId, string payer, string phone = "09170000001") => new(ArrearsAccountKind.StallRent, contractId, phone, payer, null);

    [SkippableFact]
    public async Task A_code_for_the_original_contract_activates_a_payor_who_sees_only_that_contracts_debt_and_a_successor_sees_nothing_of_it()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var head = Access(ctx, new(Guid.NewGuid(), w.TenantId, "SuperAdmin"));
        var issued = (await head.IssueAsync(ForStall(w.AnnaId, "Anna Cruz"), default)).Value!;
        Assert.Equal("Activation", issued.Mode);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", issued.Code);

        // Anna activates with her code and number: a payor account exists, linked to her contract (not to the stall).
        var anon = Access(ctx, new(Guid.Empty, w.TenantId, "Payor"));
        var activated = await anon.ActivateAsync(new(issued.Code, "09170000001", null, "password1"), default);
        Assert.True(activated.IsSuccess, activated.Error);
        var payor = await ctx.PayorUsers.SingleAsync(x => x.Username == "09170000001");
        Assert.Equal("Anna Cruz", payor.FullName);                                 // the office's record, never typed
        var link = await ctx.PayorLiabilityLinks.SingleAsync();
        Assert.Equal((w.AnnaId, null), (link.ContractId, link.ObligationAccountId));
        Assert.Empty(await ctx.PayorStallLinks.ToListAsync());                      // no stall link: nothing on the stall's current lessee

        var mine = (await Access(ctx, new(payor.Id, w.TenantId, "Payor")).GetMineAsync(default)).Value!;
        Assert.Equal(15, mine.Count);                                               // Jan 2025 - Mar 2026, one row per unpaid month
        Assert.Equal(15_000m, mine.Sum(r => r.Balance));
        Assert.All(mine, r => Assert.Equal(("StallRent", "Re-let", "PayAtOfficeOrCollector", false), (r.Kind, r.Status, r.Channel, r.PayableOnline)));
        Assert.Equal("2025-01", mine.First().Period);

        // It reconciles with the Collector Mobile / Head arrears total for the same account.
        var arrears = (await Reader(ctx, new(w.CollectorId, w.TenantId)).GetForCollectorAsync(default)).Accounts.Single(a => a.ContractId == w.AnnaId);
        Assert.Equal(arrears.Total, mine.Sum(r => r.Balance));

        // The code is single-use.
        var replay = await anon.ActivateAsync(new(issued.Code, "09170000001", null, "password1"), default);
        Assert.False(replay.IsSuccess);
        Assert.Equal(1, await ctx.PayorUsers.CountAsync(x => x.Username == "09170000001"));
    }

    [SkippableFact]
    public async Task An_existing_payor_links_a_second_account_through_the_signed_in_flow_and_is_never_merged_by_number()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var head = Access(ctx, new(Guid.NewGuid(), w.TenantId, "SuperAdmin"));
        var first = (await head.IssueAsync(ForStall(w.AnnaId, "Anna Cruz"), default)).Value!;
        await Access(ctx, new(Guid.Empty, w.TenantId, "Payor")).ActivateAsync(new(first.Code, "09170000001", null, "password1"), default);
        var payor = await ctx.PayorUsers.SingleAsync();

        // The same number now holds an account: a space code is a LINK code, and activating with it is refused rather than merged.
        var spaceAccount = (await Reader(ctx, new(w.CollectorId, w.TenantId)).GetForCollectorAsync(default)).Accounts.Single(a => a.Kind == ArrearsAccountKind.SpaceRental);
        var link = (await head.IssueAsync(new(ArrearsAccountKind.SpaceRental, w.SpaceAccountId, "09170000001", spaceAccount.Payer, null), default)).Value!;
        Assert.Equal("Link", link.Mode);
        var asAnon = await Access(ctx, new(Guid.Empty, w.TenantId, "Payor")).ActivateAsync(new(link.Code, "09170000001", null, "password1"), default);
        Assert.Equal(ResultStatus.Conflict, asAnon.Status);

        var me = Access(ctx, new(payor.Id, w.TenantId, "Payor"));
        Assert.True((await me.LinkAsync(new(link.Code), default)).IsSuccess);
        Assert.Equal(1, await ctx.PayorUsers.CountAsync());                          // still one payor
        Assert.Equal(2, await ctx.PayorLiabilityLinks.CountAsync());
        var mine = (await me.GetMineAsync(default)).Value!;
        Assert.Equal(15_000m + 6_000m, mine.Sum(r => r.Balance));
        Assert.Equal(["contract", "space"], mine.Select(r => r.AccountKey.Split(':')[0]).Distinct().OrderBy(x => x));
        Assert.False((await me.LinkAsync(new(link.Code), default)).IsSuccess);        // single use
    }

    [SkippableFact]
    public async Task Codes_are_refused_for_a_wrong_number_an_expired_code_another_tenant_an_unassigned_collector_and_a_stale_payer()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var head = Access(ctx, new(Guid.NewGuid(), w.TenantId, "SuperAdmin"));
        var issued = (await head.IssueAsync(ForStall(w.AnnaId, "Anna Cruz"), default)).Value!;
        var anon = Access(ctx, new(Guid.Empty, w.TenantId, "Payor"));
        Assert.False((await anon.ActivateAsync(new(issued.Code, "09179999999", null, "password1"), default)).IsSuccess);   // issued for another number
        Assert.False((await anon.ActivateAsync(new(issued.Code, "09170000001", null, "short"), default)).IsSuccess);        // weak password
        Assert.Empty(await ctx.PayorUsers.ToListAsync());

        // A payor of another tenant cannot link this tenant's code.
        var other = await SeedOtherTenantPayorAsync("09170000001");
        await using (var otherCtx = db.CreateContext(other.TenantId))
            Assert.False((await Access(otherCtx, new(other.PayorId, other.TenantId, "Payor")).LinkAsync(new(issued.Code), default)).IsSuccess);

        // Expired: a code past its date cannot be redeemed even with the right number.
        var expired = PayorHistoricalAccessCode.Create("EXPD-2345", "09170000002", "Anna Cruz", w.AnnaId, null, null, DateTime.UtcNow.AddMinutes(-1), "head");
        ctx.Add(expired); await ctx.SaveChangesAsync();
        Assert.False((await anon.ActivateAsync(new(expired.Code, "09170000002", null, "password1"), default)).IsSuccess);

        // The payer must be confirmed; a mistaken or stale row is refused.
        var stale = await head.IssueAsync(ForStall(w.AnnaId, "Somebody Else"), default);
        Assert.Equal(ResultStatus.Conflict, stale.Status);

        // A collector can issue only for accounts they may see: with the facility unassigned, the contract is not theirs to issue for.
        ctx.RemoveRange(await ctx.CollectorFacilityAssignments.Where(x => x.CollectorId == w.CollectorId).ToListAsync());
        await ctx.SaveChangesAsync();
        var collector = Access(ctx, new(w.CollectorId, w.TenantId));
        Assert.Equal(ResultStatus.NotFound, (await collector.IssueAsync(ForStall(w.AnnaId, "Anna Cruz"), default)).Status);
        // A collector token from another tenant is refused outright.
        Assert.Equal(ResultStatus.Forbidden, (await Access(ctx, new(w.CollectorId, Guid.NewGuid()), w.TenantId).IssueAsync(ForStall(w.AnnaId, "Anna Cruz"), default)).Status);
    }

    [SkippableFact]
    public async Task The_successors_own_debt_is_a_separate_account_and_its_payor_never_sees_the_predecessors()
    {
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var head = Access(ctx, new(Guid.NewGuid(), w.TenantId, "SuperAdmin"));
        var ben = (await head.IssueAsync(ForStall(w.BenId, "Ben Lopez", "09170000003"), default)).Value!;
        await Access(ctx, new(Guid.Empty, w.TenantId, "Payor")).ActivateAsync(new(ben.Code, "09170000003", null, "password1"), default);
        var payor = await ctx.PayorUsers.SingleAsync();
        var mine = (await Access(ctx, new(payor.Id, w.TenantId, "Payor")).GetMineAsync(default)).Value!;
        Assert.Equal(6, mine.Count);                                                 // Apr - Sep 2026 only
        Assert.Equal(6_000m, mine.Sum(r => r.Balance));
        Assert.DoesNotContain(mine, r => string.CompareOrdinal(r.Period, "2026-04") < 0);   // none of Anna's 2025 - Mar 2026 months
    }

    private async Task<(Guid TenantId, Guid PayorId)> SeedOtherTenantPayorAsync(string phone)
    {
        var code = "OTH-" + Guid.NewGuid().ToString("N")[..8];
        var municipality = Municipality.Create(code, "Other", "Province", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(municipality.Id);
        var payor = PayorUser.Create("Other Payor", phone + "9", new FakeHasher().Hash("password1"));
        ctx.Add(payor); await ctx.SaveChangesAsync();
        return (municipality.Id, payor.Id);
    }
}
