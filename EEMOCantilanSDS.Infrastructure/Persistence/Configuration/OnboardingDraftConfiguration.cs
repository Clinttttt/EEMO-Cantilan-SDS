using EEMOCantilanSDS.Domain.Entities.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration
{
    public class OnboardingDraftConfiguration : IEntityTypeConfiguration<OnboardingDraft>
    {
        public void Configure(EntityTypeBuilder<OnboardingDraft> builder)
        {
            builder.ToTable("OnboardingDrafts");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.AssessmentRequestId).IsRequired();
            builder.Property(x => x.Municipality).IsRequired().HasMaxLength(120);
            builder.Property(x => x.Province).IsRequired().HasMaxLength(120);

            builder.Property(x => x.Token).IsRequired().HasMaxLength(200);

            // Opaque draft configuration document.
            builder.Property(x => x.ConfigJson).HasColumnType("jsonb");

            builder.Property(x => x.IsSubmittedForValidation).IsRequired().HasDefaultValue(false);
            builder.Property(x => x.SubmittedAt);
            builder.Property(x => x.ExpiresAt).IsRequired();

            builder.HasIndex(x => x.Token).IsUnique();
            builder.HasIndex(x => x.AssessmentRequestId);

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
