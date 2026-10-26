using System.Linq.Expressions;
using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;

/// <summary>One status expression for SQL filtering/counting and returned rows. Expiry does not end an account.</summary>
public static class ContractMonitoringRules
{
    // SQL-translatable form of DomainRules.TermLastDay, restricted to complete signed evidence.
    // Page rows use the domain helper itself; PostgreSQL parity tests include leap-day anniversaries.
    public static readonly Expression<Func<ContractMonitoringRecord, DateOnly?>> Expiry = r =>
        r.OccupancyBasis == OccupancyArrangement.SignedContract && r.EffectivityDate != null &&
        r.EffectivityDate > DateOnly.MinValue && (r.DurationYears > 0 || r.DurationMonths > 0)
        && r.DurationYears <= 9999 - r.EffectivityDate.Value.Year - (r.DurationMonths > 0 ? 1 : 0)
            // Yearly terms keep their original AddYears form; a month-aware term is start + (years*12 + months) months, minus one day.
            ? (r.DurationMonths > 0
                ? r.EffectivityDate.Value.AddMonths(r.DurationYears.Value * 12 + r.DurationMonths.Value).AddDays(-1)
                : r.EffectivityDate.Value.AddYears(r.DurationYears.Value).AddDays(-1))
            : null;

    public static Expression<Func<ContractMonitoringRecord, ContractMonitoringStatus>> Status(DateOnly today) => r =>
        r.Ended ? ContractMonitoringStatus.Ended :
        r.OccupancyStart > today || r.EffectivityDate > today ? ContractMonitoringStatus.NotYetEffective :
        r.OccupancyBasis == OccupancyArrangement.SpaceOnly || r.OccupancyBasis == OccupancyArrangement.Extension ? ContractMonitoringStatus.NoContract :
        r.OccupancyBasis != OccupancyArrangement.SignedContract || r.ExpiryDate == null ? ContractMonitoringStatus.Unknown :
        r.ExpiryDate < today ? ContractMonitoringStatus.Expired :
        r.ExpiryDate <= today.AddDays(30) ? ContractMonitoringStatus.ExpiringSoon : ContractMonitoringStatus.Active;

    public static Expression<Func<ContractMonitoringRecord, bool>> HasStatus(DateOnly today, ContractMonitoringStatus status)
    {
        var expression = Status(today);
        Expression match = Expression.Equal(expression.Body, Expression.Constant(status));
        // Active is the live-contract filter/card, including the ExpiringSoon subset.
        if (status == ContractMonitoringStatus.Active)
            match = Expression.OrElse(match, Expression.Equal(expression.Body, Expression.Constant(ContractMonitoringStatus.ExpiringSoon)));
        return Expression.Lambda<Func<ContractMonitoringRecord, bool>>(match, expression.Parameters);
    }

    public static Expression<Func<ContractMonitoringRecord, bool>> InWindow(DateOnly today, int days) => r =>
        !r.Ended && r.OccupancyStart <= today && r.EffectivityDate <= today &&
        r.OccupancyBasis == OccupancyArrangement.SignedContract && r.ExpiryDate >= today && r.ExpiryDate <= today.AddDays(days);

    /// <summary>Copies the source projection and inserts the shared expiry expression before any SQL filter/page.</summary>
    public static Expression<Func<ContractMonitoringRecord, ContractMonitoringRecord>> WithExpiry()
    {
        var parameter = Expiry.Parameters[0];
        var bindings = typeof(ContractMonitoringRecord).GetProperties().Select(property => Expression.Bind(property,
            property.Name == nameof(ContractMonitoringRecord.ExpiryDate) ? Expiry.Body : Expression.Property(parameter, property)));
        return Expression.Lambda<Func<ContractMonitoringRecord, ContractMonitoringRecord>>(
            Expression.MemberInit(Expression.New(typeof(ContractMonitoringRecord)), bindings), parameter);
    }

    public static ContractMonitoringRowDto ToDto(ContractMonitoringRecord r, DateOnly today, Func<ContractMonitoringRecord, ContractMonitoringStatus> status)
    {
        var expiry = r.ExpiryDate is not null ? DomainRules.TermLastDay(r.EffectivityDate!.Value, r.DurationYears!.Value, r.DurationMonths) : (DateOnly?)null;
        if (expiry != r.ExpiryDate) throw new InvalidOperationException("Monitoring SQL expiry disagrees with the canonical term rule.");
        return new(r.SourceType, r.SourceRecordId, r.FacilityId, r.FacilityCode, r.Operation, r.OperationDisplayName,
            r.StallId, r.ObligationKind, r.PayorId, r.ActualOccupant, r.ContractName, r.SpaceNumber, r.OccupancyBasis,
            r.ContractReference, r.OccupancyStart, r.EffectivityDate, r.DurationYears, expiry,
            expiry is { } date ? date.DayNumber - today.DayNumber : null, status(r), r.ClosureDate,
            r.ApprovedCharge, r.ChargeFrequency, r.ContractStatedAmount, r.AreaSqm, r.AreaLocation, r.DurationMonths);
    }
}
