using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class StallMonthlyExceptionConfiguration : IEntityTypeConfiguration<StallMonthlyException>
    {
        public void Configure(EntityTypeBuilder<StallMonthlyException> builder)
        {
            builder.ToTable("StallMonthlyExceptions");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.StallId).IsRequired();
            builder.Property(x => x.BillingYear).IsRequired();
            builder.Property(x => x.BillingMonth).IsRequired();
            builder.Property(x => x.Reason).IsRequired().HasConversion<int>();
            builder.Property(x => x.Remarks).HasMaxLength(300);

            // One active exception per stall-month. Filtered on IsDeleted as a safe backstop so a
            // cleared row could never block re-excusing the same month.
            builder.HasIndex(x => new { x.StallId, x.BillingYear, x.BillingMonth })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            builder.HasOne(x => x.Stall)
                .WithMany()
                .HasForeignKey(x => x.StallId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedAt);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);
        }
    }
}
