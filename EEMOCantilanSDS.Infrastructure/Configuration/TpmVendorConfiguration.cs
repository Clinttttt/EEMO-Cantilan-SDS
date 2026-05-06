using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Configuration;

public class TpmVendorConfiguration : IEntityTypeConfiguration<TpmVendor>
{
    public void Configure(EntityTypeBuilder<TpmVendor> builder)
    {
        builder.ToTable("TpmVendors");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnType("uuid");

        builder.Property(v => v.VendorName)
            .IsRequired()
            .HasColumnType("character varying(200)");

        builder.Property(v => v.Goods)
            .IsRequired()
            .HasColumnType("character varying(200)");

        builder.Property(v => v.IsActive)
            .IsRequired()
            .HasColumnType("boolean");

        builder.Property(v => v.ContactNumber)
            .HasColumnType("character varying(50)");

        builder.Property(v => v.Remarks)
            .HasColumnType("text");

        // Audit fields
        builder.Property(v => v.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.CreatedBy)
            .HasColumnType("character varying(100)");

        builder.Property(v => v.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.UpdatedBy)
            .HasColumnType("character varying(100)");

        // Soft delete
        builder.Property(v => v.IsDeleted)
            .IsRequired()
            .HasColumnType("boolean");

        builder.Property(v => v.DeletedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(v => v.DeletedBy)
            .HasColumnType("character varying(100)");

        // Navigation
        builder.HasMany(v => v.Attendances)
            .WithOne(a => a.Vendor)
            .HasForeignKey(a => a.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Global query filter for soft delete
        builder.HasQueryFilter(v => !v.IsDeleted);
    }
}
