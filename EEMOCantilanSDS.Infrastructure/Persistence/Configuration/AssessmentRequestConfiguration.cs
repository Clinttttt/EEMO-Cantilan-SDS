using EEMOCantilanSDS.Domain.Entities.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class AssessmentRequestConfiguration : IEntityTypeConfiguration<AssessmentRequest>
    {
        public void Configure(EntityTypeBuilder<AssessmentRequest> builder)
        {
            builder.ToTable("AssessmentRequests");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Municipality).IsRequired().HasMaxLength(120);
            builder.Property(x => x.Province).IsRequired().HasMaxLength(120);
            builder.Property(x => x.RequestingOffice).IsRequired().HasMaxLength(160);
            builder.Property(x => x.FocalPerson).IsRequired().HasMaxLength(120);
            builder.Property(x => x.Position).IsRequired().HasMaxLength(120);
            builder.Property(x => x.OfficialEmail).IsRequired().HasMaxLength(160);
            builder.Property(x => x.ContactNumber).IsRequired().HasMaxLength(40);
            builder.Property(x => x.FacilitiesManaged).IsRequired().HasMaxLength(500);
            builder.Property(x => x.ApproxVendors).HasMaxLength(60);
            builder.Property(x => x.AuthorizationStatus).HasMaxLength(120);
            builder.Property(x => x.Acknowledged).IsRequired().HasDefaultValue(false);
            builder.Property(x => x.Notes).HasMaxLength(1000);

            builder.Property(x => x.Status)
                .IsRequired()
                .HasConversion<int>();

            builder.Property(x => x.Stage).IsRequired().HasMaxLength(30);
            builder.Property(x => x.DecisionMessage).HasMaxLength(2000);
            builder.Property(x => x.OnboardingLink).HasMaxLength(300);
            builder.Property(x => x.SubmittedAt).IsRequired();

            builder.HasIndex(x => x.Status);
            builder.HasIndex(x => x.SubmittedAt);

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
