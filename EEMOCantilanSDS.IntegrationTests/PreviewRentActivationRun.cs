using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The one authorized localhost preview run. Skipped in every normal run: it needs STALLTRACK_PREVIEW_CONNECTION (never
/// logged) and STALLTRACK_PREVIEW_TENANT, and PreviewRentActivation refuses unless the connection is the loopback
/// <c>stalltrack_preview</c> database on port 55489. It activates one synthetic NCC month and collects nothing.
/// </summary>
public sealed class PreviewRentActivationRun(ITestOutputHelper output)
{
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [SkippableFact]
    public async Task Activate_the_synthetic_NCC_month_in_the_authorized_preview()
    {
        var connection = Environment.GetEnvironmentVariable("STALLTRACK_PREVIEW_CONNECTION");
        var tenantText = Environment.GetEnvironmentVariable("STALLTRACK_PREVIEW_TENANT");
        Skip.If(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(tenantText), "Preview activation not requested.");
        var tenant = Guid.Parse(tenantText!);
        var builder = new NpgsqlConnectionStringBuilder(connection);
        // The accepted preview by default; the local-main working copy is named explicitly. The gate still refuses the municipal database.
        var database = Environment.GetEnvironmentVariable("STALLTRACK_PREVIEW_DATABASE") ?? "stalltrack_preview";
        var port = int.Parse(Environment.GetEnvironmentVariable("STALLTRACK_PREVIEW_PORT") ?? "55489");
        var target = new PreviewMonthlyRentTarget(database, port, tenant, FacilityCode.NCC);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection)
            .AddInterceptors(new MunicipalityStampInterceptor()).Options;
        await using var context = new AppDbContext(options, new Tenant(tenant));
        var head = await context.AdminUsers.Where(x => x.MunicipalityId == tenant && !x.IsDeleted).Select(x => x.Id).FirstAsync();

        var stall = Environment.GetEnvironmentVariable("STALLTRACK_PREVIEW_STALL");
        Skip.If(string.IsNullOrWhiteSpace(stall), "Name the permitted synthetic stall in STALLTRACK_PREVIEW_STALL.");
        var today = PhilippineTime.Today;
        var outcome = await PreviewRentActivation.RunAsync(context, target, connection!, head, today.Year, today.Month, stall!);
        output.WriteLine($"db={builder.Database}:{builder.Port} stall={stall} " +
            $"stallId={outcome.StallId} record={outcome.PaymentRecordId} authority={outcome.Authority} version={outcome.SourceVersion}");
    }
}
