using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class NpmMarketClosureConfiguration : IEntityTypeConfiguration<NpmMarketClosure>
    {
        public void Configure(EntityTypeBuilder<NpmMarketClosure> builder)
        {
            builder.ToTable("NpmMarketClosures");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ClosureDate).IsRequired();
            builder.Property(x => x.Reason).IsRequired().HasConversion<int>();
            builder.Property(x => x.Remarks).HasMaxLength(300);

            // One active closure per date (the market is facility-wide). Filtered on IsDeleted as a
            // safe backstop so a cleared row could never block re-closing the same date.
            builder.HasIndex(x => new { x.MunicipalityId, x.ClosureDate })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

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
