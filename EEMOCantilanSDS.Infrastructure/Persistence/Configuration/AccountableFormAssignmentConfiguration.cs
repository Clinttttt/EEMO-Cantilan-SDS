using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class AccountableFormAssignmentConfiguration : IEntityTypeConfiguration<AccountableFormAssignment>
{
    public void Configure(EntityTypeBuilder<AccountableFormAssignment> builder)
    {
        builder.ToTable("AccountableFormAssignments");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.MunicipalityId, x.Id });
        builder.Property(x => x.MunicipalityId).IsRequired();
        builder.Property(x => x.AccountableDocumentId).IsRequired();
        builder.Property(x => x.AssignedUserId).IsRequired();
        builder.Property(x => x.AssignedByActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AssignedAtUtc).IsRequired();
        builder.Property(x => x.ReturnedAtUtc);
        builder.Property(x => x.ReturnedByActorId).HasMaxLength(100);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy).HasMaxLength(100);
        builder.HasIndex(x => new { x.MunicipalityId, x.AccountableDocumentId })
            .IsUnique().HasFilter("\"ReturnedAtUtc\" IS NULL AND \"IsDeleted\" = false");
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.AccountableDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
