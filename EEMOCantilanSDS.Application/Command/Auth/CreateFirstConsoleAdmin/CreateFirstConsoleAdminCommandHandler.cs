using EEMOCantilanSDS.Application.Common.Interface.Security;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Auth.CreateFirstConsoleAdmin
{
    /// <summary>
    /// Creates the one dedicated console operator, first run only.
    ///
    /// <para>
    /// The account is provisioned under the DEFAULT municipality for tenant context, which means it shares
    /// that municipality's uniqueness rules: <c>Users</c> is uniquely indexed on (MunicipalityId, Username)
    /// and on (MunicipalityId, Email), neither filtered on soft-delete. Both are therefore checked here, in
    /// the same terms the database uses. They were not, and the consequence was a lie: an e-mail address the
    /// default LGU's Head already held reached the insert, Postgres raised a unique violation, the middleware
    /// turned that into a 409, and the console reads any 409 as "an operator already exists" — so the office
    /// was told setup was finished while <c>/status</c> kept correctly answering that it had not begun.
    /// </para>
    /// </summary>
    public class CreateFirstConsoleAdminCommandHandler(
        IAppDbContext context,
        IPasswordHasher passwordHasher,
        IEmailVerificationSender emailVerificationSender)
        : IRequestHandler<CreateFirstConsoleAdminCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(CreateFirstConsoleAdminCommand request, CancellationToken ct)
        {
            // Only one first-run: refuse once any platform operator exists. Stated in words, because a bare
            // 409 leaves the console to guess the reason — and it guessed this one for every conflict.
            var operatorExists = await context.AdminUsers
                .IgnoreQueryFilters()
                .AnyAsync(u => u.IsPlatformOperator && !u.IsDeleted, ct);
            if (operatorExists)
                return Result<bool>.Failure(
                    "A platform operator already exists for this console. Sign in with that account.",
                    ResultStatus.Conflict);

            var defaultMunicipalityId = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => (System.Guid?)m.Id)
                .FirstOrDefaultAsync(ct);
            if (defaultMunicipalityId is null)
                return Result<bool>.Failure("The platform is not initialized yet.");

            var username = request.Username.Trim();
            var email = request.Email.Trim();

            // Both checks deliberately IGNORE soft-delete, because the unique indexes do: a removed account
            // still holds its username and e-mail address, so excluding deleted rows here would let the
            // request through only to fail in the database with no explanation the office could act on.
            var usernameTaken = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.MunicipalityId == defaultMunicipalityId && u.Username == username, ct);
            if (usernameTaken)
                return Result<bool>.Failure(
                    $"The username '{username}' is already in use. Choose a different one for the operator account.");

            var emailTaken = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.MunicipalityId == defaultMunicipalityId && u.Email == email, ct);
            if (emailTaken)
                return Result<bool>.Failure(
                    "That e-mail address already belongs to an account on this platform. The operator account "
                    + "needs its own address — it is a separate account from any municipality's Head.");

            // Dedicated console operator: platform-operator flag set, own password (no forced change),
            // provisioned under the default municipality for tenant context.
            var operatorAdmin = AdminUser.Create(
                request.FullName.Trim(),
                username,
                email,
                passwordHasher.Hash(request.Password),
                AdminRole.SuperAdmin,
                defaultMunicipalityId.Value,
                isActive: true,
                isPlatformOperator: true,
                mustChangePassword: false);

            context.AdminUsers.Add(operatorAdmin);
            await context.SaveChangesAsync(ct);

            // The address is confirmed the same way every other account's is, and for a reason that only shows up much
            // later: a self-service password reset is only ever sent to a VERIFIED address, and nothing verified this
            // one. So the operator — the one account with nobody above it to restore its access — was the single account
            // on the platform that could never reset its own password. The email is best-effort and never throws, so an
            // unconfigured or failing mailer cannot turn the platform's own setup into a failure.
            await emailVerificationSender.SendAsync(operatorAdmin, save: true, ct);

            return Result<bool>.Success(true);
        }
    }
}
