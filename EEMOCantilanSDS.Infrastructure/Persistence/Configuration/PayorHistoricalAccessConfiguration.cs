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
            builder.HasOne<PayorUser>().WithMany().HasForeignKey(x => x.PayorUserId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
