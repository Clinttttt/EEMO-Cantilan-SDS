using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.Users;
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
        // Financial transactions
        typeof(PaymentRecord), typeof(DailyCollection),
        typeof(TpmAttendance), typeof(TrmTrip), typeof(SlaughterTransaction),
        typeof(OnlinePaymentTransaction),
        // Account / payor / stall management
        typeof(AdminUser), typeof(CollectorUser), typeof(PayorUser),
        typeof(Stall), typeof(PayorActivationCode), typeof(PayorStallLink)
    ];

    // User-account types whose login/token-refresh updates are routine and must NOT flood the trail.
    private static readonly HashSet<Type> UserTypes =
    [
        typeof(AdminUser), typeof(CollectorUser), typeof(PayorUser)
    ];

    // Auth-housekeeping columns. A user Modified entry whose changed columns are ALL within this set
    // (login, token refresh, failed-attempt/lockout bookkeeping) is skipped — not an auditable action.
    private static readonly HashSet<string> AuthHousekeepingFields = new(StringComparer.Ordinal)
    {
        "LastLoginAt", "RefreshToken", "RefreshTokenExpiryTime", "FailedAttempts", "LockedUntil"
    };

    // Secrets/credentials that must NEVER be written into the audit snapshot (old or new values).
    private static readonly HashSet<string> RedactedFields = new(StringComparer.Ordinal)
    {
        "PasswordHash", "RefreshToken", "RefreshTokenExpiryTime", "Code"
    };

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
            var entityType = entry.Entity.GetType();

            // Skip routine auth housekeeping (login / token refresh / lockout) on user accounts:
            // if every changed column is housekeeping, there's nothing meaningful to audit.
            if (entry.State == EntityState.Modified && UserTypes.Contains(entityType))
            {
                var changed = entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToList();
                if (changed.Count > 0 && changed.All(AuthHousekeepingFields.Contains))
                    continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "Deleted",
                _ => "Updated"
            };

            // A soft-delete is a Modified that flips IsDeleted -> true; record it as a Deleted action.
            if (entry.State == EntityState.Modified)
            {
                var isDeleted = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "IsDeleted");
                if (isDeleted is { IsModified: true } && isDeleted.CurrentValue is true)
                    action = "Deleted";
            }

            var oldValues = entry.State is EntityState.Modified or EntityState.Deleted ? Serialize(entry.OriginalValues) : null;
            var newValues = entry.State is EntityState.Added or EntityState.Modified ? Serialize(entry.CurrentValues) : null;

            context.Add(AuditLog.Create(
                actorId, actorName, actorRole,
                action,
                entityType.Name,
                (entry.Entity as BaseEntity)?.Id,
                oldValues, newValues));
        }
    }

    // Serializes a property snapshot to JSON, replacing any credential/secret field with a marker
    // so password hashes, refresh tokens, and activation codes never land in the audit log.
    private static string Serialize(PropertyValues values) =>
        JsonSerializer.Serialize(values.Properties.ToDictionary(
            p => p.Name,
            p => RedactedFields.Contains(p.Name) ? (object?)"[redacted]" : values[p.Name]));
}
