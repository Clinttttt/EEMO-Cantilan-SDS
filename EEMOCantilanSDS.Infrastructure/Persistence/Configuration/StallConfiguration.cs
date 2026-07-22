using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
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

            builder.Property(s => s.Type)
                .IsRequired()
                .HasConversion<int>()
                .HasDefaultValue(StallType.Permanent);

            builder.Property(s => s.Section)
                .HasConversion<int?>();

            // Per-LGU custom NPM section name (set only when Section is null). Kept short like the section
            // display labels on Facility.
            builder.Property(s => s.CustomSectionName)
                .HasMaxLength(60);

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

            builder.Property(s => s.ClosedAt);

            builder.HasIndex(s => new { s.FacilityId, s.Section, s.StallNo })
                .IsUnique()
                .HasFilter("\"Section\" IS NOT NULL");

            // Null-section stalls (monthly-rental facilities AND NPM custom-section stalls) are kept unique
            // per (FacilityId, COALESCE(CustomSectionName,''), StallNo) via a raw expression index created in
            // migration 20260722xxxxx_ReplaceNullSectionStallUniqueIndexPerCustomSection. That lets each NPM
            // custom section number its stalls independently (1,2,3…) while non-NPM facilities (CustomSectionName
            // null → '') keep per-facility uniqueness exactly as before. It is NOT declared here because EF cannot
            // express the COALESCE; leaving it out keeps EF from trying to manage/drop it.

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
        }
    }
}
