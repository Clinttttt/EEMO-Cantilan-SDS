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
    public class ContractConfiguration : IEntityTypeConfiguration<Contract>
    {
        public void Configure(EntityTypeBuilder<Contract> builder)
        {
            builder.ToTable("Contracts");

            builder.HasKey(c => c.Id);

            builder.Property(s=> s.StallId).IsRequired();

            builder.Property(s=> s.ORNumber)
                .HasMaxLength(50);

            builder.Property(s => s.ActualOccupant)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(s => s.NameOnContract)
                .HasMaxLength(100);

            builder.Property(s => s.EffectivityDate);
            builder.Property(s=> s.DurationYears);

            builder.Property(s=> s.MonthlyRentalRate)
                .IsRequired()
                .HasPrecision(18, 2);

            builder.Property(s=> s.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(s => s.Remarks)
                .HasMaxLength(500);

            builder.HasOne(c => c.Stall)
                .WithMany(s => s.Contracts)
                .HasForeignKey(c => c.StallId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Ignore(x => x.ExpiryDate);
            builder.Ignore(x => x.WholeYearRental);
            builder.Ignore(x => x.IsExpired);
            builder.Ignore(x => x.IsExpiringSoon);

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
