using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class AccountableDocumentConfiguration : IEntityTypeConfiguration<AccountableDocument>
{
    public void Configure(EntityTypeBuilder<AccountableDocument> builder)
    {
        builder.ToTable("AccountableDocuments");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.FormBookId).IsRequired();
        builder.Property(x => x.InstrumentType).HasConversion<int>().IsRequired();
        builder.Property(x => x.SerialNumber).IsRequired();
        builder.Property(x => x.DocumentNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.State).HasConversion<int>().IsRequired().IsConcurrencyToken();
        builder.Property(x => x.AssignedUserId);
        builder.Property(x => x.CollectionId);
        builder.Property(x => x.ClientOperationId);
        builder.Property(x => x.ConsumedAtUtc);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy).HasMaxLength(100);
        builder.HasIndex(x => new { x.MunicipalityId, x.InstrumentType, x.DocumentNumber }).IsUnique();
        builder.HasIndex(x => new { x.MunicipalityId, x.FormBookId, x.SerialNumber }).IsUnique();
        builder.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId })
            .IsUnique().HasFilter("\"ClientOperationId\" IS NOT NULL");
        builder.HasIndex(x => new { x.MunicipalityId, x.State, x.AssignedUserId });
        builder.HasOne<AccountableFormBook>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.FormBookId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Collection>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
