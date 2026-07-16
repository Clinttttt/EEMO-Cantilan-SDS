using EEMOCantilanSDS.Domain.Entities.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class FacilityConfiguration : IEntityTypeConfiguration<Facility>
    {
        public void Configure(EntityTypeBuilder<Facility> builder)
        {
            builder.ToTable("Facilities");

            builder.HasKey(x => x.Id);

            builder.Property(s => s.Code)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(s => s.Archetype)
                .IsRequired()
                .HasConversion<int>();
            
            builder.Property(s => s.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(s => s.ShortName)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(s => s.Description)
                .HasMaxLength(500);

            builder.Property(s => s.VegetableSectionLabel).HasMaxLength(60);
            builder.Property(s => s.FishSectionLabel).HasMaxLength(60);
            builder.Property(s => s.MeatSectionLabel).HasMaxLength(60);

            builder.Property(s => s.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.HasIndex(s => new { s.MunicipalityId, s.Code }).IsUnique();

            builder.HasMany(s=> s.Stalls)
                .WithOne(s=> s.Facility)
                .HasForeignKey(s=> s.FacilityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(s => s.CollectorAssignments)
                .WithOne(s => s.Facility)
                .HasForeignKey(s => s.FacilityId)
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
