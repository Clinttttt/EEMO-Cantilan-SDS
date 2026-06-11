using EEMOCantilanSDS.Domain.Entities.Suggestions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Configuration;

public class HiddenSuggestionConfiguration : IEntityTypeConfiguration<HiddenSuggestion>
{
    public void Configure(EntityTypeBuilder<HiddenSuggestion> builder)
    {
        builder.ToTable("HiddenSuggestions");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnType("uuid");

        builder.Property(h => h.Type)
            .IsRequired()
            .HasColumnType("integer");

        builder.Property(h => h.Value)
            .IsRequired()
            .HasColumnType("character varying(150)");

        // Audit fields
        builder.Property(h => h.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(h => h.CreatedBy).HasColumnType("character varying(100)");
        builder.Property(h => h.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(h => h.UpdatedBy).HasColumnType("character varying(100)");

        // Soft delete
        builder.Property(h => h.IsDeleted).IsRequired().HasColumnType("boolean");
        builder.Property(h => h.DeletedAt).HasColumnType("timestamp with time zone");
        builder.Property(h => h.DeletedBy).HasColumnType("character varying(100)");

        // One blocklist entry per (type, value)
        builder.HasIndex(h => new { h.Type, h.Value }).IsUnique();
    }
}
