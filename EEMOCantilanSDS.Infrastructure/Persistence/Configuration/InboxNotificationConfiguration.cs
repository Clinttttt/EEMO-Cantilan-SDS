using EEMOCantilanSDS.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

public class InboxNotificationConfiguration : IEntityTypeConfiguration<InboxNotification>
{
    public void Configure(EntityTypeBuilder<InboxNotification> builder)
    {
        builder.ToTable("InboxNotifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.RecipientKind).HasConversion<int>();
        builder.Property(n => n.DedupeKey).IsRequired().HasMaxLength(100);
        builder.Property(n => n.Title).IsRequired().HasMaxLength(120);
        builder.Property(n => n.Body).IsRequired().HasMaxLength(1000);
        builder.Property(n => n.Link).HasMaxLength(200);

        // One person hears about one fact once, however many passes or app instances run.
        builder.HasIndex(n => new { n.MunicipalityId, n.RecipientKind, n.RecipientId, n.DedupeKey }).IsUnique();
        builder.HasIndex(n => new { n.MunicipalityId, n.RecipientKind, n.RecipientId, n.CreatedAt });
    }
}
