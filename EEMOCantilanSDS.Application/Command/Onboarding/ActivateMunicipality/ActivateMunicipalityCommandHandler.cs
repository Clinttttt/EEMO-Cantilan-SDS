using EEMOCantilanSDS.Application.Common.Interface.Security;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality
{
    public class ActivateMunicipalityCommandHandler(IAppDbContext context, ICurrentUserService currentUser, IEmailSender emailSender, IPasswordHasher passwordHasher)
        : IRequestHandler<ActivateMunicipalityCommand, Result<ActivationResultDto>>
    {
        // Rates are seeded effective from a base date early enough to cover any billing period, so the
        // resolver returns the LGU's own rate for every date (mirrors the Cantilan seeder convention).
        private static readonly DateOnly RateEffectiveFrom = new(2020, 1, 1);

        public async Task<Result<ActivationResultDto>> Handle(ActivateMunicipalityCommand request, CancellationToken ct)
        {
            // Platform-operator authorization: onboarding a new LGU is a system-owner action, so a per-LGU Head can
            // never provision another municipality. (Defense-in-depth alongside the controller's [Authorize].)
            //
            // Through the shared guard, not an inlined copy. The copy accepted only the default tenant's SuperAdmin, so
            // a DEDICATED operator account — the mechanism meant to replace that fallback — could approve an LGU's
            // onboarding and then be refused the activation that completes it.
            var preflight = await MunicipalityActivationPreflight.CheckAsync(context, currentUser, request, ct);
            if (!preflight.IsSuccess)
                return MunicipalityActivationPreflight.CopyFailure<ActivationResultDto>(preflight);

            var municipality = preflight.Value!;

            var pipeline = await MunicipalityActivationPreflight.FindPipelineAsync(context, request, municipality, ct);
            if (pipeline is null)
                return Result<ActivationResultDto>.Failure(
                    "No active onboarding request matches this municipality.", ResultStatus.Conflict);
            if (pipeline.Stage != "Activation")
                return Result<ActivationResultDto>.Failure(
                    $"{pipeline.Municipality}'s onboarding request is in {pipeline.Stage}, not Activation.", ResultStatus.Conflict);

            var username = request.Administrator.Username.Trim();

            // Usernames are unique per municipality (Phase 3 scoped constraint) — guard within the target LGU.
            // 1) Stamp branding + go live.
            municipality.ApplyOnboardingProfile(
                request.Branding.OfficeName, request.Branding.Address, request.Branding.SealPath, request.Branding.OfficeAcronym, "Activation", request.TpmMarketDay);
            municipality.Activate();

            // 2) Facilities — created under the NEW LGU's id (explicit id makes the stamp interceptor skip
            //    them, so the operator's own tenant is never applied). Stalls/units (and their
            //    occupants/payors) are NEVER provisioned at onboarding/activation — they are created in the
            //    live portal — so any StallGroups on the command are intentionally ignored.
            var stallsCreated = 0;
            Facility? npmFacility = null;
            ActivationSectionLabels? npmSectionLabels = null;
            IReadOnlyList<string>? npmCustomSections = null;
            foreach (var f in request.Facilities)
            {
                var facility = Facility.Create(
                    f.Code, f.Name.Trim(), f.ShortName.Trim(), archetype: f.Archetype, municipalityId: municipality.Id);
                context.Facilities.Add(facility);
                if (f.Code == FacilityCode.NPM)
                {
                    npmFacility = facility;
                    npmSectionLabels = f.SectionLabels;
                    npmCustomSections = f.CustomSections;

                    // The office's own rule for what a market month owes, as it declared at onboarding, so its first month
                    // is measured by its own convention rather than by somebody else's. Recorded as a STATEMENT, which is
                    // also what stops the console asking the question the office has just answered.
                    facility.SetMonthBasis(f.MonthBasis, "Activation");
                }
            }

            // Name the daily market's collection areas as the LGU named them (e.g. "Gulayan" for the vegetable
            // area), so its own wording shows on its sheets without re-entry. The LGU declared which area each
            // of its sections is during onboarding, so nothing here interprets those names. An area the LGU
            // left unnamed keeps the platform's canonical wording until its Head sets one in the portal.
            if (npmFacility is not null)
                await ApplyNpmSectionLabelsAsync(npmFacility, npmSectionLabels, municipality.Name, ct);

            // The market's OWN areas, beyond the three the platform keys on. An office whose market has a rice section
            // or a dry goods row declared it during onboarding; registering it here means its stalls can be filed under
            // that area from the first day, instead of the office having to re-type the name into the first stall it
            // creates (which is the only way one came into being before). AddCustomSection trims, refuses a blank and
            // ignores a repeat, so this is safe to call for whatever the office sent.
            if (npmFacility is not null && npmCustomSections is { Count: > 0 })
            {
                foreach (var area in npmCustomSections)
                    npmFacility.AddCustomSection(area, "Activation");
            }

            // 3) Fixed ordinance rates for the LGU.
            //
            // Filed once per facility and rate key. An onboarding config can state the same rate twice — the platform
            // holds ONE large-animal rate, so a slaughterhouse listing carabao and cow at the same amount arrives as
            // two identical rows — and every rate is filed on one effective date, so the second row hit the unique
            // index and Postgres answered the operator with the bare word "Conflict". Two rows saying the same thing
            // are one statement; two rows saying DIFFERENT things are refused by the validator, because choosing
            // between them would be the platform deciding an ordinance.
            var ratesToSeed = request.Rates
                .GroupBy(r => (r.FacilityCode, r.Key))
                .Select(g => g.First())
                .ToList();
            foreach (var r in ratesToSeed)
            {
                context.FacilityRates.Add(FacilityRate.Create(
                    r.FacilityCode, r.Key, r.Amount, RateEffectiveFrom, municipality.Id, "Activation"));
            }

            // 3b) Custom slaughterhouse animal types (beyond Hog/Carabao/Cow) with their default per-head
            //     rates — seeded into the LGU's own registry so the SLH record screen can offer them later.
            var customAnimalsCreated = 0;
            if (request.CustomAnimals is { Count: > 0 })
            {
                // Once per name, for the same reason as the rates above: the registry is keyed by name, and the same
                // animal named twice at the same rate is one entry. Two rates for one name are refused by the validator.
                foreach (var a in request.CustomAnimals
                    .GroupBy(a => a.AnimalName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First()))
                {
                    context.SlaughterAnimalRates.Add(SlaughterAnimalRate.Create(
                        a.AnimalName, a.RatePerHead, municipality.Id, "Activation"));
                    customAnimalsCreated++;
                }
            }

            // 3c) Optional tenant wording for stable built-in animal identities. Missing rows deliberately keep the
            // canonical labels, preserving every municipality activated before this capability existed.
            if (request.SlaughterLabels is { } labels)
            {
                context.SlaughterAnimalLabels.Add(SlaughterAnimalLabel.Create(
                    AnimalType.Hog, labels.Hog, municipality.Id, "Activation"));
                context.SlaughterAnimalLabels.Add(SlaughterAnimalLabel.Create(
                    AnimalType.Carabao, labels.Carabao, municipality.Id, "Activation"));
                context.SlaughterAnimalLabels.Add(SlaughterAnimalLabel.Create(
                    AnimalType.Cow, labels.Cow, municipality.Id, "Activation"));
            }

            // 3d) Optional OR-series suggestion config (one per LGU). OR numbers stay manually entered; this
            //     only seeds the suggested format the portal pre-fills.
            var orSeriesConfigured = false;
            if (request.OrSeries is { } os)
            {
                context.OrSeriesConfigs.Add(OrSeriesConfig.Create(
                    os.Prefix, os.StartNumber, os.PadWidth, os.Enabled, municipality.Id, "Activation"));
                orSeriesConfigured = true;
            }

            // 4) Head account — provisioned INACTIVE with a one-time activation token. The Head sets their
            //    own password through the secure link; the placeholder password is random and never disclosed.
            var (activationToken, activationTokenHash) = GenerateActivationToken();
            var head = AdminUser.Create(
                request.Administrator.FullName.Trim(),
                username,
                request.Administrator.Email.Trim(),
                passwordHasher.Hash(GenerateTemporaryPassword()),
                AdminRole.SuperAdmin,
                municipality.Id,
                isActive: false);
            head.SetActivationToken(activationTokenHash, DateTime.UtcNow.AddDays(7));
            context.AdminUsers.Add(head);

            // Activation and pipeline completion share the same transaction. A successful tenant can never leave an
            // AssessmentRequest appearing active, and a failed activation leaves the request in Activation to retry.
            pipeline.CompleteActivation(currentUser.Username ?? "Operator");

            // One SaveChanges => one transaction => all-or-nothing.
            await context.SaveChangesAsync(ct);

            // Email the Head their one-time set-password link (best-effort; the link is also shown in the
            // console for the operator to copy). Mirrors the onboarding-approval email pattern.
            // The username is NOT stated. The account is provisioned under a name derived from the LGU so the row is
            // valid, but the Head chooses their own on the activation page; naming the provisioned one here told the
            // office to expect a sign-in name it never chose and would not end up using.
            var activationLink = ActivationLinks.Build(activationToken);
            var emailBody =
                $"Congratulations! {municipality.Name}'s StallTrack portal is now live.\n\n" +
                "As the designated Administrator (Head), please use the secure link below to choose your username and " +
                "set your password, then sign in for the first time. Once inside, you can add and manage your own staff " +
                "(admins and collectors) and begin day-to-day operations.\n\n" +
                $"Activate your account:\n{activationLink}\n\n" +
                "This is a one-time link and expires in 7 days.\n\n" +
                "— StallTrack Platform Team";
            await emailSender.SendAsync(
                head.Email!, head.FullName, $"{municipality.Name} — Your StallTrack portal is live", emailBody, ct);

            return Result<ActivationResultDto>.Success(new ActivationResultDto(
                municipality.Id,
                municipality.Code,
                head.Username!,
                activationToken,
                request.Facilities.Count,
                ratesToSeed.Count,
                stallsCreated,
                customAnimalsCreated,
                orSeriesConfigured));
        }

        // Names the daily market's three collection areas as the LGU named them.
        //
        // The LGU declares, during onboarding, which collection area each of its market sections is; its
        // section names are its own labels, in its own language, and carry no meaning to the platform. This
        // used to classify those names by English keyword ("fish", "meat") and take whatever was left as the
        // vegetable area, which meant an LGU writing "Gulayan, Isda, Karne" had its fish and meat areas
        // dropped and rendered under the platform's canonical wording instead of its own.
        //
        // Labels come from the activation command. Where a command carries none (an older console build), the
        // LGU's saved onboarding draft is read for the area each section was declared to be. Nothing is ever
        // inferred from a section's wording: an area with no declaration keeps the canonical label, which the
        // Head can correct in the facility Configuration drawer.
        private async Task ApplyNpmSectionLabelsAsync(
            Facility npm, ActivationSectionLabels? labels, string municipalityName, CancellationToken ct)
        {
            if (labels is not null && (labels.Vegetable ?? labels.Fish ?? labels.Meat) is not null)
            {
                npm.SetSectionLabels(labels.Vegetable, labels.Fish, labels.Meat, "Activation");
                return;
            }

            try
            {
                var declared = await ReadDeclaredSectionLabelsFromDraftAsync(municipalityName, ct);
                if (declared is not null)
                    npm.SetSectionLabels(declared.Vegetable, declared.Fish, declared.Meat, "Activation");
            }
            catch
            {
                // Best-effort only: never block activation on reading a saved draft. Labels stay canonical
                // and the Head sets them in the facility Configuration drawer.
            }
        }

        // Reads the collection area each market section was declared to be from the LGU's saved onboarding
        // draft. Only an explicit declaration counts; a section without one is skipped.
        private async Task<ActivationSectionLabels?> ReadDeclaredSectionLabelsFromDraftAsync(
            string municipalityName, CancellationToken ct)
        {
            var configJson = await context.OnboardingDrafts
                .IgnoreQueryFilters()
                .Where(d => d.Municipality == municipalityName)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => d.ConfigJson)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(configJson))
                return null;

            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("facilities", out var facs) || facs.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var fac in facs.EnumerateArray())
            {
                var catalogKey = fac.TryGetProperty("catalogKey", out var ck) ? ck.GetString() : null;
                var archetype = fac.TryGetProperty("archetype", out var at) ? at.GetString() : null;
                var isDailyStall = string.Equals(catalogKey, "public_market", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(archetype, "DailyStall", StringComparison.OrdinalIgnoreCase);
                if (!isDailyStall)
                    continue;
                if (!fac.TryGetProperty("sections", out var secs) || secs.ValueKind != JsonValueKind.Array)
                    return null;

                string? veg = null, fish = null, meat = null;
                foreach (var sec in secs.EnumerateArray())
                {
                    var name = (sec.TryGetProperty("name", out var n) ? n.GetString() : null)?.Trim();
                    var kind = (sec.TryGetProperty("kind", out var k) ? k.GetString() : null)?.Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind))
                        continue;

                    // The draft's values are the MarketSection names verbatim.
                    if (string.Equals(kind, nameof(MarketSection.VegetableArea), StringComparison.OrdinalIgnoreCase))
                        veg ??= name;
                    else if (string.Equals(kind, nameof(MarketSection.FishSection), StringComparison.OrdinalIgnoreCase))
                        fish ??= name;
                    else if (string.Equals(kind, nameof(MarketSection.MeatSection), StringComparison.OrdinalIgnoreCase))
                        meat ??= name;
                }

                // Only the first daily-stall facility carries the market's sections.
                return (veg ?? fish ?? meat) is null ? null : new ActivationSectionLabels(veg, fish, meat);
            }

            return null;
        }

        // A url-safe, cryptographically-random one-time activation token; only its SHA-256 hash is stored.
        private static (string raw, string hash) GenerateActivationToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var raw = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            return (raw, hash);
        }

        // Cryptographically-random one-time password that satisfies upper/lower/digit/symbol complexity.
        private static string GenerateTemporaryPassword()
        {
            Span<byte> bytes = stackalloc byte[12];
            RandomNumberGenerator.Fill(bytes);
            var core = Convert.ToBase64String(bytes).Replace('+', 'K').Replace('/', 'z').TrimEnd('=');
            return $"Aa1!{core}";
        }
    }

    /// <summary>
    /// The shared, read-only prerequisite check used by both activation and the operator's preflight endpoint.
    /// Keeping it beside activation prevents the irreversible path and its dry-run from drifting apart.
    /// </summary>
    public static class MunicipalityActivationPreflight
    {
        public static async Task<Result<Municipality>> CheckAsync(IAppDbContext context, ICurrentUserService currentUser,
            ActivateMunicipalityCommand request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<Municipality>.Forbidden();

            var code = request.MunicipalityCode.Trim().ToUpperInvariant();
            var municipality = await context.Municipalities.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Code == code, ct);
            if (municipality is null) return Result<Municipality>.NotFound();
            if (municipality.IsDefault)
                return Result<Municipality>.Failure("The default municipality cannot be activated through onboarding.");
            if (municipality.Status == MunicipalityStatus.Active)
                return Result<Municipality>.Failure("This municipality is already active.");

            var pipeline = await FindPipelineAsync(context, request, municipality, ct);
            if (pipeline is null)
                return Result<Municipality>.Failure(
                    "No active onboarding request matches this municipality.", ResultStatus.Conflict);

            var username = request.Administrator.Username.Trim();
            if (await context.Users.IgnoreQueryFilters()
                    .AnyAsync(u => u.MunicipalityId == municipality.Id && u.Username == username, ct))
                return Result<Municipality>.Failure($"Username '{username}' is already taken in this municipality.");

            return Result<Municipality>.Success(municipality);
        }

        public static async Task<AssessmentRequest?> FindPipelineAsync(
            IAppDbContext context,
            ActivateMunicipalityCommand request,
            Municipality municipality,
            CancellationToken ct)
        {
            var active = OnboardingPipelineGuard.Active(context.AssessmentRequests);
            if (request.AssessmentRequestId is Guid requestId)
            {
                return await active.FirstOrDefaultAsync(r =>
                    r.Id == requestId
                    && r.Municipality.Trim().ToUpper() == municipality.Name.Trim().ToUpper()
                    && r.Province.Trim().ToUpper() == municipality.Province.Trim().ToUpper(), ct);
            }

            // Compatibility for a cached/older operator console. The database invariant guarantees at most one.
            return await active.FirstOrDefaultAsync(r =>
                r.Municipality.Trim().ToUpper() == municipality.Name.Trim().ToUpper()
                && r.Province.Trim().ToUpper() == municipality.Province.Trim().ToUpper(), ct);
        }

        public static Result<T> CopyFailure<T>(Result<Municipality> source) =>
            source.ValidationErrors is { } errors
                ? Result<T>.ValidationFailure(errors)
                : Result<T>.Failure(source.Error ?? "Activation preflight failed.", source.Status);
    }
}
