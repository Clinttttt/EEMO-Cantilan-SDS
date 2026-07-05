using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality
{
    public class ActivateMunicipalityCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<ActivateMunicipalityCommand, Result<ActivationResultDto>>
    {
        // Rates are seeded effective from a base date early enough to cover any billing period, so the
        // resolver returns the LGU's own rate for every date (mirrors the Cantilan seeder convention).
        private static readonly DateOnly RateEffectiveFrom = new(2020, 1, 1);

        public async Task<Result<ActivationResultDto>> Handle(ActivateMunicipalityCommand request, CancellationToken ct)
        {
            // Platform-operator authorization: onboarding a new LGU is a system-owner action, so only a
            // SuperAdmin of the DEFAULT municipality (Cantilan) may run it. A per-LGU Head can never
            // provision another municipality. (Defense-in-depth alongside the controller's [Authorize].)
            var defaultMunicipalityId = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync(ct);

            var isPlatformOperator =
                string.Equals(currentUser.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase)
                && defaultMunicipalityId is not null
                && currentUser.MunicipalityId == defaultMunicipalityId;

            if (!isPlatformOperator)
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
                request.Branding.OfficeName, request.Branding.Address, request.Branding.SealPath, "Activation");
            municipality.Activate();

            // 2) Facilities — created under the NEW LGU's id (explicit id makes the stamp interceptor skip
            //    them, so the operator's own tenant is never applied). Optionally provision each facility's
            //    stalls/units (spaces); occupants/contracts are added later in the portal.
            var stallsCreated = 0;
            foreach (var f in request.Facilities)
            {
                var facility = Facility.Create(
                    f.Code, f.Name.Trim(), f.ShortName.Trim(), archetype: f.Archetype, municipalityId: municipality.Id);
                context.Facilities.Add(facility);

                if (f.StallGroups is { Count: > 0 })
                {
                    var stallNo = 0;
                    foreach (var g in f.StallGroups)
                    {
                        for (var i = 0; i < g.Count; i++)
                        {
                            stallNo++;
                            context.Stalls.Add(Stall.Create(
                                facility.Id,
                                stallNo.ToString(),
                                g.MonthlyRate,
                                g.Fees,
                                section: g.Section,
                                dailyRate: g.DailyRate,
                                createdBy: "Activation",
                                municipalityId: municipality.Id));
                            stallsCreated++;
                        }
                    }
                }
            }

            // 3) Fixed ordinance rates for the LGU.
            foreach (var r in request.Rates)
            {
                context.FacilityRates.Add(FacilityRate.Create(
                    r.FacilityCode, r.Key, r.Amount, RateEffectiveFrom, municipality.Id, "Activation"));
            }

            // 4) Head account — provisioned INACTIVE with a one-time activation token. The Head sets their
            //    own password through the secure link; the placeholder password is random and never disclosed.
            var (activationToken, activationTokenHash) = GenerateActivationToken();
            var head = AdminUser.Create(
                request.Administrator.FullName.Trim(),
                username,
                request.Administrator.Email.Trim(),
                GenerateTemporaryPassword(),
                AdminRole.SuperAdmin,
                municipality.Id,
                isActive: false);
            head.SetActivationToken(activationTokenHash, DateTime.UtcNow.AddDays(7));
            context.AdminUsers.Add(head);

            // One SaveChanges => one transaction => all-or-nothing.
            await context.SaveChangesAsync(ct);

            return Result<ActivationResultDto>.Success(new ActivationResultDto(
                municipality.Id,
                municipality.Code,
                head.Username!,
                activationToken,
                request.Facilities.Count,
                request.Rates.Count,
                stallsCreated));
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
