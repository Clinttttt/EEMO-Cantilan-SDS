using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Configuration
{
    public class PayorStallLinkConfiguration : IEntityTypeConfiguration<PayorStallLink>
    {
        public void Configure(EntityTypeBuilder<PayorStallLink> builder)
        {
            builder.ToTable("PayorStallLinks");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.PayorUserId).IsRequired();
            builder.Property(x => x.StallId).IsRequired();

            // A payor links each stall at most once.
            builder.HasIndex(x => new { x.PayorUserId, x.StallId }).IsUnique();

            builder.HasOne(x => x.Stall)
                .WithMany()
                .HasForeignKey(x => x.StallId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
