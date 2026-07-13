using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public class TenantBackupConfiguration : IEntityTypeConfiguration<TenantBackup>
{
    public void Configure(EntityTypeBuilder<TenantBackup> builder)
    {
        builder.ToTable("TenantBackups");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnType("uuid");

        builder.Property(b => b.MunicipalityId).IsRequired().HasColumnType("uuid");

        builder.Property(b => b.CreatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(b => b.CreatedBy).IsRequired().HasColumnType("character varying(100)");
        builder.Property(b => b.FormatVersion).IsRequired().HasColumnType("character varying(32)");

        builder.Property(b => b.RowCount).IsRequired().HasColumnType("integer");
        builder.Property(b => b.TableCount).IsRequired().HasColumnType("integer");
        builder.Property(b => b.SizeBytes).IsRequired().HasColumnType("bigint");

        // The full round-trippable snapshot payload — stored verbatim as text so a restore reads the exact
        // bytes that were captured (no jsonb re-encoding).
        builder.Property(b => b.SnapshotJson).IsRequired().HasColumnType("text");

        builder.Property(b => b.Note).HasColumnType("character varying(120)");

        // Newest-first listing per municipality.
        builder.HasIndex(b => new { b.MunicipalityId, b.CreatedAtUtc });
    }
}
