using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class PayorHistoricalAccessCodeConfiguration : IEntityTypeConfiguration<PayorHistoricalAccessCode>
    {
        public void Configure(EntityTypeBuilder<PayorHistoricalAccessCode> builder)
        {
            builder.ToTable("PayorHistoricalAccessCodes", t => t.HasCheckConstraint("CK_PayorHistoricalAccessCodes_OneIdentity",
                "(\"ContractId\" IS NOT NULL) <> (\"ObligationAccountId\" IS NOT NULL)"));
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Code).IsRequired().HasMaxLength(40);
            builder.HasIndex(x => x.Code).IsUnique();
            builder.Property(x => x.ContactNumber).IsRequired().HasMaxLength(20);
            builder.Property(x => x.PayerName).IsRequired().HasMaxLength(200);
            builder.Property(x => x.ExpiresAt).IsRequired();
            builder.Property(x => x.IsUsed).IsRequired().HasDefaultValue(false);
            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);
            builder.HasIndex(x => new { x.MunicipalityId, x.ContractId });
            builder.HasIndex(x => new { x.MunicipalityId, x.ObligationAccountId });
            // Same-tenant integrity in the database, not only in the application: a code or link can only name a contract / obligation account /
            // municipality of its OWN tenant. A nullable half of the XOR pair is not enforced (match simple), the check constraint decides which half is set.
            builder.HasOne<Contract>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ContractId })
                .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<ObligationAccount>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ObligationAccountId })
                .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<Municipality>().WithMany().HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class PayorLiabilityLinkConfiguration : IEntityTypeConfiguration<PayorLiabilityLink>
    {
        public void Configure(EntityTypeBuilder<PayorLiabilityLink> builder)
        {
            builder.ToTable("PayorLiabilityLinks", t => t.HasCheckConstraint("CK_PayorLiabilityLinks_OneIdentity",
                "(\"ContractId\" IS NOT NULL) <> (\"ObligationAccountId\" IS NOT NULL)"));
            builder.HasKey(x => x.Id);
            builder.Property(x => x.PayorUserId).IsRequired();
            builder.Property(x => x.LinkedBy).IsRequired().HasMaxLength(100);
            // A payor holds each liable identity at most once.
            builder.HasIndex(x => new { x.PayorUserId, x.ContractId }).IsUnique().HasFilter("\"ContractId\" IS NOT NULL");
            builder.HasIndex(x => new { x.PayorUserId, x.ObligationAccountId }).IsUnique().HasFilter("\"ObligationAccountId\" IS NOT NULL");
            // The payor must belong to the link's own tenant (Users carries the (MunicipalityId, Id) key).
            builder.HasOne<PayorUser>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.PayorUserId })
                .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            // Same-tenant integrity in the database, not only in the application: a code or link can only name a contract / obligation account /
            // municipality of its OWN tenant. A nullable half of the XOR pair is not enforced (match simple), the check constraint decides which half is set.
            builder.HasOne<Contract>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ContractId })
                .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<ObligationAccount>().WithMany().HasForeignKey(x => new { x.MunicipalityId, x.ObligationAccountId })
                .HasPrincipalKey(x => new { x.MunicipalityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne<Municipality>().WithMany().HasForeignKey(x => x.MunicipalityId).OnDelete(DeleteBehavior.Restrict);

        }
    }
}
