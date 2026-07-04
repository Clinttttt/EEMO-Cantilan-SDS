using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Interceptors
{
    /// <summary>
    /// Stamps <see cref="IMunicipalityOwned.MunicipalityId"/> on inserted tenant-owned entities so every
    /// row is attributed to a municipality. Until users are municipality-scoped (Phase 5), this is the
    /// <b>default</b> municipality (Cantilan), resolved from the DB once and cached process-wide (its id
    /// is stable). Rows that already carry a municipality id are left untouched; if no default
    /// municipality exists yet, rows are left unstamped (the Phase 3 filter is added only after backfill).
    /// </summary>
    public sealed class MunicipalityStampInterceptor : SaveChangesInterceptor
    {
        private static Guid _defaultMunicipalityId;

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

            var municipalityId = ResolveDefaultMunicipalityId(context);
            if (municipalityId == Guid.Empty) return; // no default municipality yet — leave unstamped

            foreach (var entry in pending)
                entry.Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = municipalityId;
        }

        // Default municipality id is stable, so resolve once from the DB and cache it for the process.
        // Bypasses query filters so a soft-delete/tenant filter can never hide it.
        private static Guid ResolveDefaultMunicipalityId(DbContext context)
        {
            if (_defaultMunicipalityId != Guid.Empty) return _defaultMunicipalityId;

            var id = context.Set<Municipality>().IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => m.Id)
                .FirstOrDefault();

            if (id != Guid.Empty) _defaultMunicipalityId = id;
            return id;
        }
    }
}
