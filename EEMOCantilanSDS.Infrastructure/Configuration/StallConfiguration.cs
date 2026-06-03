using EEMOCantilanSDS.Domain.Entities.Facilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Configuration
{
    public class StallConfiguration : IEntityTypeConfiguration<Stall>
    {
        public void Configure(EntityTypeBuilder<Stall> builder)
        {
            builder.ToTable("Stalls");

            builder.HasKey(s => s.Id);

            builder.Property(s=> s.FacilityId)
                .IsRequired();

            builder.Property(s => s.StallNo)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(s => s.Fees)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(s => s.Section)
                .HasConversion<int?>();

            builder.Property(s => s.AreaLocation)
                .HasConversion<int?>();

            builder.Property(s => s.AreaSqm);
            builder.Property(s => s.AreaNote)
                .HasMaxLength(200);
            builder.Property(s => s.Remarks)
                .HasColumnType("text");

            builder.Property(s=> s.MonthlyRate)
                .HasPrecision(18, 2);

            builder.Property(s => s.DailyRate)
                .HasPrecision(18, 2);

            builder.HasIndex(s => new { s.FacilityId, s.Section, s.StallNo })
                .IsUnique()
                .HasFilter("\"Section\" IS NOT NULL");

            builder.HasIndex(s => new { s.FacilityId, s.StallNo })
                .IsUnique()
                .HasFilter("\"Section\" IS NULL");
            
            builder.HasOne(s => s.Facility)
                .WithMany(f => f.Stalls)
                .HasForeignKey(s => s.FacilityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(s => s.Contracts)
                .WithOne(c => c.Stall)
                .HasForeignKey(c => c.StallId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(s=> s.PaymentRecords)
                .WithOne(pr => pr.Stall)
                .HasForeignKey(pr => pr.StallId)
                .OnDelete(DeleteBehavior.Restrict);

             builder.HasMany(s => s.DailyCollections)
                .WithOne(dc => dc.Stall)
                .HasForeignKey(dc => dc.StallId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedAt);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);

            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
