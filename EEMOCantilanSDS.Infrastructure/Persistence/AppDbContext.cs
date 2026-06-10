using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.Suggestions;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence
{
    public class AppDbContext : DbContext, IAppDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }



        public DbSet<Facility> Facilities { get; set; }
        public DbSet<Stall> Stalls { get; set; }
        public DbSet<Contract> Contracts { get; set; }

 
        public DbSet<PaymentRecord> PaymentRecords { get; set; }
        public DbSet<DailyCollection> DailyCollections { get; set; }

        public DbSet<SlaughterTransaction> SlaughterTransactions { get; set; }

        public DbSet<TpmVendor> TpmVendors { get; set; }
        public DbSet<TpmAttendance> TpmAttendances { get; set; }

        public DbSet<TrmTransporter> TrmTransporters { get; set; }
        public DbSet<TrmTrip> TrmTrips { get; set; }


        public DbSet<BaseUser> Users { get; set; }
        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<CollectorUser> CollectorUsers { get; set; }

      
        public DbSet<CollectorFacilityAssignment> CollectorFacilityAssignments { get; set; }


        public DbSet<AuditLog> AuditLogs { get; set; }

        public DbSet<HiddenSuggestion> HiddenSuggestions { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }
}
