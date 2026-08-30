using EEMOCantilanSDS.Domain.Entities.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class FacilitySectionClosureConfiguration : IEntityTypeConfiguration<FacilitySectionClosure>
    {
        public void Configure(EntityTypeBuilder<FacilitySectionClosure> builder)
        {
            builder.ToTable("FacilitySectionClosures");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MunicipalityId).IsRequired();
            builder.Property(x => x.FacilityCode).IsRequired().HasConversion<int>();
            builder.Property(x => x.SectionName).IsRequired().HasMaxLength(60);
            builder.Property(x => x.ClosedOn).IsRequired();

            // The stalls this act closed, so a reopen returns exactly those. Native uuid[], for the same reason a
            // facility's section names are a text[]: one row, one short list, and no join table to keep in step.
            builder.Property(x => x.ClosedStallIds)
                .HasColumnType("uuid[]")
                .HasDefaultValueSql("'{}'::uuid[]");

            // One row per municipality + facility + section: a section is either closed now or it is not. Whether it was
            // closed before is the audit trail's business, not this table's.
            builder.HasIndex(x => new { x.MunicipalityId, x.FacilityCode, x.SectionName })
                .IsUnique();

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
