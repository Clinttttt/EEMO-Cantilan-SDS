using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Whether an LGU's backup actually holds everything the LGU owns.
///
/// <para>
/// The round-trip test proves a restore reproduces the tables it is GIVEN, and leaves other municipalities
/// alone. It cannot see the more dangerous question: is it given all of them? A new tenant-owned table is
/// isolated by construction — the filter is attached by walking the model — so it arrives already private to
/// its LGU and already absent from the backup lists, which are written out by hand. Nothing fails. The
/// office's snapshot simply stops being complete, and it finds out on the day it restores.
/// </para>
///
/// <para>
/// So every tenant-owned table must be accounted for: restorable, export-only, or named in
/// <see cref="TenantDataTables.NotBackedUp"/> with the reason it is not. Adding a table and forgetting its
/// backup fails here rather than in an office.
/// </para>
/// </summary>
public class TenantBackupCoverageTests
{
    private sealed class NoTenant : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => Guid.Empty;
        public void Set(Guid municipalityId) { }
    }

    /// <summary>A resolved tenant — the export refuses to read anything without one.</summary>
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    /// <summary>The real model, built as production builds it.</summary>
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"backup-coverage-{Guid.NewGuid()}")
            .Options;

        using var context = new AppDbContext(options, new NoTenant());
        return context.Model;
    }

    /// <summary>
    /// Every table owned by a municipality. Derived TPH types share their root's table, so this is a set of
    /// table names rather than of entities — the backup works in tables.
    /// </summary>
    private static IReadOnlySet<string> TenantOwnedTables() =>
        Model().GetEntityTypes()
            .Where(t => t.ClrType is not null && typeof(IMunicipalityOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.GetTableName())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryTenantOwnedTableIsRestorableExportedOrExcludedWithAReason()
    {
        var accounted = new HashSet<string>(TenantDataTables.Restorable, StringComparer.Ordinal);
        accounted.UnionWith(TenantDataTables.ExportOnly);
        accounted.UnionWith(TenantDataTables.NotBackedUp.Keys);

        var unaccounted = TenantOwnedTables().Except(accounted, StringComparer.Ordinal).OrderBy(n => n).ToList();

        Assert.True(unaccounted.Count == 0,
            "These tables belong to a municipality but appear in none of the backup sets, so an LGU's backup "
            + "silently omits them. Add each to TenantDataTables.Restorable, .ExportOnly, or .NotBackedUp with "
            + "the reason: " + string.Join(", ", unaccounted));
    }

    [Fact]
    public void NothingIsNamedInTheBackupSetsThatTheModelDoesNotOwn()
    {
        // The opposite drift: a table renamed or removed leaves a name behind that reads as covered. A restore
        // silently skips a table it cannot find, so a stale name is an invisible hole.
        var owned = TenantOwnedTables();

        var stale = TenantDataTables.Restorable
            .Concat(TenantDataTables.ExportOnly)
            .Concat(TenantDataTables.NotBackedUp.Keys)
            .Where(name => !owned.Contains(name))
            .OrderBy(n => n)
            .ToList();

        Assert.True(stale.Count == 0,
            "These names are listed in TenantDataTables but no municipality-owned table has them — they are "
            + "renamed, removed, or misspelled: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryExclusionStatesWhyInWords()
    {
        var unexplained = TenantDataTables.NotBackedUp
            .Where(e => string.IsNullOrWhiteSpace(e.Value) || e.Value.Trim().Length < 20)
            .Select(e => e.Key)
            .OrderBy(n => n)
            .ToList();

        Assert.True(unexplained.Count == 0,
            "Leaving part of an office's record out of its backup is a decision that has to be written down: "
            + string.Join(", ", unexplained));
    }

    [Fact]
    public void TheAuditTrailIsExportedButNeverRestored()
    {
        // Restoring over the audit log would let a restore erase the record of itself.
        Assert.Contains("AuditLogs", TenantDataTables.ExportOnly);
        Assert.DoesNotContain("AuditLogs", TenantDataTables.Restorable);
    }

    [Fact]
    public void ARestoreCanNeverReplaceCredentialsOrTheBackupHistory()
    {
        // Users: a snapshot must not reinstate a removed account or an old password.
        // TenantBackups: a restore must not destroy the backups the office would need to undo it.
        Assert.DoesNotContain("Users", TenantDataTables.Restorable);
        Assert.DoesNotContain("TenantBackups", TenantDataTables.Restorable);
        Assert.DoesNotContain("Users", TenantDataTables.ExportOnly);
        Assert.DoesNotContain("TenantBackups", TenantDataTables.ExportOnly);
    }

    [Fact]
    public async Task TheExportTheOfficeDownloadsHoldsExactlyTheTablesTheseSetsName()
    {
        // The archive an office downloads is assembled by hand, table by table, in TenantExportRepository. If it
        // and these sets drift, the office's archive quietly misses a table while everything still reads covered.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"backup-export-keys-{Guid.NewGuid()}")
            .Options;

        await using var context = new AppDbContext(options, new FixedTenant(Guid.NewGuid()));
        var payload = await new TenantExportRepository(context).ExportAsync(default);

        var expected = TenantDataTables.Restorable
            .Concat(TenantDataTables.ExportOnly)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var actual = payload.Tables.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, actual);
    }
}
