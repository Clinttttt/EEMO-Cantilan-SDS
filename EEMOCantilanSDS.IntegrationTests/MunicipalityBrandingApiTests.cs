using System.Net;
using System.Net.Http.Json;
using EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MunicipalityBrandingApiTests(PostgresFixture database)
{
    private const string Seal = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==";

    private async Task<(Municipality Default, Municipality Other)> SeedAsync()
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "");
        await database.ResetAsync();
        var baseline = Municipality.Create("CANTILAN", "Cantilan", "Test", MunicipalityStatus.Active,
            tenantCode: "cantilan-sds", officeAcronym: "MEEDO", isDefault: true);
        var other = Municipality.Create("CARRASCAL", "Carrascal", "Test", MunicipalityStatus.Active,
            tenantCode: "carrascal", officeName: "Carrascal office", officeAcronym: "CLEE", sealPath: Seal);
        await using var db = database.CreateContext(Guid.Empty);
        db.Municipalities.AddRange(baseline, other);
        await db.SaveChangesAsync();
        return (baseline, other);
    }

    [SkippableFact]
    public async Task AnonymousCurrentBrandingIsUnauthorized_WhilePublicRegistryBrandingAndSealRemainAvailable()
    {
        var offices = await SeedAsync();
        await using var host = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), offices.Other.Id);
        host.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/municipalities/current/branding")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/municipalities")).StatusCode);
        var branding = await host.Client.GetFromJsonAsync<MunicipalityBrandingDto>("/api/municipalities/carrascal/branding", CollectorApiHost.Json);
        Assert.Equal("CLEE", branding!.OfficeAcronym);
        var seal = await host.Client.GetAsync(branding.SealPath);
        Assert.Equal(HttpStatusCode.OK, seal.StatusCode);
        Assert.Equal("image/png", seal.Content.Headers.ContentType?.MediaType);
    }

    [SkippableTheory]
    [InlineData("SuperAdmin")]
    [InlineData("Admin")]
    [InlineData("Collector")]
    public async Task CurrentBrandingUsesTheAuthenticatedMunicipalityId_NotTheDefaultOrOtherIdentifiers(string role)
    {
        var offices = await SeedAsync();
        // Host deliberately carries the legacy string municipality claim "scratch"; the stable ID must win.
        await using var host = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), offices.Other.Id, role: role);
        var branding = await host.Client.GetFromJsonAsync<MunicipalityBrandingDto>(
            $"/api/municipalities/current/branding?municipalityId={offices.Default.Id}&tenant=cantilan-sds", CollectorApiHost.Json);
        Assert.Equal("CARRASCAL", branding!.Code);
        Assert.Equal("CLEE", branding.OfficeAcronym);
        Assert.Contains("/carrascal/seal?v=", branding.SealPath);
        Assert.False(branding.IsDefault);
    }

    [SkippableFact]
    public async Task MissingAuthenticatedMunicipalityIsForbidden_UnknownMunicipalityIsNotFound()
    {
        await SeedAsync();
        await using var missing = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), Guid.Empty);
        Assert.Equal(HttpStatusCode.Forbidden, (await missing.Client.GetAsync("/api/municipalities/current/branding")).StatusCode);
        await using var unknown = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await unknown.Client.GetAsync("/api/municipalities/current/branding")).StatusCode);
    }

    [SkippableFact]
    public async Task ProfileSaveAndBrandingRefreshStayWithinTheAuthenticatedOffice()
    {
        var offices = await SeedAsync();
        await using var host = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), offices.Other.Id, role: "SuperAdmin");
        var saved = await host.Client.PutAsJsonAsync("/api/municipality-profile",
            new UpdateOfficeProfileCommand("Updated Carrascal office", "Test address", Seal, "NEW"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var updated = await host.Client.GetFromJsonAsync<MunicipalityBrandingDto>("/api/municipalities/current/branding", CollectorApiHost.Json);
        Assert.Equal("NEW", updated!.OfficeAcronym);
        Assert.Equal("Updated Carrascal office", updated.OfficeName);
        Assert.Contains("/carrascal/seal?v=", updated.SealPath);
        await using var db = database.CreateContext(offices.Other.Id);
        Assert.Equal("MEEDO", (await db.Municipalities.SingleAsync(x => x.Id == offices.Default.Id)).OfficeAcronym);
        Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.CollectionAllocations.ToListAsync());
    }
}
