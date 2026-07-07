using System;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Onboarding.ApproveAssessmentRequest;
using EEMOCantilanSDS.Application.Command.Onboarding.DeclineAssessmentRequest;
using EEMOCantilanSDS.Application.Command.Onboarding.SubmitAssessmentRequest;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetAssessmentRequests;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Onboarding
{
    /// <summary>
    /// Stage 1 (Assessment): public submission creates an inert pending request; operator review
    /// (approve/decline/list) is gated to the platform operator (default-LGU SuperAdmin). No live LGU
    /// data is touched.
    /// </summary>
    public class AssessmentRequestHandlerTests
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

        private static async Task<Guid> SeedDefaultAsync(DbContextOptions<AppDbContext> options)
        {
            using var seed = new AppDbContext(options);
            var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true);
            seed.Municipalities.Add(cantilan);
            await seed.SaveChangesAsync();
            return cantilan.Id;
        }

        private static SubmitAssessmentRequestCommand SampleSubmit() => new(
            "Madrid", "Surigao del Sur", "Local Economic Enterprise Office (LEEO)",
            "Clint Villanueva", "LEEO Officer", "leeo.madrid@example.gov.ph", "0912 345 6789",
            "Public Market — daily stalls, Slaughterhouse", "~180", "In process", true, null);

        [Fact]
        public async Task Submit_CreatesPendingRequest()
        {
            var options = Options();
            using var ctx = new AppDbContext(options);

            var result = await new SubmitAssessmentRequestCommandHandler(ctx).Handle(SampleSubmit(), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("PendingReview", result.Value!.Status);
            Assert.Equal("Assessment", result.Value!.Stage);
            Assert.Equal(1, await ctx.AssessmentRequests.CountAsync());
        }

        [Fact]
        public async Task Approve_ByOperator_MovesToOnboarding()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid requestId;
            using (var seed = new AppDbContext(options))
            {
                var r = AssessmentRequest.Create("Madrid", "Surigao del Sur", "LEEO", "C V", "Officer", "a@b.gov.ph", "0912", "Public Market", null, null, true, null);
                seed.AssessmentRequests.Add(r);
                await seed.SaveChangesAsync();
                requestId = r.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var op = new FakeCurrentUser(cantilanId, "SuperAdmin");
            var result = await new ApproveAssessmentRequestCommandHandler(ctx, op)
                .Handle(new ApproveAssessmentRequestCommand(requestId, "https://stalltrack.site/onboarding/madrid-abc123", "Welcome"), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("Approved", result.Value!.Status);
            Assert.Equal("Onboarding", result.Value!.Stage);
            Assert.Equal("https://stalltrack.site/onboarding/madrid-abc123", result.Value!.OnboardingLink);
        }

        [Fact]
        public async Task Approve_ByNonOperator_IsForbidden()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid requestId;
            using (var seed = new AppDbContext(options))
            {
                var r = AssessmentRequest.Create("Madrid", "Surigao del Sur", "LEEO", "C V", "Officer", "a@b.gov.ph", "0912", "Public Market", null, null, true, null);
                seed.AssessmentRequests.Add(r);
                await seed.SaveChangesAsync();
                requestId = r.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            // A SuperAdmin of a DIFFERENT municipality is not the platform operator.
            var notOperator = new FakeCurrentUser(Guid.NewGuid(), "SuperAdmin");
            var result = await new ApproveAssessmentRequestCommandHandler(ctx, notOperator)
                .Handle(new ApproveAssessmentRequestCommand(requestId, "https://x", null), default);

            Assert.False(result.IsSuccess);
            Assert.Equal(403, result.StatusCode);
        }

        [Fact]
        public async Task Decline_ByOperator_MarksDeclined()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            Guid requestId;
            using (var seed = new AppDbContext(options))
            {
                var r = AssessmentRequest.Create("Lanuza", "Surigao del Sur", "Mayor", "A C", "AO", "a@l.gov.ph", "0920", "Public Market", null, null, false, null);
                seed.AssessmentRequests.Add(r);
                await seed.SaveChangesAsync();
                requestId = r.Id;
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));
            var op = new FakeCurrentUser(cantilanId, "SuperAdmin");
            var result = await new DeclineAssessmentRequestCommandHandler(ctx, op)
                .Handle(new DeclineAssessmentRequestCommand(requestId, "Not yet authorized."), default);

            Assert.True(result.IsSuccess);
            Assert.Equal("Declined", result.Value!.Status);
            Assert.Equal("Not yet authorized.", result.Value!.DecisionMessage);
        }

        [Fact]
        public async Task GetAll_ByOperator_ReturnsRequests_NonOperatorForbidden()
        {
            var options = Options();
            var cantilanId = await SeedDefaultAsync(options);
            using (var seed = new AppDbContext(options))
            {
                seed.AssessmentRequests.Add(AssessmentRequest.Create("Madrid", "SDS", "LEEO", "A", "O", "a@b.gov.ph", "0912", "Market", null, null, true, null));
                await seed.SaveChangesAsync();
            }

            using var ctx = new AppDbContext(options, new FixedMunicipality(cantilanId));

            var okResult = await new GetAssessmentRequestsQueryHandler(ctx, new FakeCurrentUser(cantilanId, "SuperAdmin"))
                .Handle(new GetAssessmentRequestsQuery(), default);
            Assert.True(okResult.IsSuccess);
            Assert.Single(okResult.Value!);

            var forbidden = await new GetAssessmentRequestsQueryHandler(ctx, new FakeCurrentUser(cantilanId, "Admin"))
                .Handle(new GetAssessmentRequestsQuery(), default);
            Assert.False(forbidden.IsSuccess);
            Assert.Equal(403, forbidden.StatusCode);
        }
    }
}
