using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.Suggestions;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence
{
    public interface IAppDbContext
    {
        DbSet<Facility> Facilities { get; }
        DbSet<Stall> Stalls { get; }
        DbSet<Contract> Contracts { get; }
        DbSet<PaymentRecord> PaymentRecords { get; }
        DbSet<DailyCollection> DailyCollections { get; }
        DbSet<SlaughterTransaction> SlaughterTransactions { get; }
        DbSet<TpmVendor> TpmVendors { get; }
        DbSet<TpmAttendance> TpmAttendances { get; }
        DbSet<TrmTransporter> TrmTransporters { get; }
        DbSet<TrmTrip> TrmTrips { get; }
        DbSet<BaseUser> Users { get; }
        DbSet<AdminUser> AdminUsers { get; }
        DbSet<CollectorUser> CollectorUsers { get; }
        DbSet<CollectorFacilityAssignment> CollectorFacilityAssignments { get; }
        DbSet<AuditLog> AuditLogs { get; }
        DbSet<HiddenSuggestion> HiddenSuggestions { get; }
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
