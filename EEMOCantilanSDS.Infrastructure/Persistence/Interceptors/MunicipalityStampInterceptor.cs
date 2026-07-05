using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// Stamps <see cref="IMunicipalityOwned.MunicipalityId"/> on inserted tenant-owned entities so every
    /// row is attributed to a municipality. The id is resolved per-request off the current
    /// <see cref="AppDbContext"/> (<see cref="AppDbContext.CurrentMunicipalityId"/>), which reflects the
    /// authenticated user's municipality, falling back to the default (Cantilan) for token-less flows.
    /// Rows that already carry a municipality id are left untouched; if the context is unresolved
    /// (<see cref="Guid.Empty"/>), rows are left unstamped (single-tenant / test path unchanged).
    /// </summary>
    public sealed class MunicipalityStampInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Stamp(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Stamp(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void Stamp(DbContext? context)
        {
            if (context is null) return;

            var pending = context.ChangeTracker.Entries<IMunicipalityOwned>()
                .Where(e => e.State == EntityState.Added && e.Entity.MunicipalityId == Guid.Empty)
                .ToList();
            if (pending.Count == 0) return;

            // Resolve the current request's municipality off the context (per-request, Phase 5).
            var municipalityId = context is AppDbContext db ? db.CurrentMunicipalityId : Guid.Empty;
            if (municipalityId == Guid.Empty) return; // unresolved tenant — leave unstamped

            foreach (var entry in pending)
                entry.Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = municipalityId;
        }
    }
}
