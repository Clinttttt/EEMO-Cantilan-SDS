using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class OnlinePaymentTransactionConfiguration : IEntityTypeConfiguration<OnlinePaymentTransaction>
    {
        public void Configure(EntityTypeBuilder<OnlinePaymentTransaction> builder)
        {
            builder.ToTable("OnlinePaymentTransactions");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Reference)
                .IsRequired()
                .HasMaxLength(40);
            builder.HasIndex(x => x.Reference).IsUnique();

            builder.Property(x => x.PayorUserId).IsRequired();

            // Monthly-rental targets link a PaymentRecord; NPM daily-month targets leave it null and carry
            // the stall + billing month instead. TargetKind defaults to MonthlyRecord for existing rows.
            builder.Property(x => x.TargetKind)
                .IsRequired()
                .HasConversion<int>()
                .HasDefaultValue(EEMOCantilanSDS.Domain.Enums.OnlinePaymentTargetKind.MonthlyRecord);
            builder.Property(x => x.PaymentRecordId);   // nullable
            builder.Property(x => x.TargetStallId);
            builder.Property(x => x.TargetYear);
            builder.Property(x => x.TargetMonth);

            builder.Property(x => x.Amount)
                .IsRequired()
                .HasColumnType("numeric(18,2)");

            builder.Property(x => x.Status)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(x => x.Provider)
                .IsRequired()
                .HasMaxLength(40);

            builder.Property(x => x.GatewayReference).HasMaxLength(100);
            // Dedupe key for webhooks — unique, but multiple NULLs allowed (pre-gateway rows) in PostgreSQL.
            builder.HasIndex(x => x.GatewayReference).IsUnique();

            builder.Property(x => x.CheckoutUrl).HasMaxLength(500);

            builder.Property(x => x.PaymentId).HasMaxLength(100);
            builder.Property(x => x.Method).HasMaxLength(40);
            builder.Property(x => x.PaidAt);
            builder.Property(x => x.RawPayload).HasColumnType("jsonb");
            builder.Property(x => x.ORNumber).HasMaxLength(50);

            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedAt);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);

            builder.HasOne(x => x.PaymentRecord)
                .WithMany()
                .HasForeignKey(x => x.PaymentRecordId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
