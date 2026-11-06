using EEMOCantilanSDS.Domain.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public class PaymentInstalmentConfiguration : IEntityTypeConfiguration<PaymentInstalment>
{
    public void Configure(EntityTypeBuilder<PaymentInstalment> builder)
    {
        builder.ToTable("PaymentInstalments");
        builder.HasKey(x => x.Id);
        // The domain sets the key when a receipt is taken. Declared never generated, a receipt a tracked record gains is saved as
        // a new row; left "generated", EF reads the set key as an existing row and the save updates nothing and fails.
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).IsRequired().HasPrecision(18, 2);
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.ORNumber).HasMaxLength(50);
        builder.HasIndex(x => new { x.MunicipalityId, x.PaymentRecordId });
        builder.HasIndex(x => new { x.MunicipalityId, x.ReceivedAt });
        builder.HasIndex(x => new { x.MunicipalityId, x.ORNumber });
        builder.HasOne<PaymentRecord>().WithMany(p => p.Instalments)
            .HasForeignKey(x => x.PaymentRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
