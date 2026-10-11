using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class NpmOperatingScheduleConfiguration : IEntityTypeConfiguration<NpmOperatingSchedule>
    {
        public void Configure(EntityTypeBuilder<NpmOperatingSchedule> builder)
        {
            builder.ToTable("NpmOperatingSchedules", table =>
                table.HasCheckConstraint("CK_NpmOperatingSchedules_Weekdays", "\"ClosedWeekdays\" >= 0 AND \"ClosedWeekdays\" <= 127"));

            builder.HasKey(x => x.Id);

            builder.Property(x => x.EffectiveFrom).IsRequired();
            builder.Property(x => x.ClosedWeekdays).IsRequired();
            builder.Property(x => x.Remarks).HasMaxLength(300);

            // One version per effective date for a municipality: a later date adds history, the same date restates an upcoming version.
            builder.HasIndex(x => new { x.MunicipalityId, x.EffectiveFrom }).IsUnique();

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
