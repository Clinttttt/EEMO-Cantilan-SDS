using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectorOperationActivationConfiguration : IEntityTypeConfiguration<CollectorOperationActivation>
{
    public void Configure(EntityTypeBuilder<CollectorOperationActivation> builder)
    {
        builder.ToTable("CollectorOperationActivations", table =>
            table.HasCheckConstraint("CK_CollectorOperationActivations_OperationCode",
                "\"OperationCode\" ~ '^[A-Z0-9_]{1,64}$'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActivatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActivatedAtUtc).IsRequired();
        builder.Property(x => x.EffectiveFrom).IsRequired();
        // One activation per tenant and operation: a concurrent second Enable fails here and resolves to the first.
        builder.HasIndex(x => new { x.MunicipalityId, x.OperationCode }).IsUnique();

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
