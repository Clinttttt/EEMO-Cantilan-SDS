using System.Security.Cryptography;
using System.Text;
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

            // Domain hashes the password and clears the one-time token (single use).
            user.CompleteActivation(request.NewPassword);
            await context.SaveChangesAsync(ct);

            return Result<bool>.Success(true);
        }
    }
}
