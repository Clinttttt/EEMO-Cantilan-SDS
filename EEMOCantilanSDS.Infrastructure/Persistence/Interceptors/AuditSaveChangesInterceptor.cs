using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every create/update/delete of a financial transaction
/// entity, attributed to the authenticated actor. This gives an immutable audit trail independent of
/// the mutable CreatedBy/UpdatedBy strings on the entities.
/// </summary>
public class AuditSaveChangesInterceptor(ICurrentUserService currentUser) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> AuditedTypes =
    [
        typeof(PaymentRecord), typeof(DailyCollection),
        typeof(TpmAttendance), typeof(TrmTrip), typeof(SlaughterTransaction),
        typeof(OnlinePaymentTransaction)
    ];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null) return;

        var entries = context.ChangeTracker.Entries()
            .Where(e => AuditedTypes.Contains(e.Entity.GetType())
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0) return;

        var actorId = currentUser.UserId?.ToString() ?? "system";
        var actorName = currentUser.Username ?? "system";
        var actorRole = currentUser.Role ?? "system";

        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "Deleted",
                _ => "Updated"
            };

            var oldValues = entry.State is EntityState.Modified or EntityState.Deleted ? Serialize(entry.OriginalValues) : null;
            var newValues = entry.State is EntityState.Added or EntityState.Modified ? Serialize(entry.CurrentValues) : null;

            context.Add(AuditLog.Create(
                actorId, actorName, actorRole,
                action,
                entry.Entity.GetType().Name,
                (entry.Entity as BaseEntity)?.Id,
                oldValues, newValues));
        }
    }

    private static string Serialize(PropertyValues values) =>
        JsonSerializer.Serialize(values.Properties.ToDictionary(p => p.Name, p => values[p.Name]));
}
