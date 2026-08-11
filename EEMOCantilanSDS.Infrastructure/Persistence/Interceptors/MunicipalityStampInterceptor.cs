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
            var db = context as AppDbContext;
            var municipalityId = db?.CurrentMunicipalityId ?? Guid.Empty;

            if (municipalityId == Guid.Empty)
            {
                // No accessor at all: design-time tooling, migrations and much of the test suite build contexts that
                // way and legitimately work across tenants. Left exactly as it was.
                if (db?.HasTenantAccessor != true) return;

                // An accessor that resolves to nothing is a different thing: a write that should have had a tenant and
                // does not. Writing it anyway produced a row belonging to NOBODY — invisible to every resolved tenant,
                // visible to every unresolved one, and counted by no LGU's reports. Silence is the worst outcome here:
                // the row is created, the office is told the payment saved, and it is not in their register.
                //
                // Nothing in production writes one of these today: every seeder and the first-admin path resolve the
                // municipality themselves, and a request resolves through the user or the default. So this cannot fire
                // for a working deployment - it fires for a broken one, which is when a loud failure is worth having.
                var kinds = string.Join(", ", pending
                    .Select(e => e.Metadata.ClrType.Name)
                    .Distinct()
                    .OrderBy(n => n));

                throw new InvalidOperationException(
                    $"Refusing to save {pending.Count} tenant-owned row(s) ({kinds}) with no municipality. The tenant " +
                    "could not be resolved for this operation. A row saved without one belongs to no LGU and would be " +
                    "invisible to the office that created it.");
            }

            foreach (var entry in pending)
                entry.Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = municipalityId;
        }
    }
}
