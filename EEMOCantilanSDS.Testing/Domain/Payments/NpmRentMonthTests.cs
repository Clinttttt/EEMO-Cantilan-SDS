using System.Text.Json;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public sealed class NpmRentMonthTests
{
    internal static readonly DateTime Utc = new(2026, 10, 16, 4, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid Reviewer = Guid.Parse("706b5c57-644c-47c5-b75e-d1fd0f382c97");
    internal static NpmRentMonthAssessmentEvidence Evidence(decimal amount = 900m, decimal credits = 0m,
        NpmMonthBasis? basis = NpmMonthBasis.RentGoal) => new(amount, credits, basis, new(2025, 8, 31),
            "Fixture-only approved evidence", "{\"version\":1,\"rateReference\":\"fixture\",\"effectiveDate\":\"2025-01-01\"}",
            "{\"version\":1,\"source\":\"fixture-reviewed-credits\"}");

    internal static (Facility Facility, Stall Stall, Contract Contract) Source()
    {
        var tenant = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "Fixture market", "NPM");
        var stall = Stall.Create(facility.Id, "A-1", 900m, ApplicableFees.DailyRental);
        var contract = Contract.Create(stall.Id, "Fixture occupant", "Fixture contract", new(2025, 1, 1), 3, 900m);
        StampTenant(facility, tenant); StampTenant(stall, tenant); StampTenant(contract, tenant);
        stall.Contracts.Add(contract);
        return (facility, stall, contract);
    }

    internal static void StampTenant(object source, Guid tenant) =>
        source.GetType().GetProperty("MunicipalityId")!.SetValue(source, tenant); // Existing persisted-source fixture pattern.

    internal static NpmRentMonth Month(NpmRentMonthAssessmentEvidence? evidence = null)
    {
        var (facility, stall, contract) = Source();
        return NpmRentMonth.Prepare(facility, stall, contract, new(2025, 8, 1), new(2026, 10, 16),
            evidence ?? Evidence(), Reviewer, "Reviewer", "SuperAdmin", Utc).Month;
    }

    [Theory]
    [InlineData(NpmMonthBasis.RentGoal)]
    [InlineData(NpmMonthBasis.PureDays)]
    [InlineData(null)]
    public void Preparation_records_exact_identity_and_explicit_basis_without_activating_or_inventing_cash(NpmMonthBasis? basis)
    {
        var (facility, stall, contract) = Source();
        var (month, audit) = NpmRentMonth.Prepare(facility, stall, contract, new(2025, 8, 1), new(2026, 10, 16),
            Evidence(basis: basis), Reviewer, "Reviewer", "SuperAdmin", Utc);
        Assert.Equal(facility.MunicipalityId, month.MunicipalityId);
        Assert.Equal(facility.Id, month.FacilityId); Assert.Equal(stall.Id, month.StallId);
        Assert.Equal(contract.Id, month.ContractId); Assert.Equal(new DateOnly(2025, 8, 1), month.PeriodStart);
        Assert.Equal(basis, month.MonthBasis);
        Assert.Equal(900m, month.AssessedAmount); Assert.Equal(0m, month.CreditedAmount);
        Assert.Equal(1L, month.SettlementVersion);
        Assert.Equal(NpmRentMonthPreparationStatus.NeedsReconciliation, month.Status);
        Assert.False(month.CanAcceptCollections);
        Assert.Equal(month.Id, audit.EntityId); Assert.Equal(month.MunicipalityId, audit.MunicipalityId);
        Assert.Equal("SuperAdmin", audit.ActorRole); Assert.Null(audit.OldValues);
        Assert.Contains("900", audit.NewValues!);
    }

    [Theory]
    [InlineData(900, 900)]
    [InlineData(0, 0)]
    [InlineData(1900, 0)]
    public void Approved_amount_and_credits_are_recorded_not_recomputed_from_daily_rates(decimal amount, decimal credits)
    {
        var month = Month(Evidence(amount, credits));
        Assert.Equal(amount, month.AssessedAmount); Assert.Equal(credits, month.CreditedAmount);
        Assert.False(month.CanAcceptCollections);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(31)]
    public void Non_month_start_periods_are_rejected(int day)
    {
        var (facility, stall, contract) = Source();
        Assert.Throws<ArgumentException>(() => NpmRentMonth.Prepare(facility, stall, contract,
            new(2025, 8, day), new(2026, 10, 16), Evidence(), Reviewer, "Reviewer", "SuperAdmin", Utc));
    }

    [Theory]
    [InlineData("facility-tenant")]
    [InlineData("stall-tenant")]
    [InlineData("contract-tenant")]
    [InlineData("empty-tenant")]
    [InlineData("empty-facility-id")]
    [InlineData("empty-stall-id")]
    [InlineData("empty-contract-id")]
    [InlineData("wrong-facility")]
    [InlineData("wrong-stall")]
    [InlineData("unrecorded-contract")]
    [InlineData("other-operation")]
    public void Preparation_fails_closed_on_unverified_source_ownership(string mismatch)
    {
        var (facility, stall, contract) = Source();
        switch (mismatch)
        {
            case "facility-tenant": StampTenant(facility, Guid.NewGuid()); break;
            case "stall-tenant": StampTenant(stall, Guid.NewGuid()); break;
            case "contract-tenant": StampTenant(contract, Guid.NewGuid()); break;
            case "empty-tenant": StampTenant(facility, Guid.Empty); StampTenant(stall, Guid.Empty); StampTenant(contract, Guid.Empty); break;
            case "empty-facility-id": typeof(Facility).GetProperty(nameof(Facility.Id))!.SetValue(facility, Guid.Empty); break;
            case "empty-stall-id": typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, Guid.Empty); break;
            case "empty-contract-id": typeof(Contract).GetProperty(nameof(Contract.Id))!.SetValue(contract, Guid.Empty); break;
            case "wrong-facility": typeof(Stall).GetProperty(nameof(Stall.FacilityId))!.SetValue(stall, Guid.NewGuid()); break;
            case "wrong-stall": typeof(Contract).GetProperty(nameof(Contract.StallId))!.SetValue(contract, Guid.NewGuid()); break;
            case "unrecorded-contract": stall.Contracts.Clear(); break;
            case "other-operation": facility = Facility.Create(FacilityCode.NCC, "Other", "NCC"); break;
        }
        Assert.Throws<ArgumentException>(() => NpmRentMonth.Prepare(facility, stall, contract,
            new(2025, 8, 1), new(2026, 10, 16), Evidence(), Reviewer, "Reviewer", "SuperAdmin", Utc));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(900, -1)]
    [InlineData(900, 901)]
    [InlineData(0.001, 0)]
    [InlineData(900, 0.001)]
    public void Invalid_assessment_or_credits_cannot_be_prepared_or_revised(decimal amount, decimal credits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Month(Evidence(amount, credits)));
        var month = Month();
        Assert.Throws<ArgumentOutOfRangeException>(() => month.ReviseAssessment(Evidence(amount, credits), 1,
            Reviewer, "Reviewer", "SuperAdmin", Utc, "Reviewed correction"));
        Assert.Equal(900m, month.AssessedAmount); Assert.Equal(1L, month.SettlementVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("invalid json")]
    public void Missing_or_invalid_rate_or_credit_provenance_is_rejected(string evidence)
    {
        Assert.Throws<ArgumentException>(() => Month(Evidence() with { RateEvidenceJson = evidence }));
        Assert.Throws<ArgumentException>(() => Month(Evidence() with { CreditEvidenceJson = evidence }));
    }

    [Fact]
    public void Invalid_basis_and_unbounded_or_unreferenced_evidence_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Month(Evidence(basis: (NpmMonthBasis)99)));
        Assert.Throws<ArgumentException>(() => Month(Evidence() with { PolicyReference = " " }));
        Assert.Throws<ArgumentException>(() => Month(Evidence() with { RateEvidenceJson = new string('a', 16385) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Month(Evidence(10_000_000_000_000_000m)));
    }

    [Fact]
    public void Ambiguous_same_start_contracts_require_reconciliation_instead_of_guessing()
    {
        var (facility, stall, contract) = Source();
        var other = Contract.Create(stall.Id, "Other fixture", "Other", contract.EffectivityDate, 3, 900m);
        StampTenant(other, facility.MunicipalityId); stall.Contracts.Add(other);
        Assert.Throws<InvalidOperationException>(() => NpmRentMonth.Prepare(facility, stall, contract,
            new(2025, 8, 1), new(2026, 10, 16), Evidence(), Reviewer, "Reviewer", "SuperAdmin", Utc));
        Assert.Throws<InvalidOperationException>(() => NpmRentMonth.Prepare(facility, stall, other,
            new(2025, 8, 1), new(2026, 10, 16), Evidence(), Reviewer, "Reviewer", "SuperAdmin", Utc));
    }

    [Fact]
    public void Handover_uses_canonical_month_owner_and_still_requires_explicit_review()
    {
        var (facility, stall, old) = Source();
        var current = Contract.Create(stall.Id, "New fixture", "New", new(2025, 8, 15), 3, 900m);
        StampTenant(current, facility.MunicipalityId); stall.Contracts.Add(current);
        Assert.Throws<InvalidOperationException>(() => NpmRentMonth.Prepare(facility, stall, old,
            new(2025, 8, 1), new(2026, 10, 16), Evidence(), Reviewer, "Reviewer", "SuperAdmin", Utc));
        var month = NpmRentMonth.Prepare(facility, stall, current, new(2025, 8, 1), new(2026, 10, 16),
            Evidence(), Reviewer, "Reviewer", "SuperAdmin", Utc).Month;
        Assert.Equal(current.Id, month.ContractId);
        Assert.Equal(NpmRentMonthPreparationStatus.NeedsReconciliation, month.Status);
        Assert.Empty(stall.DailyCollections); // No old cash is imported/transferred by owner selection.
    }

    [Fact]
    public void Review_and_revision_preserve_identity_capture_old_new_audit_and_never_activate_collection()
    {
        var month = Month(); var id = month.Id; var owner = month.ContractId;
        var reviewed = month.Reconcile(1, Reviewer, "Reviewer", "SuperAdmin", Utc, "Fixture owner/rates/credits reviewed; no activation");
        Assert.Equal(NpmRentMonthPreparationStatus.Reconciled, month.Status);
        Assert.Equal(2L, month.SettlementVersion); Assert.False(month.CanAcceptCollections);
        Assert.NotNull(reviewed.OldValues); Assert.NotNull(reviewed.NewValues);
        var audit = month.ReviseAssessment(Evidence(1000, 25), 2, Reviewer, "Reviewer", "SuperAdmin", Utc, "Approved fixture revision");
        using var old = JsonDocument.Parse(audit.OldValues!); using var next = JsonDocument.Parse(audit.NewValues!);
        Assert.Equal(900m, old.RootElement.GetProperty("AssessedAmount").GetDecimal());
        Assert.Equal(1000m, next.RootElement.GetProperty("AssessedAmount").GetDecimal());
        Assert.Equal(id, month.Id); Assert.Equal(owner, month.ContractId);
        Assert.Equal(3L, month.SettlementVersion); Assert.Equal(25m, month.CreditedAmount);
        Assert.Equal(NpmRentMonthPreparationStatus.NeedsReconciliation, month.Status);
        Assert.Null(month.ReconciledAtUtc); Assert.Null(month.ReconciledByUserId); Assert.Null(month.ReconciliationEvidence);
        Assert.Equal("Approved fixture revision", audit.Notes); Assert.Equal(month.MunicipalityId, audit.MunicipalityId);
        Assert.False(month.CanAcceptCollections);
    }

    [Fact]
    public void Stale_unaudited_backdated_or_unknown_basis_reviews_fail_without_mutation()
    {
        var month = Month();
        Assert.Throws<InvalidOperationException>(() => month.Reconcile(2, Reviewer, "Reviewer", "SuperAdmin", Utc, "Review"));
        Assert.Throws<ArgumentException>(() => month.Reconcile(1, Guid.Empty, "Reviewer", "SuperAdmin", Utc, "Review"));
        Assert.Throws<ArgumentException>(() => month.Reconcile(1, Reviewer, "Reviewer", "", Utc, "Review"));
        Assert.Throws<ArgumentException>(() => month.Reconcile(1, Reviewer, "Reviewer", "SuperAdmin", Utc, " "));
        Assert.Throws<ArgumentException>(() => month.Reconcile(1, Reviewer, "Reviewer", "SuperAdmin", Utc.AddSeconds(-1), "Review"));
        Assert.Throws<ArgumentException>(() => month.Reconcile(1, Reviewer, "Reviewer", "SuperAdmin", DateTime.SpecifyKind(Utc, DateTimeKind.Local), "Review"));
        Assert.Equal(1L, month.SettlementVersion); Assert.Null(month.UpdatedAtUtc);
        var unknown = Month(Evidence(basis: null));
        Assert.Throws<InvalidOperationException>(() => unknown.Reconcile(1, Reviewer, "Reviewer", "SuperAdmin", Utc, "No inferred basis"));
        Assert.Null(unknown.MonthBasis); Assert.Equal(1L, unknown.SettlementVersion);
    }

    [Fact]
    public void Review_is_not_activation_and_explicit_activation_is_audited_without_cash()
    {
        var month = Month();
        Assert.Throws<InvalidOperationException>(() => month.ActivateSettlement(1, Reviewer, "Head", "SuperAdmin", Utc, "Too early"));
        month.Reconcile(1, Reviewer, "Head", "SuperAdmin", Utc, "Reviewed synthetic opening evidence");
        Assert.False(month.CanAcceptCollections);
        Assert.Throws<InvalidOperationException>(() => month.RecordSettlementChange(2, Reviewer, "Collector", "Collector", Utc, "Review is not permission to collect"));
        Assert.Throws<InvalidOperationException>(() => month.ActivateSettlement(2, Reviewer, "Collector", "Collector", Utc, "Not authorized"));
        var audit = month.ActivateSettlement(2, Reviewer, "Head", "SuperAdmin", Utc, "Explicit fixture-only activation");
        Assert.True(month.CanAcceptCollections); Assert.Equal(Reviewer, month.SettlementActivatedByUserId);
        Assert.Equal(Utc, month.SettlementActivatedAtUtc); Assert.Equal(3, month.SettlementVersion);
        Assert.Equal("ActivateNpmRentMonthSettlement", audit.Action); Assert.NotNull(audit.OldValues); Assert.NotNull(audit.NewValues);
        Assert.Null(month.SettlementSealedAtUtc); // Activation itself creates no payment evidence.
        Assert.Throws<InvalidOperationException>(() => month.ActivateSettlement(3, Reviewer, "Head", "SuperAdmin", Utc, "Cannot activate twice"));
        Assert.Throws<InvalidOperationException>(() => month.ReviseAssessment(Evidence(1000), 3, Reviewer, "Head", "SuperAdmin", Utc, "Cannot overwrite an activated assessment"));
    }
}
