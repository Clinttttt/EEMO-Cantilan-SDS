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
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<ActivationResultDto>.Forbidden();

            var code = request.MunicipalityCode.Trim().ToUpperInvariant();

            // Municipality is a global reference table (not tenant-owned); load it directly.
            var municipality = await context.Municipalities
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Code == code, ct);

            if (municipality is null)
                return Result<ActivationResultDto>.NotFound();

            // Guard rails: never re-provision the live default LGU, never double-activate.
            if (municipality.IsDefault)
                return Result<ActivationResultDto>.Failure("The default municipality cannot be activated through onboarding.");
            if (municipality.Status == MunicipalityStatus.Active)
                return Result<ActivationResultDto>.Failure("This municipality is already active.");

            var username = request.Administrator.Username.Trim();

            // Usernames are unique per municipality (Phase 3 scoped constraint) — guard within the target LGU.
            var usernameTaken = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.MunicipalityId == municipality.Id && u.Username == username, ct);
            if (usernameTaken)
                return Result<ActivationResultDto>.Failure($"Username '{username}' is already taken in this municipality.");

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
            foreach (var f in request.Facilities)
            {
                var facility = Facility.Create(
                    f.Code, f.Name.Trim(), f.ShortName.Trim(), archetype: f.Archetype, municipalityId: municipality.Id);
                context.Facilities.Add(facility);
                if (f.Code == FacilityCode.NPM)
                {
                    npmFacility = facility;
                    npmSectionLabels = f.SectionLabels;
                }
            }

            // Name the daily market's collection areas as the LGU named them (e.g. "Gulayan" for the vegetable
            // area), so its own wording shows on its sheets without re-entry. The LGU declared which area each
            // of its sections is during onboarding, so nothing here interprets those names. An area the LGU
            // left unnamed keeps the platform's canonical wording until its Head sets one in the portal.
            if (npmFacility is not null)
                await ApplyNpmSectionLabelsAsync(npmFacility, npmSectionLabels, municipality.Name, ct);

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

            // 3c) Optional OR-series suggestion config (one per LGU). OR numbers stay manually entered; this
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

            // One SaveChanges => one transaction => all-or-nothing.
            await context.SaveChangesAsync(ct);

            // Email the Head their one-time set-password link (best-effort; the link is also shown in the
            // console for the operator to copy). Mirrors the onboarding-approval email pattern.
            var activationLink = ActivationLinks.Build(activationToken);
            var emailBody =
                $"Congratulations! {municipality.Name}'s StallTrack portal is now live.\n\n" +
                "As the designated Administrator (Head), please use the secure link below to set your password " +
                "and sign in for the first time. Once inside, you can add and manage your own staff (admins and " +
                "collectors) and begin day-to-day operations.\n\n" +
                $"Your username: {head.Username}\n" +
                $"Set your password:\n{activationLink}\n\n" +
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
}
