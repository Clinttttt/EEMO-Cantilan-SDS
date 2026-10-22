using EEMOCantilanSDS.Api.Controllers;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class TerminalSectionsControllerTests
{
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid tenantId, string role = "SuperAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "head";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => tenantId;
    }

    [Fact]
    public async Task FirstEditAdoptsOnce_SubsequentEditPreservesOtherChoices_StaleEditConflicts()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("terminal-settings-" + Guid.NewGuid()).Options;
        await using var db = new AppDbContext(options, new Tenant(tenantId));
        var controller = new TerminalSectionsController(db, new Caller(tenantId), new Tenant(tenantId));

        var first = await controller.UpdateAsync(new TerminalSectionUpdateRequest(
            TerminalSection.ComfortRoom, false, "Public comfort room", 0), default);
        Assert.IsType<OkObjectResult>(first);
        var afterFirst = await TerminalSectionSettingsStore.ReadAsync(db, tenantId, default);
        Assert.Equal(3, afterFirst.Count);
        Assert.False(afterFirst.Single(x => x.Section == TerminalSection.ComfortRoom).IsEnabled);
        Assert.True(afterFirst.Single(x => x.Section == TerminalSection.Tricycad).IsEnabled);

        var edit = await controller.UpdateAsync(new TerminalSectionUpdateRequest(
            TerminalSection.Tricycad, false, "Local tricycle terminal", 1), default);
        Assert.IsType<OkObjectResult>(edit);
        var afterSecond = await TerminalSectionSettingsStore.ReadAsync(db, tenantId, default);
        Assert.False(afterSecond.Single(x => x.Section == TerminalSection.ComfortRoom).IsEnabled);
        Assert.False(afterSecond.Single(x => x.Section == TerminalSection.Tricycad).IsEnabled);

        var stale = await controller.UpdateAsync(new TerminalSectionUpdateRequest(
            TerminalSection.ComfortRoom, true, null, 1), default);
        Assert.IsType<ConflictObjectResult>(stale);
        Assert.False((await TerminalSectionSettingsStore.ReadAsync(db, tenantId, default))
            .Single(x => x.Section == TerminalSection.ComfortRoom).IsEnabled);

        var wrongRole = new TerminalSectionsController(db, new Caller(tenantId, "Admin"), new Tenant(tenantId));
        Assert.IsType<ForbidResult>(await wrongRole.UpdateAsync(new TerminalSectionUpdateRequest(
            TerminalSection.ComfortRoom, true, null, 2), default));

        var mismatch = new TerminalSectionsController(db, new Caller(tenantId), new Tenant(Guid.NewGuid()));
        Assert.IsType<ForbidResult>(await mismatch.GetAsync(default));
        Assert.IsType<ForbidResult>(await mismatch.UpdateAsync(new TerminalSectionUpdateRequest(
            TerminalSection.ComfortRoom, true, null, 2), default));
    }

    [Fact]
    public async Task DifferentMunicipalityNeverReadsOrReenablesOtherMunicipalitySections()
    {
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("terminal-tenant-" + Guid.NewGuid()).Options;

        await using (var writer = new AppDbContext(options, new Tenant(firstTenant)))
        {
            writer.TerminalSectionSettings.Add(TerminalSectionSetting.Create(
                TerminalSection.Tricycad, false, "First LGU only", firstTenant, "Activation"));
            await writer.SaveChangesAsync();
        }
        await using var other = new AppDbContext(options, new Tenant(secondTenant));
        var views = await TerminalSectionSettingsStore.ReadAsync(other, secondTenant, default);
        Assert.Equal(3, views.Count);
        Assert.DoesNotContain(views, x => x.DisplayName == "First LGU only");
        Assert.True(await TerminalSectionSettingsStore.AllowsAsync(other, secondTenant, TerminalSection.Tricycad, default));
        Assert.False(await TerminalSectionSettingsStore.AllowsAsync(other, secondTenant, (TerminalSection)99, default));
    }
}
