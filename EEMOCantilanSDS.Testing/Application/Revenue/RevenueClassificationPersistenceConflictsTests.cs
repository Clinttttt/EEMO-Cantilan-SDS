using EEMOCantilanSDS.Application.Common.Revenue;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class RevenueClassificationPersistenceConflictsTests
{
    private const string SemanticCodeIndex = "IX_RevenueClassifications_MunicipalityId_SemanticCode";
    private const string PolicyEffectiveDateIndex = "IX_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~";

    [Fact]
    public void SemanticCodeConflictRequiresTheSemanticCodeIndex()
    {
        var exception = Wrap("23505", SemanticCodeIndex);

        Assert.True(RevenueClassificationPersistenceConflicts.IsDuplicateSemanticCode(exception));
        Assert.False(RevenueClassificationPersistenceConflicts.IsDuplicatePolicyEffectiveDate(exception));
    }

    [Fact]
    public void PolicyDateConflictRequiresThePolicyEffectiveDateIndex()
    {
        var exception = Wrap("23505", PolicyEffectiveDateIndex);

        Assert.False(RevenueClassificationPersistenceConflicts.IsDuplicateSemanticCode(exception));
        Assert.True(RevenueClassificationPersistenceConflicts.IsDuplicatePolicyEffectiveDate(exception));
    }

    [Fact]
    public void UnknownUniqueConstraintIsNotARevenueBusinessConflict()
    {
        var exception = Wrap("23505", "PK_RevenueClassificationPolicies");

        Assert.False(RevenueClassificationPersistenceConflicts.IsDuplicateSemanticCode(exception));
        Assert.False(RevenueClassificationPersistenceConflicts.IsDuplicatePolicyEffectiveDate(exception));
    }

    [Fact]
    public void NonUniquePostgresErrorIsNotARevenueBusinessConflict()
    {
        var exception = Wrap("23503", SemanticCodeIndex);

        Assert.False(RevenueClassificationPersistenceConflicts.IsDuplicateSemanticCode(exception));
        Assert.False(RevenueClassificationPersistenceConflicts.IsDuplicatePolicyEffectiveDate(exception));
    }

    private static Exception Wrap(string sqlState, string? constraintName) =>
        new InvalidOperationException("Persistence failed.", new FakePostgresException(sqlState, constraintName));

    private sealed class FakePostgresException(string sqlState, string? constraintName) : Exception("PostgreSQL error")
    {
        public string SqlState { get; } = sqlState;
        public string? ConstraintName { get; } = constraintName;
    }
}
