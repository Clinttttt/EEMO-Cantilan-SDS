using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Configuration
{
    public class PayorActivationCodeConfiguration : IEntityTypeConfiguration<PayorActivationCode>
    {
        public void Configure(EntityTypeBuilder<PayorActivationCode> builder)
        {
            builder.ToTable("PayorActivationCodes");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Code)
                .IsRequired()
                .HasMaxLength(40);
            builder.HasIndex(x => x.Code).IsUnique();

            builder.Property(x => x.ContactNumber)
                .IsRequired()
                .HasMaxLength(20);

            builder.Property(x => x.StallId).IsRequired();
            builder.Property(x => x.ExpiresAt).IsRequired();
            builder.Property(x => x.IsUsed).IsRequired().HasDefaultValue(false);
            builder.Property(x => x.UsedAt);
            builder.Property(x => x.PayorUserId);

            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedAt);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);

            builder.HasOne(x => x.Stall)
                .WithMany()
                .HasForeignKey(x => x.StallId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
