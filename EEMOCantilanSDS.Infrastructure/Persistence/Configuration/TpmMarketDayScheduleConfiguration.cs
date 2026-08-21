using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class TpmMarketDayScheduleConfiguration : IEntityTypeConfiguration<TpmMarketDaySchedule>
    {
        public void Configure(EntityTypeBuilder<TpmMarketDaySchedule> builder)
        {
            builder.ToTable("TpmMarketDaySchedules");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Day).IsRequired().HasConversion<int>();
            builder.Property(x => x.EffectiveFrom).IsRequired();

            // One schedule per office per effective date: re-stating the same date replaces the decision rather
            // than leaving two answers for the same week. Filtered on IsDeleted for the same reason a closure is.
            builder.HasIndex(x => new { x.MunicipalityId, x.EffectiveFrom })
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
