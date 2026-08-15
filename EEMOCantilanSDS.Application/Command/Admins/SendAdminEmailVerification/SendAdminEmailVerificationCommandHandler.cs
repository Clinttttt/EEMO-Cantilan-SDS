using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.SendAdminEmailVerification
{
    /// <summary>
    /// Sends (or re-sends) an admin's email-confirmation link. Tenant-scoped through the admin repository,
    /// so a Head can only act on their own municipality's accounts.
    /// </summary>
    public class SendAdminEmailVerificationCommandHandler(
        IAdminRepository adminRepo,
        IEmailVerificationSender verificationSender,
        ICurrentUserService currentUser,
        IUnitOfWork uow)
        : IRequestHandler<SendAdminEmailVerificationCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(SendAdminEmailVerificationCommand request, CancellationToken ct)
        {
            var admin = await adminRepo.GetByIdAsync(request.AdminId, ct);
            if (admin is null) return Result<bool>.NotFound();

            // A Head may not act on a PEER Head's account (only their own, or ordinary Admins).
            if (!AdminManagementGuard.CanActOn(admin, currentUser.UserId))
                return Result<bool>.Failure(AdminManagementGuard.PeerHeadDenied, ResultStatus.Forbidden);

            if (string.IsNullOrWhiteSpace(admin.Email))
                return Result<bool>.Failure("This account has no email address to confirm.");

            if (admin.EmailVerified)
                return Result<bool>.Failure("This email address is already confirmed.");

            // Stamps the token on the tracked entity; the unit of work persists it.
            var sent = await verificationSender.SendAsync(admin, save: false, ct);
            await uow.SaveChangesAsync(ct);

            return sent
                ? Result<bool>.Success(true)
                : Result<bool>.Failure("We couldn't send the confirmation email. Please check the email settings and try again.");
        }
    }
}
