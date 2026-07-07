using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Onboarding.SubmitOnboarding;
using EEMOCantilanSDS.Application.Command.Onboarding.UpdateOnboardingConfig;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraft;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetOnboardingDraftByRequest;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Onboarding
{
    /// <summary>
    /// Stage 2 (Onboarding): the LGU loads/edits/submits its staged draft by secure token (anonymous);
    /// the operator reads a submitted draft. Drafts are inert pre-LGU records — no live data is written.
    /// </summary>
    public class OnboardingDraftHandlerTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        private sealed class FakeCurrentUser(Guid? municipalityId, string? role) : ICurrentUserService
        {
            public bool IsAuthenticated => true;
            public Guid? UserId => Guid.NewGuid();
            public string? Username => "operator";
            public string? Role => role;
            public Guid? CollectorId => null;
            public string? MunicipalityCode => null;
            public Guid? MunicipalityId => municipalityId;
            public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        private static async Task<(Guid requestId, string token)> SeedDraftAsync(
            DbContextOptions<AppDbContext> options, string? config = null, DateTime? expiresAt = null)
        {
            using var seed = new AppDbContext(options);
            var requestId = Guid.NewGuid();
            var token = "tok_" + Guid.NewGuid().ToString("N");
            var draft = OnboardingDraft.Create(requestId, "Madrid", "Surigao del Sur", token, expiresAt ?? DateTime.UtcNow.AddDays(30));
            if (config is not null) draft.UpdateConfig(config, "LGU");
            seed.OnboardingDrafts.Add(draft);
            await seed.SaveChangesAsync();
            return (requestId, token);
        }

        [Fact]
        public async Task GetDraft_ByToken_ReturnsDraft()
        {
            var options = Options();
            var (_, token) = await SeedDraftAsync(options);

            using var ctx = new AppDbContext(options);
            var result = await new GetOnboardingDraftQueryHandler(ctx).Handle(new GetOnboardingDraftQuery(token), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("Madrid", result.Value!.Municipality);
            Assert.False(result.Value!.IsSubmittedForValidation);
        }

        [Fact]
        public async Task GetDraft_Expired_Fails()
        {
            var options = Options();
            var (_, token) = await SeedDraftAsync(options, expiresAt: DateTime.UtcNow.AddDays(-1));

            using var ctx = new AppDbContext(options);
            var result = await new GetOnboardingDraftQueryHandler(ctx).Handle(new GetOnboardingDraftQuery(token), default);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task UpdateConfig_SavesConfig()
        {
            var options = Options();
            var (_, token) = await SeedDraftAsync(options);

            using var ctx = new AppDbContext(options);
            var json = "{\"facilities\":[{\"name\":\"Public Market\"}]}";
            var result = await new UpdateOnboardingConfigCommandHandler(ctx)
                .Handle(new UpdateOnboardingConfigCommand(token, json), default);

            Assert.True(result.IsSuccess);
            Assert.Equal(json, result.Value!.ConfigJson);
        }

        [Fact]
        public async Task Submit_WithoutConfig_Fails_WithConfig_Succeeds()
        {
            var options = Options();
            var (_, emptyToken) = await SeedDraftAsync(options);

            using (var ctx = new AppDbContext(options))
            {
                var empty = await new SubmitOnboardingCommandHandler(ctx).Handle(new SubmitOnboardingCommand(emptyToken), default);
                Assert.False(empty.IsSuccess);
            }

            var (_, token) = await SeedDraftAsync(options, config: "{\"ok\":true}");
            using (var ctx = new AppDbContext(options))
            {
                var ok = await new SubmitOnboardingCommandHandler(ctx).Handle(new SubmitOnboardingCommand(token), default);
                Assert.True(ok.IsSuccess);
                Assert.True(ok.Value!.IsSubmittedForValidation);
                Assert.NotNull(ok.Value!.SubmittedAt);
            }
        }

        [Fact]
        public async Task GetByRequest_OperatorOnly()
        {
            var options = Options();
            Guid cantilanId;
            using (var seed = new AppDbContext(options))
            {
                var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
                seed.Municipalities.Add(cantilan);
                await seed.SaveChangesAsync();
                cantilanId = cantilan.Id;
            }
            var (requestId, _) = await SeedDraftAsync(options, config: "{\"ok\":true}");

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));

            var ok = await new GetOnboardingDraftByRequestQueryHandler(ctx, new FakeCurrentUser(cantilanId, "SuperAdmin"))
                .Handle(new GetOnboardingDraftByRequestQuery(requestId), default);
            Assert.True(ok.IsSuccess);

            var forbidden = await new GetOnboardingDraftByRequestQueryHandler(ctx, new FakeCurrentUser(Guid.NewGuid(), "SuperAdmin"))
                .Handle(new GetOnboardingDraftByRequestQuery(requestId), default);
            Assert.False(forbidden.IsSuccess);
            Assert.Equal(403, forbidden.StatusCode);
        }
    }
}
