namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Matches only the PostgreSQL unique indexes that represent expected Revenue Classification business conflicts,
/// without making Application depend on Npgsql.
/// </summary>
public static class RevenueClassificationPersistenceConflicts
{
    private const string PostgresUniqueViolationSqlState = "23505";

    // These names are the persisted unique-index names in AddRevenueClassificationFoundation.
    private const string SemanticCodeIndex = "IX_RevenueClassifications_MunicipalityId_SemanticCode";
    private const string PolicyEffectiveDateIndex = "IX_RevenueClassificationPolicies_MunicipalityId_RevenueClassif~";

    public static bool IsDuplicateSemanticCode(Exception exception) =>
        IsUniqueViolationOnIndex(exception, SemanticCodeIndex);

    public static bool IsDuplicatePolicyEffectiveDate(Exception exception) =>
        IsUniqueViolationOnIndex(exception, PolicyEffectiveDateIndex);

    private static bool IsUniqueViolationOnIndex(Exception exception, string expectedIndexName)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var exceptionType = current.GetType();
            var sqlState = exceptionType.GetProperty("SqlState")?.GetValue(current) as string;
            var constraintName = exceptionType.GetProperty("ConstraintName")?.GetValue(current) as string;

            if (string.Equals(sqlState, PostgresUniqueViolationSqlState, StringComparison.Ordinal)
                && string.Equals(constraintName, expectedIndexName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
