using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The one authorized run against the local-main WORKING COPY. Skipped in every normal run: it needs STALLTRACK_WORKCOPY_CONNECTION,
/// STALLTRACK_WORKCOPY_TENANT and STALLTRACK_WORKCOPY_FIXTURE=directfine. It refuses unless the connection is the loopback
/// <c>stalltrack_workcopy_main</c> database on port 55491 (never the municipal database, never the accepted preview).
/// </summary>
public sealed class WorkingCopyDirectFineFixtureRun(ITestOutputHelper output)
{
    private sealed class Tenant(Guid id) : EEMOCantilanSDS.Application.Common.Tenancy.ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [SkippableFact]
    public async Task Create_the_synthetic_direct_fine_authority_in_the_working_copy()
    {
        var connection = Environment.GetEnvironmentVariable("STALLTRACK_WORKCOPY_CONNECTION");
        var tenantText = Environment.GetEnvironmentVariable("STALLTRACK_WORKCOPY_TENANT");
        Skip.If(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(tenantText)
            || Environment.GetEnvironmentVariable("STALLTRACK_WORKCOPY_FIXTURE") != "directfine", "Working-copy fixture not requested.");
        var tenant = Guid.Parse(tenantText!);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor()).Options;
        await using var context = new AppDbContext(options, new Tenant(tenant));

        var code = Environment.GetEnvironmentVariable("STALLTRACK_WORKCOPY_PENALTY") ?? "LATE_PAYMENT";
        var outcome = await WorkingCopyDirectFineFixture.EnsureAsync(context, connection!, "stalltrack_workcopy_main", 55491, tenant, code, PhilippineTime.Today);
        output.WriteLine($"authority={outcome.AuthorityId} penalty={outcome.PenaltyCode} version={outcome.PenaltyVersionId} created={outcome.Created} " +
            $"valid {outcome.EffectiveFrom:yyyy-MM-dd}..{outcome.EffectiveTo:yyyy-MM-dd}");
    }
}
