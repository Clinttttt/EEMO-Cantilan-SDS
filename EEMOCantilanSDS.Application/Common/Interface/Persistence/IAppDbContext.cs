using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.Suggestions;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence
{
    public interface IAppDbContext
    {
        ChangeTracker ChangeTracker { get; }
        bool HasActiveTransaction => false;
        Task<IAppDbContextTransaction> BeginSerializableTransactionAsync(CancellationToken cancellationToken = default);
        /// <summary>Serializes space-account allocation for a tenant before reading the committed numbering state.</summary>
        Task<IAppDbContextTransaction> BeginSpaceAccountTransactionAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => BeginSerializableTransactionAsync(cancellationToken);
        DbSet<Facility> Facilities { get; }
        DbSet<Municipality> Municipalities { get; }
        DbSet<OrSeriesConfig> OrSeriesConfigs { get; }
        DbSet<FacilityRate> FacilityRates { get; }
        DbSet<RevenueClassification> RevenueClassifications { get; }
        DbSet<OfficialReportRevision> OfficialReportRevisions { get; }
        DbSet<RevenueClassificationPolicy> RevenueClassificationPolicies { get; }
        DbSet<EEMOCantilanSDS.Domain.Entities.Revenue.Collection> Collections { get; }
        DbSet<EEMOCantilanSDS.Domain.Entities.Revenue.CollectionLine> CollectionLines { get; }
        DbSet<Payor> Payors { get; }
        DbSet<CollectionAllocation> CollectionAllocations { get; }
        DbSet<WebCollectionDraft> WebCollectionDrafts { get; }
        DbSet<WebCollectionDraftLine> WebCollectionDraftLines { get; }
        DbSet<WebCollectionDraftAllocation> WebCollectionDraftAllocations { get; }
        DbSet<PostingOperation> PostingOperations { get; }
        DbSet<CollectionSettlementCutover> CollectionSettlementCutovers { get; }
        DbSet<AccountableFormBook> AccountableFormBooks { get; }
        DbSet<AccountableDocument> AccountableDocuments { get; }
        DbSet<AccountableFormAssignment> AccountableFormAssignments { get; }
        DbSet<CollectionCorrection> CollectionCorrections { get; }
        DbSet<CollectionCorrectionLine> CollectionCorrectionLines { get; }
        DbSet<CollectionCorrectionAllocation> CollectionCorrectionAllocations { get; }

    /// <summary>An office's own market sections' daily fees, effective-dated like every other rate here.</summary>
    DbSet<FacilitySectionRate> FacilitySectionRates { get; }

    /// <summary>Whether stalls in an office's own section are metered, as a default for a stall recorded there.</summary>
    DbSet<FacilitySectionUtilities> FacilitySectionUtilities { get; }
    DbSet<FacilitySectionClosure> FacilitySectionClosures { get; }
        DbSet<Stall> Stalls { get; }
        DbSet<Contract> Contracts { get; }
        DbSet<PaymentRecord> PaymentRecords { get; }
        DbSet<DailyCollection> DailyCollections { get; }
        DbSet<UtilityBill> UtilityBills { get; }
        DbSet<StallMonthlyException> StallMonthlyExceptions { get; }
        DbSet<NpmMarketClosure> NpmMarketClosures { get; }
        DbSet<OnlinePaymentTransaction> OnlinePaymentTransactions { get; }
        DbSet<SlaughterTransaction> SlaughterTransactions { get; }
        DbSet<SlaughterAnimalRate> SlaughterAnimalRates { get; }
        DbSet<SlaughterAnimalLabel> SlaughterAnimalLabels { get; }
        DbSet<TpmVendor> TpmVendors { get; }
        DbSet<TpmAttendance> TpmAttendances { get; }
    DbSet<TpmMarketDaySchedule> TpmMarketDaySchedules { get; }
        DbSet<TrmTransporter> TrmTransporters { get; }
        DbSet<TrmTrip> TrmTrips { get; }
        DbSet<BaseUser> Users { get; }
        DbSet<AdminUser> AdminUsers { get; }
        DbSet<CollectorUser> CollectorUsers { get; }
        DbSet<PayorUser> PayorUsers { get; }
        DbSet<PayorActivationCode> PayorActivationCodes { get; }
        DbSet<PayorStallLink> PayorStallLinks { get; }
        DbSet<CollectorFacilityAssignment> CollectorFacilityAssignments { get; }
        DbSet<CollectorOperationAssignment> CollectorOperationAssignments { get; }
        DbSet<EEMOCantilanSDS.Domain.Entities.Revenue.CollectorOperationActivation> CollectorOperationActivations { get; }
        DbSet<GovernedService> GovernedServices { get; }
        DbSet<GovernedServiceSetting> GovernedServiceSettings { get; }
        DbSet<PenaltyDefinition> PenaltyDefinitions { get; }
        DbSet<ObligationAccount> ObligationAccounts { get; }
        DbSet<ObligationRate> ObligationRates { get; }
        DbSet<ObligationPeriod> ObligationPeriods { get; }
        DbSet<VehicleClass> VehicleClasses { get; }
        DbSet<VehicleClassRate> VehicleClassRates { get; }
        DbSet<GovernedServiceFeeOption> GovernedServiceFeeOptions { get; }
        DbSet<GovernedServiceFeeOptionRate> GovernedServiceFeeOptionRates { get; }
        DbSet<CollectionRemittance> CollectionRemittances { get; }
        DbSet<CollectionRemittanceCoverage> CollectionRemittanceCoverages { get; }
        DbSet<AccountableFormSpoilage> AccountableFormSpoilages { get; }
        DbSet<AccountableFormLossReport> AccountableFormLossReports { get; }
        DbSet<AccountableFormReference> AccountableFormReferences { get; }
        DbSet<AuditLog> AuditLogs { get; }
        DbSet<HiddenSuggestion> HiddenSuggestions { get; }
        DbSet<EEMOCantilanSDS.Domain.Entities.Onboarding.AssessmentRequest> AssessmentRequests { get; }
        DbSet<EEMOCantilanSDS.Domain.Entities.Onboarding.OnboardingDraft> OnboardingDrafts { get; }
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
