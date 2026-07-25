using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Security;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

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
        ICredentialProtector protector,
        ITotpService totp,
        IQrCodeGenerator qr,
        IUnitOfWork uow)
        : IRequestHandler<BeginMfaEnrollmentCommand, Result<MfaEnrollmentDto>>,
          IRequestHandler<ConfirmMfaEnrollmentCommand, Result<MfaRecoveryCodesDto>>,
          IRequestHandler<DisableMfaCommand, Result<bool>>,
          IRequestHandler<RegenerateRecoveryCodesCommand, Result<MfaRecoveryCodesDto>>
    {
        private const string BadPassword = "Your password is incorrect.";
        private const string BadCode = "That code is not valid. Check your authenticator app and try again.";

        public async Task<Result<MfaEnrollmentDto>> Handle(BeginMfaEnrollmentCommand request, CancellationToken ct)
        {
            var (user, failure) = await ResolveSelfAsync(request.CurrentPassword, ct);
            if (failure is not null) return Result<MfaEnrollmentDto>.Failure(failure.Error!, failure.StatusCode ?? 400);

            if (user!.MfaEnabled)
                return Result<MfaEnrollmentDto>.Failure("Two-factor authentication is already switched on.", 400);

            // A fresh secret every time enrollment starts, so an abandoned attempt can never be resumed.
            var secret = totp.GenerateSecret();
            user.BeginMfaEnrollment(protector.Protect(secret));
            await uow.SaveChangesAsync(ct);

            // The issuer is what the user sees in their app; keep it recognisable per office.
            var issuer = string.IsNullOrWhiteSpace(currentUser.MunicipalityCode)
                ? "StallTrack"
                : $"StallTrack {currentUser.MunicipalityCode}";
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

            return Result<MfaRecoveryCodesDto>.Success(new MfaRecoveryCodesDto(plain));
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

            if (string.IsNullOrEmpty(currentPassword) || !user.VerifyPassword(currentPassword))
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
