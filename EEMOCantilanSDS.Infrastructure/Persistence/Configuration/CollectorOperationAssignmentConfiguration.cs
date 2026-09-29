using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public sealed class CollectorOperationAssignmentConfiguration : IEntityTypeConfiguration<CollectorOperationAssignment>
{
    public void Configure(EntityTypeBuilder<CollectorOperationAssignment> builder)
    {
        builder.ToTable("CollectorOperationAssignments", table =>
            table.HasCheckConstraint("CK_CollectorOperationAssignments_OperationCode",
                "\"OperationCode\" ~ '^[A-Z0-9_]{1,64}$'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AssignedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AssignedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.CollectorId, x.OperationCode }).IsUnique();

        builder.HasOne<Municipality>().WithMany()
            .HasForeignKey(x => x.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
