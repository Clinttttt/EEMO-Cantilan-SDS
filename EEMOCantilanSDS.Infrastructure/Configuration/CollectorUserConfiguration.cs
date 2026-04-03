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
    public class CollectorUserConfiguration : IEntityTypeConfiguration<CollectorUser>
    {
        public void Configure(EntityTypeBuilder<CollectorUser> builder)
        {
            builder.Property(s => s.EmployeeId)
                .HasMaxLength(50);

            builder.Property(s => s.ContactNumber)
                .HasMaxLength(20);

            builder.Property(s => s.LastActiveAt);

            builder.HasMany(s => s.FacilityAssignments)
                .WithOne(s=> s.Collector)
                .HasForeignKey(s => s.CollectorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Ignore(x => x.IsLockedOut);


        }
    }
}
