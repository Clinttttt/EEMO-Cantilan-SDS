using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class FineAssessmentPolicyConfiguration : IEntityTypeConfiguration<FineAssessmentPolicy>
{
    public void Configure(EntityTypeBuilder<FineAssessmentPolicy> b)
    {
        b.ToTable("FineAssessmentPolicies", t => {
            t.HasCheckConstraint("CK_FinePolicy_Source", "(\"AccountKind\" = 1 AND \"Facility\" IS NOT NULL) OR (\"AccountKind\" IN (2,3,4) AND \"Facility\" IS NULL)");
            t.HasCheckConstraint("CK_FinePolicy_DateBasis", "\"DateBasis\" IN (1,2)");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        b.HasAlternateKey(x => new { x.MunicipalityId, x.Id, x.PenaltyVersionId, x.AccountKind });
        b.Property(x => x.GoverningReference).HasMaxLength(200).IsRequired();
        b.HasOne<PenaltyDefinition>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.PenaltyVersionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class FineAssessmentConfiguration : IEntityTypeConfiguration<FineAssessment>
{
    public void Configure(EntityTypeBuilder<FineAssessment> b)
    {
        b.ToTable("FineAssessments", t => {
            t.HasCheckConstraint("CK_FineAssessment_Amount", "\"AssessedAmount\" > 0");
            t.HasCheckConstraint("CK_FineAssessment_Dates", "\"ViolationDate\" <= \"BusinessDate\"");
            t.HasCheckConstraint("CK_FineAssessment_Source", "(\"AccountKind\" = 1 AND \"ContractId\" IS NOT NULL AND \"ObligationAccountId\" IS NULL) OR (\"AccountKind\" IN (2,3,4) AND \"ContractId\" IS NULL AND \"ObligationAccountId\" IS NOT NULL)");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        b.Property(x => x.AssessedAmount).HasPrecision(18, 2);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.Occupant).HasMaxLength(200).IsRequired();
        b.Property(x => x.UnitReference).HasMaxLength(120).IsRequired();
        b.Property(x => x.SourceName).HasMaxLength(120).IsRequired();
        b.Property(x => x.PenaltyCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.PenaltyName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.IncidentReference).HasMaxLength(200);
        b.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        b.HasIndex(x => new { x.MunicipalityId, x.ContractId, x.ObligationAccountId, x.PenaltyCode, x.IncidentReference })
            .IsUnique().AreNullsDistinct(false).HasFilter("\"IncidentReference\" IS NOT NULL");
        b.HasIndex(x => new { x.MunicipalityId, x.BusinessDate });
        b.HasOne<FineAssessmentPolicy>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.PolicyId, x.PenaltyVersionId, x.AccountKind })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id, x.PenaltyVersionId, x.AccountKind }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PenaltyDefinition>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.PenaltyVersionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Contract>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ContractId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ObligationAccount>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ObligationAccountId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Municipality>().WithMany().HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class FineAssessmentEventConfiguration : IEntityTypeConfiguration<FineAssessmentEvent>
{
    public void Configure(EntityTypeBuilder<FineAssessmentEvent> b)
    {
        b.ToTable("FineAssessmentEvents"); b.HasKey(x => x.Id);
        b.Property(x => x.EventKind).HasMaxLength(40); b.Property(x => x.PreviousAmount).HasPrecision(18, 2);
        b.Property(x => x.ResultingAmount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.MunicipalityId, x.AssessmentId });
        b.HasOne<FineAssessment>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.AssessmentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
