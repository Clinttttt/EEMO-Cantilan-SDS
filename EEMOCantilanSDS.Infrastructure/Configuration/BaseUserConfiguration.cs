using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Configuration
{
    public class BaseUserConfiguration : IEntityTypeConfiguration<BaseUser>
    {
        public void Configure(EntityTypeBuilder<BaseUser> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(s => s.Id);

            builder.HasDiscriminator<string>("UserType")
                  .HasValue<AdminUser>("Admin")
                  .HasValue<CollectorUser>("Collector")
                  .HasValue<PayorUser>("Payor");

            builder.Property(s => s.FullName)
                .HasMaxLength(100);

         builder.HasIndex(x => x.Username).IsUnique();
            builder.HasIndex(x => x.Email).IsUnique();

            builder.Property(s=> s.PasswordHash)
                .IsRequired()
                .HasMaxLength(500); 

            builder.Property(s => s.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(s => s.MustChangePassword)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(s=> s.FailedAttempts)
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(s => s.LockedUntil);
            builder.Property(s => s.LastLoginAt);
            builder.Property(s => s.RefreshToken);
            builder.Property(s => s.RefreshTokenExpiryTime);

            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.CreatedBy).HasMaxLength(100);
            builder.Property(x => x.UpdatedAt);
            builder.Property(x => x.UpdatedBy).HasMaxLength(100);
            builder.Property(x => x.IsDeleted).HasDefaultValue(false);
            builder.Property(x => x.DeletedAt);
            builder.Property(x => x.DeletedBy).HasMaxLength(100);
        }
    }
}
