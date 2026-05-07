using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Configuration
{
    public class SlaughterTransactionConfiguration : IEntityTypeConfiguration<SlaughterTransaction>
    {
        public void Configure(EntityTypeBuilder<SlaughterTransaction> builder)
        {
            builder.ToTable("SlaughterTransactions");

            builder.HasKey(st => st.Id);

            builder.Property(st => st.FacilityId)
                .IsRequired();

            builder.Property(st => st.CollectorId);

            builder.Property(st => st.OwnerName)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(st => st.AnimalType)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(st => st.CustomAnimalType)
                .HasMaxLength(100);

            builder.Property(st => st.NumberOfHeads)
                .IsRequired();

            builder.Property(st => st.RatePerHead)
                .IsRequired()
                .HasPrecision(18, 2);

            builder.Ignore(st => st.TotalAmount);

            builder.Property(s=> s.ORNumber)
                .HasMaxLength(50);

            builder.Property(s => s.TransactionDate);

            builder.Property(x => x.SlaughterFee).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.SlaughterPermit).HasPrecision(18, 2);  
            builder.Property(x => x.AntemortemFee).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.PostmortemFee).HasPrecision(18, 2);   
            builder.Property(x => x.TableCharge).IsRequired().HasPrecision(18, 2);
            builder.Property(x => x.EntranceFee).HasPrecision(18, 2);     
            builder.Property(x => x.LivestockFee).HasPrecision(18, 2);

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
