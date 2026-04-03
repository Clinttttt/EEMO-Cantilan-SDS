using EEMOCantilanSDS.Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Configuration
{
    public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
    {
        public void Configure(EntityTypeBuilder<AuditLog> builder)
        {
            builder.ToTable("AuditLogs");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ActorId)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(x => x.ActorName)
                   .IsRequired()
                   .HasMaxLength(150);

            builder.Property(x => x.ActorRole)
                   .IsRequired()
                   .HasMaxLength(50);

            builder.Property(x => x.Action)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(x => x.EntityType)
                   .IsRequired()
                   .HasMaxLength(100);

            builder.Property(x => x.EntityId);

            builder.Property(x => x.OldValues).HasColumnType("text");
            builder.Property(x => x.NewValues).HasColumnType("text");

            builder.Property(x => x.IPAddress).HasMaxLength(50);
            builder.Property(x => x.Notes).HasMaxLength(500);

            builder.Property(x => x.LoggedAt).IsRequired();

            // AuditLog is immutable — no soft delete needed
            builder.HasIndex(x => x.ActorId);
            builder.HasIndex(x => new { x.EntityType, x.EntityId });
            builder.HasIndex(x => x.LoggedAt);
        }
    }
}
