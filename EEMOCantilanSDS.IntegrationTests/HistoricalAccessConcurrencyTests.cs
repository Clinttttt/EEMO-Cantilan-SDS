using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>ST-11 / ST-19 review: a single-use historical code redeemed by two simultaneous requests is honoured exactly once.</summary>
public partial class HistoricalArrearsReaderTests
{
    [SkippableFact]
    public async Task Two_simultaneous_activations_of_one_code_create_exactly_one_payor_and_one_link_without_a_server_error()
    {
        var w = await SeedAsync();
        string code;
        await using (var ctx = db.CreateContext(w.TenantId))
            code = (await Access(ctx, new(Guid.NewGuid(), w.TenantId, "SuperAdmin")).IssueAsync(ForStall(w.AnnaId, "Anna Cruz"), default)).Value!.Code;

        async Task<(bool Ok, Exception? Fault)> Redeem(string password)
        {
            await using var ctx = db.CreateContext(w.TenantId);
            try
            {
                var r = await Access(ctx, new(Guid.Empty, w.TenantId, "Payor")).ActivateAsync(new(code, "09170000001", null, password), default);
                return (r.IsSuccess, null);
            }
            catch (Exception ex) { return (false, ex); }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => Redeem($"password{i}1"))));

        await using var check = db.CreateContext(w.TenantId);
        Assert.Equal(1, await check.PayorUsers.CountAsync(x => x.Username == "09170000001"));
        Assert.Equal(1, await check.PayorLiabilityLinks.CountAsync());
        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.All(results.Where(r => !r.Ok), r => Assert.Null(r.Fault));   // losers are refused, never a 500
    }
}
