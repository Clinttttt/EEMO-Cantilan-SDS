using System.Text.Json;
using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The one authorized synthetic monthly-market acceptance preview. Skipped in every normal run: it needs STALLTRACK_MMP_CONNECTION (never
/// logged), STALLTRACK_MMP_DATABASE, STALLTRACK_MMP_PORT and STALLTRACK_MMP_OUT, and refuses the retained municipal database, the accepted NCC
/// preview and the local-main working copy by name and port. It creates two explicitly synthetic LGUs, activates the first through the real
/// activation handler and its stalls through the real cutover lifecycle, and collects nothing. Existing tenants (Cantilan) are only read.
/// </summary>
public sealed class MonthlyMarketPreviewRun(ITestOutputHelper output)
{
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Operator(Guid userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "preview.operator";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => null;
    }

    private sealed class RecordingEmail : IEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string? toName, string subject, string body, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FixedClock : EEMOCantilanSDS.Application.Common.Interface.Time.IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private static string Password() => "Mm!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)) + "x9";

    [SkippableFact]
    public async Task Prepare_the_synthetic_monthly_market_preview()
    {
        var connection = Environment.GetEnvironmentVariable("STALLTRACK_MMP_CONNECTION");
        var database = Environment.GetEnvironmentVariable("STALLTRACK_MMP_DATABASE");
        var portText = Environment.GetEnvironmentVariable("STALLTRACK_MMP_PORT");
        var outPath = Environment.GetEnvironmentVariable("STALLTRACK_MMP_OUT");
        Skip.If(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(portText) || string.IsNullOrWhiteSpace(outPath),
            "Monthly-market preview not requested.");
        var port = int.Parse(portText!);
        // Never the retained municipal database, the accepted NCC preview or the local-main working copy.
        Assert.DoesNotContain(port, new[] { 5432, 55489, 55491 });
        Assert.DoesNotContain(database!, new[] { "EEMOCantilanSDS.DB", "stalltrack_preview", "stalltrack_workcopy_main" });
        Assert.Empty(PreviewMonthlyRentGate.DatabaseRefusals(connection, database!, port));

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor()).Options;
        await using var context = new AppDbContext(options, new Tenant(Guid.Empty));
        Assert.Equal(database, await context.Database.SqlQuery<string>($"select current_database() as \"Value\"").SingleAsync());
        var hasher = new IdentityPasswordHasher();
        var accounts = new Dictionary<string, object>();

        var cantilan = await context.Municipalities.AsNoTracking().SingleAsync(x => x.Code == "CANTILAN");

        // ── A synthetic platform operator, in this disposable database only ────────────────────────────
        var operatorPassword = Password();
        var op = AdminUser.Create("Preview Operator", "preview.operator", "operator@preview.invalid", hasher.Hash(operatorPassword),
            AdminRole.SuperAdmin, cantilan.Id, isPlatformOperator: true, mustChangePassword: false);
        context.AdminUsers.Add(op);

        // ── LGU 1: Sample Monthly Market, activated now through the real activation handler ──────────────────
        var monthly = Municipality.Create("SAMPLEM", "Sample Monthly Market", "Synthetic Province", MunicipalityStatus.Upcoming, tenantCode: "samplem");
        // ── LGU 2: Sample Onboarding, left Upcoming with a validated monthly draft for the Platform Admin activation walk-through ──
        var onboarding = Municipality.Create("SAMPLEO", "Sample Onboarding", "Synthetic Province", MunicipalityStatus.Upcoming, tenantCode: "sampleo");
        context.Municipalities.AddRange(monthly, onboarding);

        AssessmentRequest Request(string name, string email)
        {
            var r = AssessmentRequest.Create(name, "Synthetic Province", $"Municipality of {name}", "Synthetic Focal", "Head of Office",
                email, "09000000000", string.Empty, null, null, true, null);
            r.Approve("https://example.invalid/onboarding/t", null, "preview.operator");
            r.SubmitForValidation("LGU");
            r.ApproveValidation("preview.operator");
            return r;
        }
        var monthlyRequest = Request("Sample Monthly Market", "head@samplem.invalid");
        var onboardingRequest = Request("Sample Onboarding", "head@sampleo.invalid");
        context.AssessmentRequests.AddRange(monthlyRequest, onboardingRequest);

        // The Landing workspace draft for LGU 2, exactly the shape the Landing saves: a Public Market declared as monthly rental.
        var draftJson = JsonSerializer.Serialize(new
        {
            orSeries = "", users = Array.Empty<object>(),
            facilities = new object[]
            {
                new
                {
                    id = "pm1", catalogKey = "public_market", archetype = "DailyStall", name = "Sample Onboarding Public Market", type = "Daily stall",
                    rateAmount = "", monthlyRent = "", monthBasis = "RentGoal", rentalModel = "monthly", rateUnit = "per day", unitLabel = "stalls", units = "",
                    ordinance = "", notes = "", marketDay = "", addOns = Array.Empty<object>(),
                    sections = new object[]
                    {
                        new { id = "s1", name = "Gulayan", kind = "VegetableArea", units = "6", rate = "", fees = Array.Empty<object>() },
                        new { id = "s2", name = "Isda", kind = "FishSection", units = "4", rate = "", fees = new object[] { new { id = "f1", label = "Fish (per kilo)", amount = "6", unit = "per kilo" } } }
                    }
                }
            }
        });
        var draft = OnboardingDraft.Create(onboardingRequest.Id, "Sample Onboarding", "Synthetic Province", "mm-" + Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(30));
        draft.UpdateConfig(draftJson, "LGU");
        draft.SubmitForValidation("LGU");
        context.OnboardingDrafts.Add(draft);
        await context.SaveChangesAsync();

        // ── Activate LGU 1 exactly as Platform Admin would ───────────────────────────────────────────────
        var headPassword = Password();
        var activation = await new ActivateMunicipalityCommandHandler(context, new Operator(op.Id), new RecordingEmail(), hasher).Handle(
            new ActivateMunicipalityCommand("SAMPLEM",
                new ActivationBranding("Sample Monthly Market Economic Enterprise Office", "Sample, Synthetic Province, Philippines", null, "SMMEEO"),
                new ActivationAdministrator("Synthetic Head", "samplem.head", "head@samplem.invalid"),
                [new ActivationFacility(FacilityCode.NPM, "Sample Public Market", "SPM", BillingArchetype.MonthlyRental,
                    SectionLabels: new ActivationSectionLabels("Gulayan", "Isda", "Karne"), CustomSections: ["Bigasan"])],
                [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 6m)],
                AssessmentRequestId: monthlyRequest.Id),
            CancellationToken.None);
        Assert.True(activation.IsSuccess, activation.Error);
        var setPassword = await new SetAdminPasswordByTokenCommandHandler(context, new FixedClock(), hasher)
            .Handle(new SetAdminPasswordByTokenCommand(activation.Value!.ActivationToken, headPassword), CancellationToken.None);
        Assert.True(setPassword.IsSuccess, setPassword.Error);

        // ── Synthetic stalls, occupants, contracts and the current month's obligation (₱ approved rent per contract) ──
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var facility = await context.Facilities.IgnoreQueryFilters().SingleAsync(x => x.MunicipalityId == monthly.Id && x.Code == FacilityCode.NPM);
        Assert.True(facility.IsMonthlyRentMarket);
        var layout = new (string No, string Occupant, decimal Rent, MarketSection? Section, string? Custom)[]
        {
            ("PREVIEW-NPM-01", "Synthetic Vendor One (Gulayan)", 1500m, MarketSection.VegetableArea, null),
            ("PREVIEW-NPM-02", "Synthetic Vendor Two (Isda)", 1800m, MarketSection.FishSection, null),
            ("PREVIEW-NPM-03", "Synthetic Vendor Three (Karne)", 1500m, MarketSection.MeatSection, null),
            ("PREVIEW-NPM-04", "Synthetic Vendor Four (Bigasan)", 1200m, null, "Bigasan"),
            ("PREVIEW-NPM-05", "Synthetic Vendor Five (Gulayan)", 1500m, MarketSection.VegetableArea, null),
            ("PREVIEW-NPM-06", "Synthetic Vendor Six (Isda)", 1800m, MarketSection.FishSection, null),
        };
        await using var seed = new AppDbContext(options, new Tenant(monthly.Id));
        foreach (var (no, occupant, rent, section, custom) in layout)
        {
            var stall = Stall.Create(facility.Id, no, rent, ApplicableFees.BaseRental, section, municipalityId: monthly.Id, customSectionName: custom);
            seed.Add(stall);
            seed.Add(Contract.Create(stall.Id, occupant, occupant, monthStart, 5, rent, createdBy: "preview-seed"));
            seed.PaymentRecords.Add(PaymentRecord.Create(stall.Id, today.Year, today.Month, rent, "preview-seed"));
        }
        // The rent classification and its Official Receipt policy: what a Head approves in Revenue Setup before rent can be collected canonically.
        var rentClassification = EEMOCantilanSDS.Domain.Entities.Revenue.RevenueClassification.Create(EEMOCantilanSDS.Domain.Constants.RevenueClassificationCodes.PermanentStallRent, monthly.Id);
        seed.AddRange(rentClassification, EEMOCantilanSDS.Domain.Entities.Revenue.RevenueClassificationPolicy.Create(rentClassification.Id,
            new DateOnly(2000, 1, 1), "Permanent Stall Rent", RevenueInstrumentType.OfficialReceipt, monthly.Id));
        await seed.SaveChangesAsync();

        // The real cutover lifecycle for each fresh, unpaid, zero-cash synthetic month (the tool refuses anything else).
        var target = new PreviewMonthlyRentTarget(database!, port, monthly.Id, FacilityCode.NPM);
        var head = await context.AdminUsers.IgnoreQueryFilters().Where(x => x.MunicipalityId == monthly.Id && !x.IsDeleted).Select(x => x.Id).SingleAsync();
        var activated = new List<object>();
        foreach (var (no, _, _, _, _) in layout)
        {
            await using var scoped = new AppDbContext(options, new Tenant(monthly.Id));
            var outcome = await PreviewRentActivation.RunAsync(scoped, target, connection!, head, today.Year, today.Month, no);
            activated.Add(new { stall = no, outcome.Authority, outcome.SourceVersion });
        }

        // ── A synthetic collector, assigned only AFTER the cutovers (queue-drain evidence is a human attestation the tool cannot make) ──
        var collectorPassword = Password();
        var collector = CollectorUser.Create("Synthetic Collector", "SMM-COL-1", "samplem.collector1", null, null, hasher.Hash(collectorPassword), monthly.Id);
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
        seed.Add(collector);
        await seed.SaveChangesAsync();

        accounts["operator"] = new { username = "preview.operator", password = operatorPassword };
        accounts["monthlyHead"] = new { username = "samplem.head", password = headPassword, tenant = "SAMPLEM", tenantId = monthly.Id };
        accounts["monthlyCollector"] = new { username = "samplem.collector1", password = collectorPassword, tenant = "SAMPLEM" };
        accounts["onboardingLgu"] = new { code = "SAMPLEO", requestId = onboardingRequest.Id, draftToken = draft.Token, tenantId = onboarding.Id };
        accounts["stalls"] = layout.Select(x => new { x.No, x.Occupant, x.Rent }).ToArray();
        accounts["cutover"] = activated;
        accounts["monthStart"] = monthStart.ToString("yyyy-MM-dd");
        await File.WriteAllTextAsync(outPath!, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"prepared monthly={monthly.Id} onboarding={onboarding.Id} stalls={layout.Length}");
    }
}
