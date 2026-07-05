using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality
{
    public class ActivateMunicipalityCommandHandler(IAppDbContext context)
        : IRequestHandler<ActivateMunicipalityCommand, Result<ActivationResultDto>>
    {
        // Rates are seeded effective from a base date early enough to cover any billing period, so the
        // resolver returns the LGU's own rate for every date (mirrors the Cantilan seeder convention).
        private static readonly DateOnly RateEffectiveFrom = new(2020, 1, 1);

        public async Task<Result<ActivationResultDto>> Handle(ActivateMunicipalityCommand request, CancellationToken ct)
        {
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
            //    them, so the operator's own tenant is never applied).
            foreach (var f in request.Facilities)
            {
                context.Facilities.Add(Facility.Create(
                    f.Code, f.Name.Trim(), f.ShortName.Trim(), archetype: f.Archetype, municipalityId: municipality.Id));
            }

            // 3) Fixed ordinance rates for the LGU.
            foreach (var r in request.Rates)
            {
                context.FacilityRates.Add(FacilityRate.Create(
                    r.FacilityCode, r.Key, r.Amount, RateEffectiveFrom, municipality.Id, "Activation"));
            }

            // 4) Head account — provisioned in a must-set-password state; the temp secret is returned once.
            var temporaryPassword = GenerateTemporaryPassword();
            var head = AdminUser.Create(
                request.Administrator.FullName.Trim(),
                username,
                request.Administrator.Email.Trim(),
                temporaryPassword,
                AdminRole.SuperAdmin,
                municipality.Id);
            context.AdminUsers.Add(head);

            // One SaveChanges => one transaction => all-or-nothing.
            await context.SaveChangesAsync(ct);

            return Result<ActivationResultDto>.Success(new ActivationResultDto(
                municipality.Id,
                municipality.Code,
                head.Username!,
                temporaryPassword,
                request.Facilities.Count,
                request.Rates.Count));
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
