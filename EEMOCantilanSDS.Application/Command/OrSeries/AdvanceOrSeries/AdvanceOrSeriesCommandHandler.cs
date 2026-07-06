using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OrSeries.AdvanceOrSeries
{
    public class AdvanceOrSeriesCommandHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<AdvanceOrSeriesCommand, Result<string>>
    {
        public async Task<Result<string>> Handle(AdvanceOrSeriesCommand request, CancellationToken ct)
        {
            // Auto-scoped to the caller's municipality (tracked so the increment persists).
            var cfg = await context.OrSeriesConfigs.FirstOrDefaultAsync(ct);

            if (cfg is null)
                return Result<string>.Failure("No OR series is configured for this municipality.");
            if (!cfg.IsEnabled)
                return Result<string>.Failure("The OR series is disabled.");

            var next = cfg.Advance(currentUser.Username ?? "System");
            await context.SaveChangesAsync(ct);

            return Result<string>.Success(next);
        }
    }
}
