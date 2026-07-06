using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.Suggestions;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence
{
    public class AppDbContext : DbContext, IAppDbContext
    {
        private readonly ICurrentMunicipalityAccessor? _municipality;

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // DI constructor: supplies the tenant accessor used by the municipality global query filter and
        // the write-stamping interceptor. The options-only ctor above keeps bare/test construction working.
        public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentMunicipalityAccessor municipality) : base(options)
        {
            _municipality = municipality;
        }

        /// <summary>
        /// The municipality the current context is scoped to. <see cref="Guid.Empty"/> when unresolved
        /// (no accessor / not yet loaded, e.g. bare test contexts) — in which case the tenant filter is a
        /// no-op and nothing is hidden. Read live so the value resolved at startup is always current.
        /// </summary>
        public Guid CurrentMunicipalityId => _municipality?.MunicipalityId ?? Guid.Empty;



        public DbSet<Facility> Facilities { get; set; }
        public DbSet<Municipality> Municipalities { get; set; }
        public DbSet<FacilityRate> FacilityRates { get; set; }
        public DbSet<Stall> Stalls { get; set; }
        public DbSet<Contract> Contracts { get; set; }

 
        public DbSet<PaymentRecord> PaymentRecords { get; set; }
        public DbSet<DailyCollection> DailyCollections { get; set; }
        public DbSet<UtilityBill> UtilityBills { get; set; }
        public DbSet<StallMonthlyException> StallMonthlyExceptions { get; set; }
        public DbSet<NpmMarketClosure> NpmMarketClosures { get; set; }
        public DbSet<OnlinePaymentTransaction> OnlinePaymentTransactions { get; set; }

        public DbSet<SlaughterTransaction> SlaughterTransactions { get; set; }
        public DbSet<SlaughterAnimalRate> SlaughterAnimalRates { get; set; }

        public DbSet<TpmVendor> TpmVendors { get; set; }
        public DbSet<TpmAttendance> TpmAttendances { get; set; }

        public DbSet<TrmTransporter> TrmTransporters { get; set; }
        public DbSet<TrmTrip> TrmTrips { get; set; }


        public DbSet<BaseUser> Users { get; set; }
        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<CollectorUser> CollectorUsers { get; set; }
        public DbSet<PayorUser> PayorUsers { get; set; }

        public DbSet<PayorActivationCode> PayorActivationCodes { get; set; }
        public DbSet<PayorStallLink> PayorStallLinks { get; set; }

      
        public DbSet<CollectorFacilityAssignment> CollectorFacilityAssignments { get; set; }


        public DbSet<AuditLog> AuditLogs { get; set; }

        public DbSet<HiddenSuggestion> HiddenSuggestions { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
            ApplyQueryFilters(modelBuilder);
        }

        private static readonly MethodInfo OwnedAuditableFilterMethod =
            typeof(AppDbContext).GetMethod(nameof(SetOwnedAuditableFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo OwnedFilterMethod =
            typeof(AppDbContext).GetMethod(nameof(SetOwnedFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo SoftDeleteFilterMethod =
            typeof(AppDbContext).GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>
        /// Applies global query filters per entity (TPH root) type, combining:
        ///  • soft-delete (<c>!IsDeleted</c>) for <see cref="AuditableEntity"/> types, and
        ///  • municipality isolation for <see cref="IMunicipalityOwned"/> types.
        /// The municipality clause is a no-op while <see cref="CurrentMunicipalityId"/> is empty (unresolved /
        /// tests), so this cannot change Cantilan's single-tenant results. EF re-evaluates the id per query.
        /// </summary>
        private void ApplyQueryFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null))
            {
                var clr = entityType.ClrType;
                var owned = typeof(IMunicipalityOwned).IsAssignableFrom(clr);
                var auditable = typeof(AuditableEntity).IsAssignableFrom(clr);

                var method = (owned, auditable) switch
                {
                    (true, true) => OwnedAuditableFilterMethod,
                    (true, false) => OwnedFilterMethod,
                    (false, true) => SoftDeleteFilterMethod,
                    _ => null
                };

                method?.MakeGenericMethod(clr).Invoke(this, new object[] { modelBuilder });
            }
        }

        // Tenant-owned + soft-deletable: hide soft-deleted rows AND rows of other municipalities.
        private void SetOwnedAuditableFilter<T>(ModelBuilder modelBuilder) where T : class =>
            modelBuilder.Entity<T>().HasQueryFilter(e =>
                !EF.Property<bool>(e, nameof(AuditableEntity.IsDeleted))
                && (CurrentMunicipalityId == Guid.Empty
                    || EF.Property<Guid>(e, nameof(IMunicipalityOwned.MunicipalityId)) == CurrentMunicipalityId));

        // Tenant-owned but not soft-deletable (e.g. AuditLog, join links): municipality isolation only.
        private void SetOwnedFilter<T>(ModelBuilder modelBuilder) where T : class =>
            modelBuilder.Entity<T>().HasQueryFilter(e =>
                CurrentMunicipalityId == Guid.Empty
                || EF.Property<Guid>(e, nameof(IMunicipalityOwned.MunicipalityId)) == CurrentMunicipalityId);

        // Not tenant-owned (e.g. Municipality) but soft-deletable: preserve the original soft-delete filter.
        private void SetSoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : class =>
            modelBuilder.Entity<T>().HasQueryFilter(e => !EF.Property<bool>(e, nameof(AuditableEntity.IsDeleted)));
    }
}
