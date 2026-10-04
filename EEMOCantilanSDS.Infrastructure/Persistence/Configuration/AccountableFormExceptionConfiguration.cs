using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Configuration;

/// <summary>Append-only loss reports. A form may have several (a duplicate lost, later the triplicate), so the index is not unique.</summary>
public sealed class AccountableFormLossReportConfiguration : IEntityTypeConfiguration<AccountableFormLossReport>
{
    public void Configure(EntityTypeBuilder<AccountableFormLossReport> builder)
    {
        builder.ToTable("AccountableFormLossReports", table =>
            table.HasCheckConstraint("CK_AccountableFormLossReports_Copies", "\"CopiesLost\" BETWEEN 1 AND 7"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CopiesLost).HasConversion<int>().IsRequired();
        builder.Property(x => x.Place).HasMaxLength(AccountableFormLossReport.MaxPlaceLength);
        builder.Property(x => x.Narrative).HasMaxLength(AccountableFormLossReport.MaxNarrativeLength).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(150).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.AccountableDocumentId });
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.AccountableDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Append-only external follow-up references (RCD for a cancellation, notice for a loss).</summary>
public sealed class AccountableFormReferenceConfiguration : IEntityTypeConfiguration<AccountableFormReference>
{
    public void Configure(EntityTypeBuilder<AccountableFormReference> builder)
    {
        builder.ToTable("AccountableFormReferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(AccountableFormReference.MaxReferenceLength).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(AccountableFormReference.MaxNoteLength);
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(150).IsRequired();
        builder.HasIndex(x => new { x.MunicipalityId, x.AccountableDocumentId, x.Kind });
        builder.HasOne<AccountableDocument>().WithMany()
            .HasForeignKey(x => new { x.MunicipalityId, x.AccountableDocumentId })
            .HasPrincipalKey(x => new { x.MunicipalityId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
