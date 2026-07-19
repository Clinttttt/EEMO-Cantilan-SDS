using EEMOCantilanSDS.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public class CollectorDeviceTokenConfiguration : IEntityTypeConfiguration<CollectorDeviceToken>
{
    public void Configure(EntityTypeBuilder<CollectorDeviceToken> builder)
    {
        builder.ToTable("CollectorDeviceTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.CollectorId).IsRequired();
        // FCM tokens are ~150-300 chars; 512 is a safe cap that stays well under the Postgres btree
        // unique-index size limit while comfortably holding any real token.
        builder.Property(t => t.Token).IsRequired().HasMaxLength(512);
        builder.Property(t => t.Platform).IsRequired().HasMaxLength(20);
        builder.Property(t => t.MunicipalityId).IsRequired();

        // A device has exactly one token — the natural key for idempotent upserts.
        builder.HasIndex(t => t.Token).IsUnique();
        builder.HasIndex(t => t.CollectorId);
    }
}
