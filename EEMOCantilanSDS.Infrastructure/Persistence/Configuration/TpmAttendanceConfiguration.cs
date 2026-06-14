using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public class TpmAttendanceConfiguration : IEntityTypeConfiguration<TpmAttendance>
{
    public void Configure(EntityTypeBuilder<TpmAttendance> builder)
    {
        builder.ToTable("TpmAttendances");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnType("uuid");

        builder.Property(a => a.VendorId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(a => a.CollectorId)
            .HasColumnType("uuid");

        builder.Property(a => a.MarketDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(a => a.Fee)
            .IsRequired()
            .HasColumnType("numeric(18,2)");

        builder.Property(a => a.IsPaid)
            .IsRequired()
            .HasColumnType("boolean");

        builder.Property(a => a.ORNumber)
            .HasColumnType("character varying(50)");

        builder.Property(a => a.PaidAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(a => a.Remarks)
            .HasColumnType("text");

        // Audit fields
        builder.Property(a => a.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(a => a.CreatedBy)
            .HasColumnType("character varying(100)");

        builder.Property(a => a.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(a => a.UpdatedBy)
            .HasColumnType("character varying(100)");

        // Soft delete
        builder.Property(a => a.IsDeleted)
            .IsRequired()
            .HasColumnType("boolean");

        builder.Property(a => a.DeletedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(a => a.DeletedBy)
            .HasColumnType("character varying(100)");

        // Unique constraint: one attendance record per vendor per Friday
        builder.HasIndex(a => new { a.VendorId, a.MarketDate })
            .IsUnique();

        // Navigation
        builder.HasOne(a => a.Vendor)
            .WithMany(v => v.Attendances)
            .HasForeignKey(a => a.VendorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
