using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken
{
    public class SetAdminPasswordByTokenCommandHandler(IAppDbContext context)
        : IRequestHandler<SetAdminPasswordByTokenCommand, Result<bool>>
    {
        private const string GenericError = "This activation link is invalid or has expired.";

        public async Task<Result<bool>> Handle(SetAdminPasswordByTokenCommand request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return Result<bool>.Failure(GenericError);

            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

            // Anonymous flow: the token is the secret, so look it up across all municipalities (the request
            // carries no tenant). A matching, unexpired, unused token is required.
            var user = await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.ActivationTokenHash == hash, ct);

            if (user is null || !user.IsActivationTokenValid(hash))
                return Result<bool>.Failure(GenericError);

            // Optional: the activating user may choose their own sign-in username. Normalize to trimmed
            // lower-case (matches the existing convention + the case-sensitive login lookup), validate the
            // format, and enforce uniqueness within their own municipality. Only applied when it actually
            // changes, so re-activation with the pre-set username is a no-op.
            if (!string.IsNullOrWhiteSpace(request.NewUsername))
            {
                var desired = request.NewUsername.Trim().ToLowerInvariant();

                if (desired.Length < 3 || desired.Length > 30
                    || !desired.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
                    return Result<bool>.Failure(
                        "Username must be 3–30 characters using only letters, numbers, dot, underscore, or hyphen.");

                if (!string.Equals(desired, user.Username, StringComparison.OrdinalIgnoreCase))
                {
                    var taken = await context.Users
                        .IgnoreQueryFilters()
                        .AnyAsync(u => u.MunicipalityId == user.MunicipalityId
                            && u.Id != user.Id
                            && !u.IsDeleted
                            && u.Username != null
                            && u.Username.ToLower() == desired, ct);
                    if (taken)
                        return Result<bool>.Failure("That username is already taken. Please choose another.");

                    user.SetUsername(desired);
                }
            }

            // Domain hashes the password and clears the one-time token (single use).
            user.CompleteActivation(request.NewPassword);
            await context.SaveChangesAsync(ct);

            return Result<bool>.Success(true);
        }
    }
}
