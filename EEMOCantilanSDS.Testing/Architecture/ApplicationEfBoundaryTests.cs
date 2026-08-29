using System.Text;

namespace EEMOCantilanSDS.Testing.Architecture;

/// <summary>
/// Where Entity Framework is allowed to appear in the Application layer.
///
/// <para>
/// The architecture review asked for "Application free of EF", to be reached by replacing <c>IAppDbContext</c> feature by feature.
/// That was measured on 2026-08-17 before starting, and the measurement changed the plan. What is asserted here instead is the
/// BOUNDARY: EF sits in 38 of Application's 790 files, and it may not spread to a 39th without somebody deciding to let it.
/// </para>
///
/// <para>
/// Why not the conversion. <c>IAppDbContext</c> exposes <c>DbSet&lt;T&gt;</c> to 35 handlers, and the obvious move — point each one at
/// the repository interface that already exists — is NOT a swap. Take the clearest case: <c>GetMyOfficeProfileQueryHandler</c> reads
/// <c>context.Municipalities.IgnoreQueryFilters().FirstOrDefaultAsync(...)</c>, and <c>IMunicipalityRepository.GetByIdAsync</c> reads
/// <c>context.Municipalities.AsNoTracking().FirstOrDefaultAsync(...)</c>. They differ on the query filter, and
/// <c>Municipality</c> IS soft-deletable, so the filter is real: the handler finds a soft-deleted municipality and the repository does
/// not. Swapping them would quietly turn a loaded office profile into a 404.
/// </para>
///
/// <para>
/// Every one of the 35 needs that comparison made, and they cluster in auth, account recovery and onboarding — the paths where a silent
/// behaviour change is worst and least visible. Set against a benefit that is architectural rather than behavioural, the conversion is
/// not worth doing as a sweep. It remains worth doing per feature, when a feature is being changed anyway and its queries are being read
/// properly.
/// </para>
///
/// <para>
/// So this test does the part that carries the value with none of the risk: it stops the boundary moving. A new Application file
/// reaching for EF fails the build until someone adds it here deliberately.
/// </para>
/// </summary>
public class ApplicationEfBoundaryTests
{
    /// <summary>
    /// The Application files permitted to use Entity Framework, grouped by why. Adding a name here says the choice was made
    /// deliberately; it is not a place to park a build failure.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // The seam itself, and the paging helper built on IQueryable.
        "IAppDbContext.cs",
        "PaginationExtensions.cs",

        // Onboarding and assessment: the operator pipeline from request to activation.
        "SubmitOnboardingCommandHandler.cs",
        "UpdateOnboardingConfigCommandHandler.cs",
        "GetOnboardingDraftQueryHandler.cs",
        "GetOnboardingDraftByRequestQueryHandler.cs",
        "ApproveOnboardingValidationCommandHandler.cs",
        "ReturnOnboardingToDraftCommandHandler.cs",
        "GetAssessmentRequestsQueryHandler.cs",
        "ApproveAssessmentRequestCommandHandler.cs",
        "DeclineAssessmentRequestCommandHandler.cs",
        "GetActivationContextQueryHandler.cs",
        "ActivateMunicipalityCommandHandler.cs",

        // Authentication, account recovery and MFA: pre-auth paths that resolve an account before a tenant exists.
        "RequestPasswordResetCommandHandler.cs",
        "ResetPasswordByTokenCommandHandler.cs",
        "SetAdminPasswordByTokenCommandHandler.cs",
        "GetPasswordResetContextQueryHandler.cs",
        "VerifyEmailCommandHandler.cs",
        "EmailVerificationSender.cs",
        "VerifyMfaLoginCommandHandler.cs",
        "ResetUserMfaCommand.cs",
        "GetMfaEnrolledAccountsQuery.cs",
        "CreateFirstConsoleAdminCommandHandler.cs",
        "IssueMobileBindLinkCommandHandler.cs",

