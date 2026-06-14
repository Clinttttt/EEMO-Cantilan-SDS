using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public class TrmTransporterConfiguration : IEntityTypeConfiguration<TrmTransporter>
{
    public void Configure(EntityTypeBuilder<TrmTransporter> builder)
    {
        builder.ToTable("TrmTransporters");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnType("uuid");

        builder.Property(t => t.Name)
            .IsRequired()
            .HasColumnType("character varying(200)");

        builder.Property(t => t.Organization)
            .IsRequired()
            .HasColumnType("character varying(200)");

        builder.Property(t => t.DefaultRoute)
            .IsRequired()
            .HasColumnType("character varying(200)");

        builder.Property(t => t.PlateNumber)
            .IsRequired()
            .HasColumnType("character varying(50)");

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasColumnType("boolean");

        builder.Property(t => t.Remarks)
            .HasColumnType("text");

        builder.Property(t => t.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(t => t.CreatedBy).HasColumnType("character varying(100)");
        builder.Property(t => t.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.UpdatedBy).HasColumnType("character varying(100)");
        builder.Property(t => t.IsDeleted).IsRequired().HasColumnType("boolean");
        builder.Property(t => t.DeletedAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.DeletedBy).HasColumnType("character varying(100)");

        builder.HasMany(t => t.Trips)
            .WithOne(tr => tr.Transporter)
            .HasForeignKey(tr => tr.TransporterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
