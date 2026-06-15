using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public class TrmTripConfiguration : IEntityTypeConfiguration<TrmTrip>
{
    public void Configure(EntityTypeBuilder<TrmTrip> builder)
    {
        builder.ToTable("TrmTrips");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnType("uuid");

        builder.Property(t => t.TransporterId).HasColumnType("uuid");
        builder.Property(t => t.CollectorId).HasColumnType("uuid");

        builder.Property(t => t.TripNumber).IsRequired().HasColumnType("integer");

        builder.Property(t => t.DriverName)
            .IsRequired()
            .HasColumnType("character varying(200)");

        builder.Property(t => t.PlateNumber)
            .IsRequired()
            .HasColumnType("character varying(20)");

        builder.Property(t => t.Organization)
            .IsRequired()
            .HasColumnType("character varying(200)")
            .HasDefaultValue("Non-associated");

        builder.Property(t => t.Route)
            .IsRequired()
            .HasColumnType("character varying(200)");

        builder.Property(t => t.Fee)
            .IsRequired()
            .HasColumnType("numeric(18,2)");

        builder.Property(t => t.ORNumber)
            .HasColumnType("character varying(50)");

        // Same-table backstop for OR uniqueness (concurrency safety net).
        // Global cross-table uniqueness is enforced in the application layer.
        builder.HasIndex(t => t.ORNumber)
            .IsUnique()
            .HasFilter("\"ORNumber\" IS NOT NULL AND \"ORNumber\" <> ''");

        builder.Property(t => t.RecordedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.Remarks).HasColumnType("text");

        builder.Property(t => t.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(t => t.CreatedBy).HasColumnType("character varying(100)");
        builder.Property(t => t.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.UpdatedBy).HasColumnType("character varying(100)");
        builder.Property(t => t.IsDeleted).IsRequired().HasColumnType("boolean");
        builder.Property(t => t.DeletedAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.DeletedBy).HasColumnType("character varying(100)");

        builder.HasOne(t => t.Transporter)
            .WithMany(tr => tr.Trips)
            .HasForeignKey(t => t.TransporterId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