        // Platform operator.
        "PlatformOperatorGuard.cs",
        "GetPlatformSetupStatusQueryHandler.cs",

        // Municipality profile and payment settings: the global Municipalities table.
        "GetMyOfficeProfileQueryHandler.cs",
        "UpdateOfficeProfileCommandHandler.cs",
        "GetMunicipalityPaymentSettingsQueryHandler.cs",
        "SetMunicipalityPaymentCredentialsCommandHandler.cs",
        // Tests an LGU's payment credentials against PayMongo and records the result on its own municipality row - the same
        // table, through the same context, as the two handlers above.
        "TestPaymentConnectionCommandHandler.cs",

        // Rates and the OR series.
        "GetFacilityRatesQueryHandler.cs",
        "SetFacilityRateCommandHandler.cs",
        // The same write as SetFacilityRate, for a section the office named itself: one effective-dated row, found by
        // its key or added. It is here for the same reason that one is — a rate row has no reads to speak of and no
        // repository of its own would carry any rule this handler does not already state.
        "SetNpmSectionRateCommandHandler.cs",
        // Registers the section AND, when the office prices it as it creates it, that same single rate row.
        "AddNpmCustomSectionCommandHandler.cs",
        // The one row holding a section's metering default: found and set, or added. A default bills nothing, so there is
        // no rule for a repository to hold beyond what this handler already states.
        "SetNpmSectionUtilitiesCommandHandler.cs",
        "GetNpmRatesQueryHandler.cs",
        "GetSlaughterAnimalRatesQueryHandler.cs",
        "AdvanceOrSeriesCommandHandler.cs",
        "GetOrSeriesSuggestionQueryHandler.cs",

        // The weekly market's day, which is effective-dated like a rate: the handler reads the office's existing
        // schedule to decide whether a baseline row is needed and whether the date is already taken, and writes
        // both rows in one save. Added deliberately (2026-08-21) for the same reason SetFacilityRate is here.
        "SetTpmMarketDayCommandHandler.cs",

        // Online payments: the gateway webhook and its confirmation.
        "HandlePaymentWebhookCommandHandler.cs",
        "ConfirmOnlinePaymentCommandHandler.cs",
    };

    private static List<string> FindApplicationFilesUsingEf()
    {
        var root = Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Application");
        var found = new List<string>();

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Contains("using Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
                found.Add(Path.GetFileName(path));
        }

        return found;
    }

    [Fact]
    public void EFDoesNotSpreadToANEWApplicationFile()
    {
        var offenders = FindApplicationFilesUsingEf()
            .Where(f => !Allowed.Contains(f))
            .Distinct()
            .OrderBy(f => f)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These Application files use Entity Framework and are not in the allowed set. Application is meant to be reached " +
            "through a repository interface; if a query really belongs here, add the file to `Allowed` in this test under the " +
            "feature it serves, with a reason:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheAllowedSetHasNoDEADEntries()
    {
        // A name left behind after a handler was converted silently re-permits EF in that file. This is also how progress on the
        // per-feature conversion shows up: convert a handler, and this test tells you to remove its name.
        var usingEf = FindApplicationFilesUsingEf().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dead = Allowed.Where(a => !usingEf.Contains(a)).OrderBy(a => a).ToList();

        Assert.True(dead.Count == 0,
            "These files no longer use Entity Framework, so they should be removed from `Allowed` — that is one fewer place " +
            "Application depends on EF:" + Environment.NewLine + string.Join(Environment.NewLine, dead));
    }

    [Fact]
    public void TheScanItselfIsNotBrokenOrVacuous()
    {
        // The lesson from TenantFilterCoverageTests and CrossTenantReadsAreNamedTests: a scan that finds nothing passes exactly as
        // quietly as one that finds nothing wrong. Audited at 38 files of 790 on 2026-08-17.
        var found = FindApplicationFilesUsingEf();

        Assert.True(found.Count >= 25, $"Expected the audited EF usage to still be found; saw {found.Count}.");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
