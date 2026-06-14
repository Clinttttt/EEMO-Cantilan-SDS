using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class CollectorFacilityAssignmentConfiguration : IEntityTypeConfiguration<CollectorFacilityAssignment>
    {
        public void Configure(EntityTypeBuilder<CollectorFacilityAssignment> builder)
        {
            builder.ToTable("CollectorFacilityAssignments");

            builder.HasKey(x => x.Id);

            builder.Property(s=> s.CollectorId)
                .IsRequired();

            builder.Property(s => s.FacilityId)
                .IsRequired();

            builder.Property(s => s.FacilityCode)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(x => x.AssignedAt).IsRequired();

            builder.HasOne(s => s.Collector)
                .WithMany(c => c.FacilityAssignments)
                .HasForeignKey(s => s.CollectorId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);


            builder.HasOne(s => s.Facility)
                .WithMany(s => s.CollectorAssignments)
                .HasForeignKey(s => s.FacilityId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }
    }
}
