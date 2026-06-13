using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Configuration
{
    public class PayorUserConfiguration : IEntityTypeConfiguration<PayorUser>
    {
        public void Configure(EntityTypeBuilder<PayorUser> builder)
        {
            // TPH subclass — shares the "Users" base table and key (no HasKey here).
            // No new scalar columns: payors reuse Username (contact number), PasswordHash, etc.
            builder.HasMany(p => p.StallLinks)
                .WithOne(l => l.Payor)
                .HasForeignKey(l => l.PayorUserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(x => x.IsLockedOut);
        }
    }
}
