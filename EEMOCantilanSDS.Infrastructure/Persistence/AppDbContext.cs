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

        /// <summary>
        /// Whether this context was given a tenant accessor at all.
        ///
        /// <para>The options-only constructor supplies none: design-time tooling, migrations and much of the test suite
        /// build contexts that way, and they legitimately work across tenants. A context that HAS an accessor and still
        /// resolves to nothing is a different thing entirely — a request that should have had a tenant and does not —
        /// and the write stamp treats the two differently rather than reading <see cref="Guid.Empty"/> as both.</para>
        /// </summary>
        public bool HasTenantAccessor => _municipality is not null;



        public DbSet<Facility> Facilities { get; set; }
        public DbSet<Municipality> Municipalities { get; set; }
        public DbSet<TenantBackup> TenantBackups { get; set; }
        public DbSet<OrSeriesConfig> OrSeriesConfigs { get; set; }
        public DbSet<FacilityRate> FacilityRates { get; set; }
        public DbSet<FacilitySectionRate> FacilitySectionRates { get; set; }
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
    public DbSet<TpmMarketDaySchedule> TpmMarketDaySchedules { get; set; }

        public DbSet<TrmTransporter> TrmTransporters { get; set; }
        public DbSet<TrmTrip> TrmTrips { get; set; }


        public DbSet<BaseUser> Users { get; set; }
        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<CollectorUser> CollectorUsers { get; set; }
        public DbSet<PayorUser> PayorUsers { get; set; }

        public DbSet<PayorActivationCode> PayorActivationCodes { get; set; }
        public DbSet<PayorStallLink> PayorStallLinks { get; set; }

      
        public DbSet<CollectorFacilityAssignment> CollectorFacilityAssignments { get; set; }

        public DbSet<EEMOCantilanSDS.Domain.Entities.Notifications.CollectorDeviceToken> CollectorDeviceTokens { get; set; }


        public DbSet<AuditLog> AuditLogs { get; set; }

        public DbSet<HiddenSuggestion> HiddenSuggestions { get; set; }

        public DbSet<EEMOCantilanSDS.Domain.Entities.Onboarding.AssessmentRequest> AssessmentRequests { get; set; }

        public DbSet<EEMOCantilanSDS.Domain.Entities.Onboarding.OnboardingDraft> OnboardingDrafts { get; set; }


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
        ///
        /// <para>
        /// Attached by walking the model rather than listing types, so a new tenant-owned entity is isolated the moment it
        /// is added. <c>TenantFilterCoverageTests</c> asserts that, and reads each filter rather than merely checking one
        /// exists: a soft-deletable entity always has a filter, so its presence says nothing about LGU isolation.
        /// </para>
        ///
        /// <para>
        /// The municipality clause FAILS CLOSED. When no accessor is present at all — migrations, tooling, seeding — the
        /// clause is skipped, which is what lets those paths work. But a context that HAS an accessor and cannot resolve a
        /// municipality reads nothing rather than everything. It was the other way round once, and the comment here used to
        /// describe that behaviour long after it changed.
        /// </para>
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
        //
        // FAIL CLOSED. An unresolved tenant now matches NOTHING, where it used to make the filter a no-op and return
        // every LGU's rows - the worst possible answer to "I don't know who is asking". A context built with NO accessor
        // is a different case and still sees everything: design-time tooling, migrations and much of the test suite are
        // built that way and legitimately work across tenants, which is why Guid.Empty could not carry both meanings.
        //
        // Deliberate cross-tenant work stays possible and stays visible, through IgnoreQueryFilters() at the call site -
        // login, the seeders, backup and the platform-operator paths already read that way.
        private void SetOwnedAuditableFilter<T>(ModelBuilder modelBuilder) where T : class =>
            modelBuilder.Entity<T>().HasQueryFilter(e =>
                !EF.Property<bool>(e, nameof(AuditableEntity.IsDeleted))
                && (!HasTenantAccessor
                    || (CurrentMunicipalityId != Guid.Empty
                        && EF.Property<Guid>(e, nameof(IMunicipalityOwned.MunicipalityId)) == CurrentMunicipalityId)));

        // Tenant-owned but not soft-deletable (e.g. AuditLog, join links): municipality isolation only.
        private void SetOwnedFilter<T>(ModelBuilder modelBuilder) where T : class =>
            modelBuilder.Entity<T>().HasQueryFilter(e =>
                !HasTenantAccessor
                || (CurrentMunicipalityId != Guid.Empty
                    && EF.Property<Guid>(e, nameof(IMunicipalityOwned.MunicipalityId)) == CurrentMunicipalityId));

        // Not tenant-owned (e.g. Municipality) but soft-deletable: preserve the original soft-delete filter.
        private void SetSoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : class =>
            modelBuilder.Entity<T>().HasQueryFilter(e => !EF.Property<bool>(e, nameof(AuditableEntity.IsDeleted)));
    }
}
