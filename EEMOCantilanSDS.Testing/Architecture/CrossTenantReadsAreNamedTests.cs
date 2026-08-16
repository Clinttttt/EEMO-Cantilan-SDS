using System.Text;

namespace EEMOCantilanSDS.Testing.Architecture;

/// <summary>
/// Every place that reads ACROSS municipalities, named.
///
/// <para>
/// <c>IgnoreQueryFilters()</c> switches off the tenant filter that keeps one LGU's figures out of another's screens. It is the
/// single most consequential call in this codebase, and it was free to add anywhere: nothing failed, nothing warned, and the
/// mistake would not look like one — a query that returns MORE rows than it should reads exactly like a query that works.
/// </para>
///
/// <para>
/// So the allowed set is stated here. A new file reaching across tenants fails this test until somebody adds it deliberately,
/// with a reason, which is the whole point: the decision becomes visible in a diff instead of arriving inside a repository nobody
/// re-reads.
/// </para>
///
/// <para>
/// Audited file by file on 2026-08-16 before being pinned. The backlog note claimed "roughly a dozen" call sites; there are 86,
/// across 37 files. Every one was read and falls into one of six patterns, all of which are legitimate:
/// </para>
///
/// <list type="number">
///   <item>
///     PRE-AUTH IDENTITY. Login, activation, password reset, e-mail verification, refresh tokens, device binding. The caller has
///     no token yet, so no tenant is resolved — the LGU is derived FROM the record found. Each looks up a globally unique secret
///     (a token, a single-use code, a hashed refresh token) or a username at sign-in, and the handler pins the request to that
///     record's municipality before doing anything with it.
///   </item>
///   <item>
///     PLATFORM OPERATOR. Console paths that exist to act across LGUs: activating a municipality, approving onboarding, resetting
///     another account's MFA, listing MFA-enrolled accounts, and the operator check itself. Each is gated by
///     <c>PlatformOperatorPolicy</c> before it reads anything.
///   </item>
///   <item>
///     GLOBAL REFERENCE DATA. <c>Municipalities</c> is not tenant-owned — a municipality cannot be scoped to itself. Office
///     profile, payment settings, market-day lookups and startup all read it by the caller's OWN id or code.
///   </item>
///   <item>
///     SEEDING AND STARTUP. Runs with no tenant at all, and asks only "has this been seeded yet".
///   </item>
///   <item>
///     SOFT-DELETE ONLY. The filter is switched off to include deleted rows, then the municipality is re-applied BY HAND in the
///     predicate — <c>OrNumberRegistry</c>, <c>AdminRepository</c> and <c>CollectorRepository</c> all read
///     <c>MunicipalityId == mid</c>. A cancelled receipt's OR number is still spent, and a deleted account's username is still
///     taken, so those rows have to be visible.
///   </item>
///   <item>
///     WHOLE-DATABASE WORK. Export and restore, which are platform-operator only by definition.
///   </item>
/// </list>
/// </summary>
public class CrossTenantReadsAreNamedTests
{
    /// <summary>
    /// The files permitted to read across municipalities, with the pattern each belongs to. Adding a name here is a deliberate
    /// act: it says this file's cross-tenant reads have been read and understood.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // 1. Pre-auth identity — the tenant is derived from the record found, never assumed.
        "AuthRepository.cs",
        "TokenService.cs",
        "PayorRepository.cs",
        "CollectorRepository.cs",
        "CollectorDeviceTokenRepository.cs",
        "AdminRepository.cs",
        "GetActivationContextQueryHandler.cs",
        "GetPasswordResetContextQueryHandler.cs",
        "RequestPasswordResetCommandHandler.cs",
        "ResetPasswordByTokenCommandHandler.cs",
        "SetAdminPasswordByTokenCommandHandler.cs",
        "VerifyEmailCommandHandler.cs",
        "VerifyMfaLoginCommandHandler.cs",
        "EmailVerificationSender.cs",
        "IssueMobileBindLinkCommandHandler.cs",

        // 2. Platform operator — gated by PlatformOperatorPolicy before reading.
        "PlatformOperatorGuard.cs",
        "ActivateMunicipalityCommandHandler.cs",
        "ResetUserMfaCommand.cs",
        "GetMfaEnrolledAccountsQuery.cs",
        "CreateFirstConsoleAdminCommandHandler.cs",
        "GetPlatformSetupStatusQueryHandler.cs",

