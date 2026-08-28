using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.SendMyEmailVerification
{
    /// <summary>
    /// Sends the caller its own confirmation link, resolving the account from the token exactly as
    /// <c>ChangeMyPassword</c> does. No account but the caller's can be reached from here.
    /// </summary>
    public class SendMyEmailVerificationCommandHandler(
        IAdminRepository adminRepo,
        ICurrentUserService currentUser,
        IEmailVerificationSender verificationSender,
        IUnitOfWork uow) : IRequestHandler<SendMyEmailVerificationCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(SendMyEmailVerificationCommand request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } actingId)
                return Result<bool>.Unauthorized();

            var admin = await adminRepo.GetByIdAsync(actingId, ct);
            if (admin is null) return Result<bool>.Unauthorized();

            if (string.IsNullOrWhiteSpace(admin.Email))
                return Result<bool>.Failure("Your account has no email address to confirm.");

            // Said plainly rather than sent again: a second link would retire the one already in the mailbox, and an
            // account whose address is confirmed has nothing to prove.
            if (admin.EmailVerified)
                return Result<bool>.Failure("Your email address is already confirmed.");

            // Stamps the token on the tracked entity; the unit of work persists it, so a failed send leaves no token.
            var sent = await verificationSender.SendAsync(admin, save: false, ct);
            if (!sent)
                return Result<bool>.Failure("The confirmation email could not be sent. Check the mail settings and try again.");

            await uow.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }
    }
}
