using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class PostingOperationConfiguration : IEntityTypeConfiguration<PostingOperation>
{
    public void Configure(EntityTypeBuilder<PostingOperation> builder)
    {
        builder.ToTable("PostingOperations");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.ClientOperationId).IsRequired();
        builder.Property(x => x.IntentVersion).IsRequired();
        builder.Property(x => x.NormalizedIntent).HasColumnType("text").IsRequired();
        builder.Property(x => x.IntentFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Origin).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.OutcomeCode).HasMaxLength(80);
        builder.Property(x => x.OutcomeDetails).HasColumnType("text");
        builder.Property(x => x.CollectionId);
        builder.Property(x => x.AccountableDocumentId);
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.ClientOperationId }).IsUnique();
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectionId });
        builder.HasOne<Collection>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.CollectionId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.AccountableDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