        // 3. Global reference data — Municipalities is not tenant-owned; each reads the caller's own row.
        "GetMyOfficeProfileQueryHandler.cs",
        "UpdateOfficeProfileCommandHandler.cs",
        "GetMunicipalityPaymentSettingsQueryHandler.cs",
        "SetMunicipalityPaymentCredentialsCommandHandler.cs",
        "TpmMarketDayProvider.cs",
        "SetupRepository.cs",

        // 4. Seeding and startup — no tenant exists yet.
        "FacilitySeeder.cs",
        "FacilityRateSeeder.cs",
        "DatabaseStartup.cs",
        "AppDbContext.cs",

        // 5. Soft-delete only — the municipality is re-applied by hand in the predicate.
        "OrNumberRegistry.cs",
        "SyncRepository.cs",
        "OnlinePaymentRepository.cs",
        "AuditRepository.cs",

        // 6. Whole-database work — platform-operator only by definition.
        "TenantExportRepository.cs",
        "TenantRestoreRepository.cs",
    };

    private static readonly string[] ProjectsThatMayNotDriftOpen =
    [
        "EEMOCantilanSDS.Infrastructure",
        "EEMOCantilanSDS.Application",
        "EEMOCantilanSDS.Api",
        "EEMOCantilanSDS.Domain",
    ];

    private static List<(string File, int Line, string Text)> FindCrossTenantReads()
    {
        var found = new List<(string, int, string)>();

        foreach (var project in ProjectsThatMayNotDriftOpen)
        {
            var root = Path.Combine(RepositoryRoot(), project);
            if (!Directory.Exists(root)) continue;

            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                // Generated output and EF's own migration scaffolding are not decisions anybody made.
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
                    continue;

                var lines = File.ReadAllLines(path, Encoding.UTF8);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("IgnoreQueryFilters", StringComparison.Ordinal))
                        found.Add((Path.GetFileName(path), i + 1, lines[i].Trim()));
                }
            }
        }

        return found;
    }

    [Fact]
    public void NoUNNAMEDFileReadsAcrossMunicipalities()
    {
        var offenders = FindCrossTenantReads()
            .Where(x => !Allowed.Contains(x.File))
            .Select(x => $"{x.File}:{x.Line}  {x.Text}")
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files switch off the tenant filter and are not in the allowed set. A cross-tenant read is a deliberate " +
            "decision, so either scope the query to the caller's municipality, or add the file to `Allowed` in this test with " +
            "the pattern it belongs to and a comment saying why:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheScanItselfIsNotBrokenOrVacuous()
    {
        // The lesson from TenantFilterCoverageTests, which once passed with a tenant-owned entity deliberately excluded: a test
        // that finds nothing passes just as quietly as one that finds nothing WRONG. If a path changes, a rename lands, or the
        // repository root cannot be located, the assertion above would go green while checking an empty list.
        //
        // Audited on 2026-08-16: 86 call sites across 37 files. The floor is set below that so ordinary tidying does not fail the
        // build, but far enough above zero that a broken scan does.
        var found = FindCrossTenantReads();

        Assert.True(found.Count >= 60, $"Expected the audited cross-tenant reads to still be found; saw {found.Count}.");
        Assert.True(found.Select(x => x.File).Distinct().Count() >= 25,
            "Expected cross-tenant reads across many files; the scan is probably looking in the wrong place.");
    }

    [Fact]
    public void TheAllowedSetHasNoDEADEntries()
    {
        // An allow-list that outlives its reasons stops being one. A name left behind after the last cross-tenant read in it was
        // removed silently re-permits the next one added to that file.
        var filesWithReads = FindCrossTenantReads().Select(x => x.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dead = Allowed.Where(a => !filesWithReads.Contains(a)).OrderBy(a => a).ToList();

        Assert.True(dead.Count == 0,
            "These files no longer read across municipalities, so they should be removed from `Allowed`:" +
            Environment.NewLine + string.Join(Environment.NewLine, dead));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);        // the tests above are meaningless if the tree cannot be found
        return dir!.FullName;
    }
}
