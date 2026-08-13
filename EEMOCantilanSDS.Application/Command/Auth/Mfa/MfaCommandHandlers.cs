using EEMOCantilanSDS.Application.Common.Interface.Security;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Security;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Command.Auth.Mfa
{
    /// <summary>
    /// Two-factor self-service for the signed-in admin: start enrollment, confirm it, regenerate recovery
    /// codes, and turn it off. All four act ONLY on the caller's own account (resolved from the token), so
    /// there is no way to target someone else's second factor.
    /// <para>
    /// Slice 1 scope: sign-in is NOT yet gated by MFA. Enrolling here is safe and reversible; enforcement
    /// arrives in the login slice.
    /// </para>
    /// </summary>
    public class MfaCommandHandlers(
        IAdminRepository adminRepo,
        ICurrentUserService currentUser,
        IMunicipalityRepository municipalityRepo,
        ICredentialProtector protector,
        ITotpService totp,
        IQrCodeGenerator qr,
        IUnitOfWork uow,
        ILogger<MfaCommandHandlers> logger,
        IPasswordHasher passwordHasher)
        : IRequestHandler<BeginMfaEnrollmentCommand, Result<MfaEnrollmentDto>>,
          IRequestHandler<ConfirmMfaEnrollmentCommand, Result<MfaRecoveryCodesDto>>,
          IRequestHandler<DisableMfaCommand, Result<bool>>,
          IRequestHandler<RegenerateRecoveryCodesCommand, Result<MfaRecoveryCodesDto>>
    {
        private const string BadPassword = "Your password is incorrect.";
        private const string BadCode = "That code is not valid. Check your authenticator app and try again.";

        public async Task<Result<MfaEnrollmentDto>> Handle(BeginMfaEnrollmentCommand request, CancellationToken ct)
        {
            // Enabling two-factor only ever RAISES protection, and the caller is already authenticated, so no
            // password re-entry is demanded here. The password is still required to DISABLE it or to
            // regenerate recovery codes — the directions where a hijacked session could do harm.
            if (currentUser.UserId is not { } selfId)
                return Result<MfaEnrollmentDto>.Unauthorized();

            var user = await adminRepo.GetByIdAsync(selfId, ct);
            if (user is null)
                return Result<MfaEnrollmentDto>.NotFound();

            if (user.MfaEnabled)
                return Result<MfaEnrollmentDto>.Failure("Two-factor authentication is already switched on.", 400);

            // A fresh secret every time enrollment starts, so an abandoned attempt can never be resumed.
            var secret = totp.GenerateSecret();
            user.BeginMfaEnrollment(protector.Protect(secret));
            await uow.SaveChangesAsync(ct);

            // Two-factor is a security control, so every state transition is logged (never a secret or a
            // code). Without this, an incident leaves no trace at all in the application logs.
            logger.LogInformation("MFA enrollment started for {Username}", user.Username);

            // The issuer is the label the user sees in their authenticator app. Data-driven per LGU from the
            // municipality registry ("EEMO Cantilan", "CEEO Carmen", …) rather than the opaque tenant code,
            // so every LGU reads correctly. Falls back to the platform name if the registry lookup fails.
            var municipality = await municipalityRepo.GetByIdAsync(user.MunicipalityId, ct);
            var issuer = BuildIssuer(municipality);
            var uri = totp.BuildProvisioningUri(secret, issuer, user.Username ?? "account");

            return Result<MfaEnrollmentDto>.Success(new MfaEnrollmentDto(
                ManualKey: secret,
                ProvisioningUri: uri,
                QrCodeDataUri: qr.ToPngDataUri(uri)));
        }

        public async Task<Result<MfaRecoveryCodesDto>> Handle(ConfirmMfaEnrollmentCommand request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } id) return Result<MfaRecoveryCodesDto>.Unauthorized();
            var user = await adminRepo.GetByIdAsync(id, ct);
            if (user is null) return Result<MfaRecoveryCodesDto>.NotFound();

            if (user.MfaEnabled)
                return Result<MfaRecoveryCodesDto>.Failure("Two-factor authentication is already switched on.", 400);
            if (!user.HasPendingMfaEnrollment || user.MfaSecretCipher is null)
                return Result<MfaRecoveryCodesDto>.Failure("Start the setup again — no pending enrollment was found.", 400);

            var secret = protector.Unprotect(user.MfaSecretCipher);
            if (!totp.TryValidate(secret, request.Code, user.MfaLastUsedStep, out var step))
                return Result<MfaRecoveryCodesDto>.Failure(BadCode, 400);

            var (plain, hashes) = RecoveryCodes.Generate();
            user.ConfirmMfaEnrollment(step, hashes);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("MFA enabled for {Username}", user.Username);

            return Result<MfaRecoveryCodesDto>.Success(new MfaRecoveryCodesDto(plain));
        }

        public async Task<Result<bool>> Handle(DisableMfaCommand request, CancellationToken ct)
        {
            var (user, failure) = await ResolveSelfAsync(request.CurrentPassword, ct);
            if (failure is not null) return Result<bool>.Failure(failure.Error!, failure.StatusCode ?? 400);

            if (!user!.MfaEnabled)
            {
                // Not on: clear any half-finished enrollment so the panel returns to a clean state.
                user.DisableMfa();
                await uow.SaveChangesAsync(ct);
                return Result<bool>.Success(true);
            }

            // Turning the second factor OFF must itself require the second factor.
            if (!VerifyCodeOrRecovery(user, request.Code, out var step))
                return Result<bool>.Failure(BadCode, 400);
            if (step is { } used) user.RecordMfaStep(used);

            user.DisableMfa();
            await uow.SaveChangesAsync(ct);
            logger.LogWarning("MFA DISABLED for {Username} (password + second factor verified)", user.Username);
            return Result<bool>.Success(true);
        }

        public async Task<Result<MfaRecoveryCodesDto>> Handle(RegenerateRecoveryCodesCommand request, CancellationToken ct)
        {
            var (user, failure) = await ResolveSelfAsync(request.CurrentPassword, ct);
            if (failure is not null) return Result<MfaRecoveryCodesDto>.Failure(failure.Error!, failure.StatusCode ?? 400);

            if (!user!.MfaEnabled)
                return Result<MfaRecoveryCodesDto>.Failure("Two-factor authentication is not switched on.", 400);

            var (plain, hashes) = RecoveryCodes.Generate();
            user.ReplaceRecoveryCodes(hashes);          // every previous code stops working
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("MFA recovery codes regenerated for {Username}", user.Username);

            return Result<MfaRecoveryCodesDto>.Success(new MfaRecoveryCodesDto(plain));
        }

        /// <summary>
        /// The authenticator-app label for an LGU: office acronym + municipality ("EEMO Cantilan"). Purely
        /// data-driven from the registry so each LGU reads correctly, degrading to whichever part exists and
        /// finally to the platform name.
        /// </summary>
        private static string BuildIssuer(Domain.Entities.Tenancy.Municipality? municipality)
        {
            if (municipality is null)
                return "StallTrack";

            var acronym = municipality.OfficeAcronym?.Trim();
            var name = municipality.Name?.Trim();

            if (!string.IsNullOrWhiteSpace(acronym) && !string.IsNullOrWhiteSpace(name))
                return $"{acronym} {name}";
            if (!string.IsNullOrWhiteSpace(acronym))
                return acronym!;
            if (!string.IsNullOrWhiteSpace(name))
                return $"StallTrack {name}";

            return "StallTrack";
        }

        /// <summary>
        /// Loads the caller's own account and re-authenticates them by password. Returns a failure Result
        /// (never a partially-populated user) when the session or password does not hold up.
        /// </summary>
        private async Task<(Domain.Entities.Users.AdminUser? User, Result<bool>? Failure)> ResolveSelfAsync(
            string currentPassword, CancellationToken ct)
        {
            if (currentUser.UserId is not { } id)
                return (null, Result<bool>.Unauthorized());

            var user = await adminRepo.GetByIdAsync(id, ct);
            if (user is null)
                return (null, Result<bool>.NotFound());

            if (string.IsNullOrEmpty(currentPassword) || passwordHasher.Check(user.PasswordHash, currentPassword) == PasswordCheck.Failed)
                return (null, Result<bool>.Failure(BadPassword, 400));

            return (user, null);
        }

        /// <summary>
        /// Accepts either a 6-digit authenticator code or one single-use recovery code. A consumed recovery
        /// code is removed immediately; <paramref name="matchedStep"/> is only set for a TOTP match.
        /// </summary>
        private bool VerifyCodeOrRecovery(Domain.Entities.Users.AdminUser user, string code, out long? matchedStep)
        {
            matchedStep = null;
            if (string.IsNullOrWhiteSpace(code) || user.MfaSecretCipher is null)
                return false;

            var secret = protector.Unprotect(user.MfaSecretCipher);
            if (totp.TryValidate(secret, code, user.MfaLastUsedStep, out var step))
            {
                matchedStep = step;
                return true;
            }

            return user.TryConsumeRecoveryCode(RecoveryCodes.Hash(code));
        }
    }
}
