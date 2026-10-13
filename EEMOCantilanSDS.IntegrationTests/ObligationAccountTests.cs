using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The specialized obligation accounts (IA-050): Fish/Meat Vendor Fee, Kanmanggay and event lot rental are monthly (or
/// event) obligations settled by installments on an Official Receipt, each under its own classification, with the
/// balance read only from canonical allocations.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ObligationAccountTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task Kanmanggay_LeaseMetadata_PersistsAcrossContexts_WithoutInventingLegacyTerms()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        var signedDate = start.AddDays(4);
        var request = new CreateObligationAccountRequest(
            ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "",
            null, null, start, 1200m, OccupancyArrangement.SignedContract, "KM-2026",
            "Actual occupant", "Signed lessee", signedDate, 3, 4.80m, "Extension");
        Guid firstId, secondId;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = Setup(context, seed);
            var first = await workflow.CreateAccountAsync(request);
            Assert.True(first.IsSuccess, first.Error);
            firstId = first.Value!.Id;
            var legacy = await workflow.CreateAccountAsync(request with
            {
                ActualOccupant = "Historical occupant",
                ContractName = null,
                ContractEffectiveDate = null,
                ContractYears = null,
                AreaSqm = null,
                AreaLocation = null,
                ContractReference = null
            });
            Assert.True(legacy.IsSuccess, legacy.Error);
            secondId = legacy.Value!.Id;
        }
        await using (var verify = db.CreateContext(seed.TenantId))
        {
            var workflow = Setup(verify, seed);
            var loaded = await workflow.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental);
            Assert.True(loaded.IsSuccess, loaded.Error);
            var signed = Assert.Single(loaded.Value!, a => a.Id == firstId);
            Assert.Equal("Signed lessee", signed.ContractName);
            Assert.Equal("KM-2026", signed.ContractReference);
            Assert.Equal(signedDate, signed.ContractEffectiveDate);
            Assert.Equal(3, signed.ContractYears);
            Assert.Equal(4.80m, signed.AreaSqm);
            Assert.Equal("Extension", signed.AreaLocation);
            Assert.Equal(start, signed.ActiveFrom);
            var legacy = Assert.Single(loaded.Value!, a => a.Id == secondId);
            Assert.Null(legacy.ContractName);
            Assert.Null(legacy.ContractEffectiveDate);
            Assert.Null(legacy.ContractYears);
            Assert.Null(legacy.AreaSqm);
            Assert.Null(legacy.AreaLocation);
            var quotes = await workflow.GetRegisterAsync(firstId);
            Assert.True(quotes.IsSuccess, quotes.Error);
            Assert.Equal(1200m, Assert.Single(quotes.Value!).AssessedAmount);
        }
    }

    [SkippableFact]
    public async Task MonthlySpaceHolderCorrection_IsAuditedAndNeverRepricesAnAccount()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        Guid accountId;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = Setup(context, seed);
            var created = await workflow.CreateAccountAsync(new CreateObligationAccountRequest(
                ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null, start, 1200m,
                OccupancyArrangement.SignedContract, "KM-ORIGINAL", "Pedro Vendor", "Named lessee",
                start.AddDays(4), 3, 5.50m, "Original corner"));
            Assert.True(created.IsSuccess, created.Error);
            accountId = created.Value!.Id;

            var corrected = await workflow.CorrectHolderAsync(accountId, new CorrectObligationHolderRequest(
                "Pedro Vendor", OccupancyArrangement.SignedContract, "KM-CORRECTED", "Named lessee",
                start.AddDays(4), 3, 6.75m, "Revised corner"));
            Assert.True(corrected.IsSuccess, corrected.Error);

            var replacementPerson = await workflow.CorrectHolderAsync(accountId, new CorrectObligationHolderRequest(
                "Different tenant", OccupancyArrangement.SignedContract, "KM-CORRECTED", "Named lessee",
                start.AddDays(4), 3, 6.75m, "Revised corner"));
            Assert.Equal(ResultStatus.Conflict, replacementPerson.Status);
        }

        await using var verify = db.CreateContext(seed.TenantId);
        var saved = await verify.ObligationAccounts.SingleAsync(x => x.Id == accountId);
        Assert.Equal("Pedro Vendor", saved.ActualOccupant);
        Assert.Equal("KM-CORRECTED", saved.ContractReference);
        Assert.Equal(6.75m, saved.AreaSqm);
        Assert.Equal("Revised corner", saved.AreaLocation);
        Assert.Equal(3, saved.ContractYears);
        var log = Assert.Single(await verify.AuditLogs.Where(x => x.EntityId == accountId
            && x.Action == "CorrectSpaceHolderEvidence").ToListAsync());
        Assert.Equal(seed.TenantId, log.MunicipalityId);
        Assert.Contains("KM-ORIGINAL", log.OldValues);
        Assert.Contains("KM-CORRECTED", log.NewValues);
        Assert.Equal(1200m, Assert.Single(await verify.ObligationRates
            .Where(x => x.ObligationAccountId == accountId).ToListAsync()).Amount);
        Assert.Empty(await verify.ObligationPeriods.Where(x => x.ObligationAccountId == accountId).ToListAsync());
        Assert.Empty(await verify.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task HolderEvidenceCorrection_AfterSettlement_IsHeadOnlyTenantScopedAndPreservesCash()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        await using var context = db.CreateContext(seed.TenantId);
        var head = Setup(context, seed);
        var created = await head.CreateAccountAsync(new CreateObligationAccountRequest(
            ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null, start, 1200m,
            OccupancyArrangement.SignedContract, "KM-ORIGINAL", "Pedro Vendor", "Named lessee",
            start.AddDays(4), 3, 5.50m, "Original corner"));
        Assert.True(created.IsSuccess, created.Error);
        var accountId = created.Value!.Id;
        var composer = Composer(context, seed);
        var draft = await composer.AddObligationAllocationAsync(new AddObligationDraftAllocationRequest(
            accountId, seed.Today.Year, seed.Today.Month, 400m, null));
        Assert.True(draft.IsSuccess, draft.Error);
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);
        var originalCash = await context.Collections.AsNoTracking().SingleAsync();
        var originalAllocation = await context.CollectionAllocations.AsNoTracking().SingleAsync();
        var originalPeriod = Assert.Single((await head.GetRegisterAsync(accountId)).Value!);
        var request = new CorrectObligationHolderRequest("Pedro Vendor", OccupancyArrangement.SignedContract,
            "KM-CORRECTED", "Named lessee", start.AddDays(4), 3, 6.75m, "Revised corner");
        Assert.Equal(ResultStatus.Forbidden,
            (await Setup(context, seed, "Admin").CorrectHolderAsync(accountId, request)).Status);
        var foreignSeed = seed with { TenantId = Guid.NewGuid() };
        await using (var foreign = db.CreateContext(foreignSeed.TenantId))
        {
            Assert.Equal(ResultStatus.NotFound,
                (await Setup(foreign, foreignSeed).CorrectHolderAsync(accountId, request)).Status);
        }
        var corrected = await head.CorrectHolderAsync(accountId, request);
        Assert.True(corrected.IsSuccess, corrected.Error);
        await using var verify = db.CreateContext(seed.TenantId);
        var saved = await verify.ObligationAccounts.SingleAsync(x => x.Id == accountId);
        Assert.Equal("KM-CORRECTED", saved.ContractReference);
        Assert.Equal("Named lessee", saved.ContractName);
        Assert.Equal(6.75m, saved.AreaSqm);
        var cash = await verify.Collections.SingleAsync();
        Assert.Equal(originalCash.Id, cash.Id);
        Assert.Equal(originalCash.ReferenceCode, cash.ReferenceCode);
        Assert.Equal(originalCash.TotalAmount, cash.TotalAmount);
        var allocation = await verify.CollectionAllocations.SingleAsync();
        Assert.Equal(originalAllocation.Id, allocation.Id);
        Assert.Equal(originalAllocation.SourceId, allocation.SourceId);
        Assert.Equal(originalAllocation.Amount, allocation.Amount);
        Assert.Equal(originalPeriod, Assert.Single((await Setup(verify, seed).GetRegisterAsync(accountId)).Value!));
        Assert.Equal(1200m, Assert.Single(await verify.ObligationRates.ToListAsync()).Amount);
        Assert.Single(await verify.AuditLogs.Where(x => x.EntityId == accountId
            && x.Action == "CorrectSpaceHolderEvidence").ToListAsync());
    }

    [SkippableFact]
    public async Task SpaceNumbers_AreAllocatedConcurrently_AndIndependentForEachEvent()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        var input = new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null,
            "", null, null, start, 175m, OccupancyArrangement.SpaceOnly);
        async Task<ObligationAccountDto> Create(CreateObligationAccountRequest request)
        {
            await using var context = db.CreateContext(seed.TenantId);
            var result = await Setup(context, seed).CreateAccountAsync(request);
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }
        var spaces = await Task.WhenAll(Create(input), Create(input));
        Assert.Equal(new[] { "1", "2" }, spaces.Select(x => x.SubjectLabel).Order());
        var fiesta = input with { Kind = ObligationKind.FiestaArawLotRental, Event = LotRentalEvent.Fiesta, EventDate = start };
        Assert.Equal("1", (await Create(fiesta)).SubjectLabel);
        Assert.Equal("1", (await Create(fiesta with { Event = LotRentalEvent.Araw })).SubjectLabel);
        var other = await SeedAsync();
        await using (var otherContext = db.CreateContext(other.TenantId))
        {
            var otherWorkflow = Setup(otherContext, other);
            Assert.Equal("1", (await otherWorkflow.CreateAccountAsync(input with { PayorId = other.PayorId })).Value!.SubjectLabel);
            Assert.Equal(ResultStatus.Invalid, (await otherWorkflow.CreateAccountAsync(input)).Status);
        }
        await using var ctx = db.CreateContext(seed.TenantId);
        Assert.Equal(ResultStatus.Conflict, (await Setup(ctx, seed).CreateAccountAsync(fiesta with { SubjectLabel = "1" })).Status);
        Assert.Equal(175m, Assert.Single((await Setup(ctx, seed).GetRegisterAsync(spaces[0].Id)).Value!).AssessedAmount);
    }

    [SkippableFact]
    public async Task ConcurrentImports_AllocateDistinctNumbersAndKeepAllRowsAtomic()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var input = new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null,
            "", null, null, new DateOnly(seed.Today.Year, seed.Today.Month, 1), 100m, OccupancyArrangement.SpaceOnly);
        async Task<ImportSpaceHoldersResult> Import()
        {
            await using var context = db.CreateContext(seed.TenantId);
            var result = await Setup(context, seed).ImportSpaceHoldersAsync(new([new(input), new(input)]));
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(2, result.Value!.Imported);
            return result.Value;
        }
        var results = await Task.WhenAll(Import(), Import());
        var rows = results.SelectMany(r => r.Rows!).ToArray();
        Assert.Equal(new[] { "1", "2", "3", "4" }, rows.Select(r => r.Facts!.Account.SubjectLabel).Order());
        Assert.Equal(4, rows.Select(r => r.AccountId).Distinct().Count());
        await using var ctx = db.CreateContext(seed.TenantId);
        Assert.Equal(4, await ctx.ObligationAccounts.CountAsync());
        Assert.Empty(await ctx.ObligationPeriods.ToListAsync());
    }

    [SkippableFact]
    public async Task ImportPreview_IsTypedAndReadOnly_SaveReallocatesAndReturnsIds_AndRejectsStalePayors()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var ctx = db.CreateContext(seed.TenantId);
        var workflow = Setup(ctx, seed);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        var operations = (await workflow.GetSpaceOperationsAsync()).Value!;
        Assert.Equal(4, operations.Count);
        Assert.True(Assert.Single(operations.Where(o => o.Kind == ObligationKind.KanmanggaySpaceRental)).IsMonthly);
        Assert.Equal(new[] { LotRentalEvent.Fiesta, LotRentalEvent.Araw }, operations.Where(o => !o.IsMonthly).Select(o => o.Event!.Value));
        var input = new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null,
            "", null, null, start, 175m, OccupancyArrangement.SpaceOnly);
        var request = new ImportSpaceHoldersRequest([new(input), new(input with { SubjectLabel = "5",
            Arrangement = OccupancyArrangement.SignedContract, ContractReference = "Lease" }, start)]);
        var preview = (await workflow.PreviewSpaceHoldersAsync(request)).Value!;
        Assert.True(preview.CanSave);
        Assert.Equal("6", preview.Rows[0].Facts!.Account.SubjectLabel);
        Assert.Equal(SpaceNumberOrigin.ServerSuggested, preview.Rows[0].Facts!.NumberOrigin);
        Assert.Equal(SpaceNumberOrigin.Supplied, preview.Rows[1].Facts!.NumberOrigin);
        Assert.NotNull(preview.Rows[0].Facts!.PayorDisplayName);
        Assert.All(preview.Rows, r => Assert.Null(r.AccountId));
        Assert.Empty(await ctx.ObligationAccounts.ToListAsync());
        Assert.True((await workflow.CreateAccountAsync(input with { SubjectLabel = "6" })).IsSuccess);
        var saved = (await workflow.ImportSpaceHoldersAsync(request)).Value!;
        Assert.Equal(2, saved.Imported);
        Assert.Equal("7", saved.Rows![0].Facts!.Account.SubjectLabel);
        Assert.All(saved.Rows, r => Assert.NotNull(r.AccountId));
        var duplicate = (await workflow.ImportSpaceHoldersAsync(new([new(input with { SubjectLabel = "007" })]))).Value!;
        Assert.Equal(1, duplicate.Skipped);
        Assert.Equal(0, duplicate.Imported);

        var stale = new ImportSpaceHoldersRequest([new(input)]);
        Assert.True((await workflow.PreviewSpaceHoldersAsync(stale)).Value!.CanSave);
        var payor = await ctx.Payors.SingleAsync(p => p.Id == seed.PayorId);
        payor.SoftDelete("test");
        await ctx.SaveChangesAsync();
        var refused = (await workflow.ImportSpaceHoldersAsync(stale)).Value!;
        Assert.Equal(0, refused.Imported);
        Assert.Equal("InvalidPayor", Assert.Single(refused.Rows!).Code);
        Assert.Equal(3, await ctx.ObligationAccounts.CountAsync());
        Assert.Empty(await ctx.ObligationPeriods.ToListAsync());
    }

    [SkippableFact]
    public async Task ASubmittedImportIsSavedOnce_AReplayOfTheSameSubmissionAnswersWithTheSavedResult_AndKeepsItsRun()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var ctx = db.CreateContext(seed.TenantId);
        var workflow = Setup(ctx, seed);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        var holder = new CreateObligationAccountRequest(ObligationKind.VegetableFruitSpaceRental, seed.PayorId, null, "", null, null, start, 60m,
            OccupancyArrangement.SpaceOnly);
        var operation = Guid.NewGuid();
        var request = new ImportSpaceHoldersRequest([new(holder), new(holder with { ActualOccupant = "Second stall" })], operation);

        var first = (await workflow.ImportSpaceHoldersAsync(request)).Value!;
        var replay = (await workflow.ImportSpaceHoldersAsync(request)).Value!;                                   // a double click or a retry after a lost response
        Assert.Equal((2, 0), (first.Imported, first.Skipped));
        Assert.Equal((2, 0, 0), (replay.Imported, replay.Skipped, replay.NeedsReview.Count));                    // the saved result, not a second import
        Assert.Equal(2, await ctx.ObligationAccounts.CountAsync());                                              // no duplicate spaces
        var run = Assert.Single(await ctx.SpaceHolderImportRuns.ToListAsync());                                  // the run (the import evidence) is kept once
        Assert.Equal(operation, run.ClientOperationId);

        // A new filling-in of the list is a new submission and is imported as before; a request without an id behaves as it always did.
        var again = (await workflow.ImportSpaceHoldersAsync(request with { ClientOperationId = Guid.NewGuid() })).Value!;
        Assert.Equal(2, again.Imported);
        Assert.Equal(4, await ctx.ObligationAccounts.CountAsync());
        Assert.Equal(2, await ctx.SpaceHolderImportRuns.CountAsync());
    }

    [SkippableFact]
    public async Task Kanmanggay_PersistsContractOrSpaceOnlyWithoutChangingMonthlyObligation_AndPreviewsIdentityErrors()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var ctx = db.CreateContext(seed.TenantId);
        var workflow = Setup(ctx, seed);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        var signed = new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null,
            "Signed space", null, null, start, 175m, OccupancyArrangement.SignedContract, "Lease 2026-01");
        var space = signed with { SubjectLabel = "Space only", Arrangement = OccupancyArrangement.SpaceOnly, ContractReference = null };
        var preview = await workflow.PreviewSpaceHoldersAsync(new([
            new(signed), new(space), new(space with { SubjectLabel = "Unlinked", PayorId = Guid.Empty }),
            new(space with { SubjectLabel = "Invalid", Amount = -1m })]));
        Assert.Equal(new[] { SpaceHolderImportStatus.Ready, SpaceHolderImportStatus.Ready, SpaceHolderImportStatus.NeedsSourceHolder, SpaceHolderImportStatus.Invalid },
            preview.Value!.Rows.Select(x => x.Status));
        Assert.False(preview.Value.CanSave);
        Assert.Equal(new[] { SpaceHolderImportAction.None, SpaceHolderImportAction.None,
            SpaceHolderImportAction.ConfirmSourceHolder, SpaceHolderImportAction.CorrectRow }, preview.Value.Rows.Select(x => x.RequiredAction));
        Assert.Empty(await ctx.ObligationAccounts.ToListAsync());
        var refused = await workflow.ImportSpaceHoldersAsync(new([new(signed), new(space with { PayorId = Guid.Empty })]));
        Assert.Equal(0, refused.Value!.Imported);
        Assert.Empty(await ctx.ObligationAccounts.ToListAsync());
        var imported = await workflow.ImportSpaceHoldersAsync(new([new(signed), new(space, start)]));
        Assert.Equal(2, imported.Value!.Imported);
        var listed = (await workflow.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!;
        Assert.Equal("Lease 2026-01", listed.Single(x => x.SubjectLabel == "Signed space").ContractReference);
        var noContract = listed.Single(x => x.SubjectLabel == "Space only");
        Assert.Equal(OccupancyArrangement.SpaceOnly, noContract.Arrangement);
        Assert.Null(noContract.ContractReference);
        Assert.Equal(start, noContract.ActiveTo);
        Assert.All(listed, x => Assert.Equal(175m, x.AssessedToDate));
        Assert.Equal(ResultStatus.Conflict, (await workflow.CreateAccountAsync(space with { SubjectLabel = " SPACE ONLY " })).Status);
        var duplicate = await workflow.PreviewSpaceHoldersAsync(new([new(space)]));
        Assert.Equal("DuplicateSpace", Assert.Single(duplicate.Value!.Rows).Code);
        Assert.Equal(SpaceHolderImportAction.ResolveDuplicate, Assert.Single(duplicate.Value.Rows).RequiredAction);
        Assert.Empty(await ctx.ObligationPeriods.ToListAsync());
    }

    [SkippableFact]
    public async Task KanmanggayAddNew_RejectsForeignPayorAndInvalidContractFacts_AndKeepsCloseHistory()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var ctx = db.CreateContext(seed.TenantId);
        var workflow = Setup(ctx, seed);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        var input = new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null,
            "Add new", null, null, start, 200m, OccupancyArrangement.SpaceOnly);
        Assert.False((await workflow.CreateAccountAsync(input with { PayorId = Guid.NewGuid() })).IsSuccess);
        Assert.False((await workflow.CreateAccountAsync(input with { Arrangement = OccupancyArrangement.Extension })).IsSuccess);
        Assert.False((await workflow.CreateAccountAsync(input with { ContractReference = "Fake" })).IsSuccess);
        Assert.True((await workflow.PreviewSpaceHoldersAsync(new([new(input with { Arrangement = OccupancyArrangement.SignedContract })]))).Value!.CanSave);
        var created = await workflow.CreateAccountAsync(input);
        Assert.True(created.IsSuccess, created.Error);
        Assert.True((await workflow.CloseAccountAsync(created.Value!.Id, new(start))).IsSuccess);
        Assert.Single(await ctx.ObligationAccounts.ToListAsync());
        Assert.Equal(200m, Assert.Single((await workflow.GetRegisterAsync(created.Value.Id)).Value!).AssessedAmount);
        Assert.Equal(seed.PayorId, (await ctx.ObligationAccounts.SingleAsync()).PayorId);
    }
    // The former Vendor Fee assessment model is historical from IA-064. Kanmanggay/Fiesta
    // retain this engine; their cross-source compatibility is tested at this pre-cutover date.
    private sealed class HistoricalClock : IClock
    {
        public DateOnly PhilippineToday => FishMeatVendorFeeRules.DirectEffectiveDate.AddDays(-1);
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineToday.ToDateTime(TimeOnly.MinValue);
    }
    private sealed record Seed(Guid TenantId, Guid UserId, Guid StallId, Guid PayorId, Guid UnlinkedStallId,
        Guid[] OrIds, DateOnly Today);

    private sealed class TestActor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "obligation-test";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "obligation-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static ObligationWorkflow Setup(AppDbContext context, Seed seed, string role = "SuperAdmin") =>
        new(context, new TestActor(seed.UserId, seed.TenantId, role), new FixedTenant(seed.TenantId), new HistoricalClock());

    [SkippableFact]
    public async Task SpaceImport_ValidatesAllRowsBeforeWriting_SkipsExisting_AndKeepsClosedHistory()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var ctx = db.CreateContext(seed.TenantId);
        var workflow = Setup(ctx, seed);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1).AddMonths(-1);
        var valid = new ImportSpaceHolderRow(new(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null,
            "K-1", null, null, start, 100m));
        var invalid = valid with { Account = valid.Account with { PayorId = Guid.NewGuid(), SubjectLabel = "K-2" } };
        var refused = await workflow.ImportSpaceHoldersAsync(new([valid, invalid]));
        Assert.True(refused.IsSuccess);
        Assert.Single(refused.Value!.NeedsReview);
        Assert.Equal(0, await ctx.ObligationAccounts.CountAsync());
        var imported = await workflow.ImportSpaceHoldersAsync(new([valid, valid with
            { Account = valid.Account with { SubjectLabel = "K-2" }, ClosedOn = start }]));
        Assert.Equal(2, imported.Value!.Imported);
        var replay = await workflow.ImportSpaceHoldersAsync(new([valid]));
        Assert.Equal((0, 1), (replay.Value!.Imported, replay.Value.Skipped));
        var report = (await workflow.GetStatusReportAsync(ObligationKind.KanmanggaySpaceRental, start.Year)).Value!;
        Assert.Contains(report.Accounts, a => a.SubjectLabel == "K-2" && a.ActiveTo == start);
        Assert.Equal(100m, report.Accounts.Single(a => a.SubjectLabel == "K-2").AssessedToDate);
        Assert.Equal(0, await ctx.Collections.CountAsync());
        var admin = await Setup(ctx, seed, "Admin").ImportSpaceHoldersAsync(new([valid]));
        Assert.Equal(ResultStatus.Forbidden, admin.Status);
    }

    private static CollectionComposerWorkflow Composer(AppDbContext context, Seed seed) =>
        new(context, new TestActor(seed.UserId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId), new HistoricalClock());

    private static async Task<long> PostAsync(CollectionComposerWorkflow composer, EcfCollectionDraftDto draft, Guid orId)
    {
        var selected = await composer.SelectDocumentAsync(draft.DraftId, new SelectEcfDraftDocumentRequest(draft.Revision, orId));
        Assert.True(selected.IsSuccess, selected.Error);
        var reviewed = await composer.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(selected.Value!.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);
        var posted = await composer.PostAsync(draft.DraftId,
            new PostEcfCollectionDraftRequest(reviewed.Value!.Revision, Guid.NewGuid()));
        Assert.True(posted.IsSuccess, posted.Error);
        return reviewed.Value.Revision;
    }

    private static Task<Result<ObligationAccountDto>> OpenVendorFee(ObligationWorkflow setup, Seed seed, decimal amount = 900m) =>
        setup.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.FishMeatVendorFee, Guid.Empty, seed.StallId,
            "Fish stall 01", null, null, new DateOnly(seed.Today.Year, seed.Today.Month, 1), amount));

    [SkippableFact]
    public async Task VendorFeeIsAMonthlyGoal_SettledByThirtyPesoInstallments_OnItsOwnClassification()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var setup = Setup(context, seed);

        var opened = await OpenVendorFee(setup, seed);
        Assert.True(opened.IsSuccess, opened.Error);
        // The Payor came from the NPM stall's linked occupancy, never from typed text.
        Assert.Equal(seed.PayorId, opened.Value!.PayorId);
        Assert.Equal(900m, opened.Value.CurrentAmount);

        var composer = Composer(context, seed);
        var first = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(opened.Value.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        Assert.True(first.IsSuccess, first.Error);
        await PostAsync(composer, first.Value!, seed.OrIds[0]);

        var second = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(opened.Value.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        Assert.True(second.IsSuccess, second.Error);
        await PostAsync(composer, second.Value!, seed.OrIds[1]);

        var register = await setup.GetRegisterAsync(opened.Value.Id);
        var month = Assert.Single(register.Value!);
        Assert.Equal((900m, 60m, 840m), (month.AssessedAmount, month.SettledAmount, month.OutstandingAmount));

        var lines = await context.CollectionLines.Include(x => x.Allocations).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.All(l.Allocations, a => Assert.Equal(CollectionSourceKind.ObligationPeriod, a.SourceKind)));
        var classification = await context.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.FishMeatVendorFee);
        Assert.All(lines, l => Assert.Equal(classification.Id, l.RevenueClassificationId));
        // Nothing in the legacy NPM daily-collection history was created or touched.
        Assert.Equal(0, await context.DailyCollections.CountAsync());
    }

    [SkippableFact]
    public async Task AnAllocationAboveTheRemainingBalance_IsRefused_AndSoIsACombinedOverAllocationInOneDraft()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var setup = Setup(context, seed);
        var account = (await OpenVendorFee(setup, seed, 100m)).Value!;
        var composer = Composer(context, seed);

        var over = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 100.01m, null));
        Assert.False(over.IsSuccess);

        var ok = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 70m, null));
        Assert.True(ok.IsSuccess, ok.Error);
        var combined = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 40m, ok.Value!.Revision));
        Assert.False(combined.IsSuccess);
        Assert.Contains("exceeds", combined.Error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task KanmanggayAndVendorFee_ForOnePayor_ShareOneOrAsSeparateClassifiedLines()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var setup = Setup(context, seed);
        var vendor = (await OpenVendorFee(setup, seed)).Value!;
        var kanmanggay = await setup.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental,
            seed.PayorId, null, "Space K-4", null, null, new DateOnly(seed.Today.Year, seed.Today.Month, 1), 1200m));
        Assert.True(kanmanggay.IsSuccess, kanmanggay.Error);

        var composer = Composer(context, seed);
        var draft = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(vendor.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        var both = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(kanmanggay.Value!.Id, seed.Today.Year, seed.Today.Month, 400m, draft.Value!.Revision));
        Assert.True(both.IsSuccess, both.Error);
        Assert.Equal(2, both.Value!.Lines.Count);
        Assert.Equal(430m, both.Value.TotalAmount);
        await PostAsync(composer, both.Value, seed.OrIds[0]);

        var collection = await context.Collections.Include(x => x.Lines).SingleAsync();
        Assert.Equal(2, collection.Lines.Count);
        Assert.Equal(2, collection.Lines.Select(l => l.RevenueClassificationId).Distinct().Count());
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", collection.ReferenceCode);   // one collection, one SRC, two classified lines
    }

    [SkippableFact]
    public async Task OnlyTheHeadOpensAccounts_AVendorFeeNeedsALinkedPayor_AndARateNeverReachesBackInTime()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);

        var admin = await OpenVendorFee(Setup(context, seed, "Admin"), seed);
        Assert.False(admin.IsSuccess);

        var head = Setup(context, seed);
        var unlinked = await head.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.FishMeatVendorFee, seed.PayorId,
            seed.UnlinkedStallId, "Fish stall 02", null, null, new DateOnly(seed.Today.Year, seed.Today.Month, 1), 900m));
        Assert.False(unlinked.IsSuccess);
        Assert.Contains("not linked", unlinked.Error, StringComparison.OrdinalIgnoreCase);

        var account = (await OpenVendorFee(head, seed)).Value!;
        var duplicate = await OpenVendorFee(head, seed);
        Assert.False(duplicate.IsSuccess);

        var composer = Composer(context, seed);
        var draft = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);

        var retroactive = await head.SetRateAsync(account.Id,
            new SetObligationRateRequest(new DateOnly(seed.Today.Year, seed.Today.Month, 1), 1000m));
        Assert.False(retroactive.IsSuccess);
        var next = new DateOnly(seed.Today.Year, seed.Today.Month, 1).AddMonths(1);
        Assert.True((await head.SetRateAsync(account.Id, new SetObligationRateRequest(next, 1000m))).IsSuccess);
        var month = Assert.Single((await head.GetRegisterAsync(account.Id)).Value!);
        Assert.Equal(900m, month.AssessedAmount);   // the assessed month is history and is never re-priced
    }

    [SkippableTheory]
    [InlineData(OccupancyArrangement.SignedContract)]
    [InlineData(OccupancyArrangement.SpaceOnly)]
    public async Task ALotRental_HasOneEventPeriod_AndTheEventDateIsNotAMonthlyBillingDate(OccupancyArrangement basis)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var head = Setup(context, seed);

        var eventDate = seed.Today.AddDays(-3);
        var lot = await head.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.FiestaArawLotRental,
            seed.PayorId, null, "Lot F-12", LotRentalEvent.Fiesta, eventDate, eventDate, 2500m,
            basis, basis == OccupancyArrangement.SignedContract ? "EVENT-LEASE-14" : null, "Ana Reyes"));
        Assert.True(lot.IsSuccess, lot.Error);
        await using (var reloaded = db.CreateContext(seed.TenantId))
        {
            var saved = await reloaded.ObligationAccounts.SingleAsync(x => x.Id == lot.Value!.Id);
            Assert.Equal(basis, saved.Arrangement);
            Assert.Equal(basis == OccupancyArrangement.SignedContract ? "EVENT-LEASE-14" : null, saved.ContractReference);
            Assert.Equal(seed.PayorId, saved.PayorId); // Preserve this existing collection test's explicit Payor context.
            Assert.Null(saved.ContractName); Assert.Null(saved.ContractEffectiveDate); Assert.Null(saved.ContractYears);
            Assert.Null(saved.AreaSqm); Assert.Null(saved.AreaLocation); Assert.Null(saved.ContractMonthlyRate);
            Assert.Equal(new[] { eventDate }, saved.PeriodStarts(eventDate.AddYears(1)));
        }

        var register = Assert.Single((await head.GetRegisterAsync(lot.Value!.Id)).Value!);
        Assert.Equal(eventDate, register.PeriodStart);
        Assert.Equal(2500m, register.OutstandingAmount);
        Assert.False((await head.SetRateAsync(lot.Value.Id, new SetObligationRateRequest(seed.Today.AddDays(1), 3000m))).IsSuccess);

        var composer = Composer(context, seed);
        var draft = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(lot.Value.Id, eventDate.Year, eventDate.Month, 2500m, null));
        Assert.True(draft.IsSuccess, draft.Error);
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);
        Assert.Equal(0m, Assert.Single((await head.GetRegisterAsync(lot.Value.Id)).Value!).OutstandingAmount);
    }

    [SkippableFact]
    public async Task IcePlantRent_CollectsUnderItsOwnClassification_NotPermanentStallRent()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var period = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        Guid iceStallId;
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            var facility = Facility.Create(FacilityCode.ICE, "Ice Plant", "ICE", municipalityId: seed.TenantId);
            var stall = Stall.Create(facility.Id, "ICE-01", 1000m, ApplicableFees.BaseRental, municipalityId: seed.TenantId);
            var contract = Contract.Create(stall.Id, "Pedro Vendor", "Pedro Vendor", period, 5, 1000m, createdBy: "test");
            contract.AssociatePayor(seed.PayorId, "test");
            var ice = RevenueClassification.Create(RevenueClassificationCodes.IcePlant, seed.TenantId);
            var iceOr = RevenueClassificationPolicy.Create(ice.Id, new DateOnly(2000, 1, 1), "Ice Plant",
                RevenueInstrumentType.OfficialReceipt, seed.TenantId);
            var rent = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, seed.TenantId);
            var rentOr = RevenueClassificationPolicy.Create(rent.Id, new DateOnly(2000, 1, 1), "Permanent Stall Rent",
                RevenueInstrumentType.OfficialReceipt, seed.TenantId);
            var record = PaymentRecord.Create(stall.Id, period.Year, period.Month, 1000m, "test");
            record.MarkSettlementPendingCutover();
            var at = DateTime.UtcNow.AddMinutes(-3);
            var cutover = CollectionSettlementCutover.Freeze(seed.TenantId, CollectionSourceKind.PaymentRecord, record.Id, null,
                record.SettlementVersion, at, 1000m, 0m, 1000m,
                "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}", seed.UserId, at.AddMinutes(1));
            record.ActivateCanonicalSettlement(cutover);
            setup.AddRange(facility, stall, contract, ice, iceOr, rent, rentOr, record);
            setup.CollectionSettlementCutovers.Add(cutover);
            await setup.SaveChangesAsync();
            iceStallId = stall.Id;
        }

        await using var context = db.CreateContext(seed.TenantId);
        var composer = Composer(context, seed);
        var draft = await composer.AddRentAllocationAsync(
            new AddRentDraftAllocationRequest(iceStallId, period.Year, period.Month, 250m));
        Assert.True(draft.IsSuccess, draft.Error);
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);

        var iceClassification = await context.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.IcePlant);
        var line = await context.CollectionLines.SingleAsync();
        Assert.Equal(iceClassification.Id, line.RevenueClassificationId);

        // Exactly once (IA-050): attribute the posted Collection AND the converted row's legacy projection to one collector.
        // The legacy projection is comparison evidence, so the collector's report shows the canonical receipt line only.
        var collector = CollectorUser.Create("Ana Reyes", "C-01", $"ana-{Guid.NewGuid():N}"[..14], null, null,
            new HashedPassword("h"), seed.TenantId);
        await using (var attach = db.CreateContext(seed.TenantId))
        {
            attach.Add(collector);
            await attach.SaveChangesAsync();
            await attach.Database.ExecuteSqlRawAsync("UPDATE \"Collections\" SET \"CollectorId\" = {0}", collector.Id);
            await attach.Database.ExecuteSqlRawAsync("UPDATE \"PaymentRecords\" SET \"CollectorId\" = {0}", collector.Id);
        }
        await using var read = db.CreateContext(seed.TenantId);
        var report = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectorReportQueries(read)
            .GetCollectionsAsync(collector.Id, seed.Today.AddDays(-1), seed.Today);
        var only = Assert.Single(report.Lines);
        Assert.Equal(("Ice Plant", 250m), (only.Nature, only.Amount));
    }

    // The default office is the one whose rulings the platform carries (Cantilan); the signed-contract duration standard is its alone.
    private async Task<Seed> SeedAsync(bool defaultOffice = true)
    {
        var municipality = Municipality.Create($"OBL-{Guid.NewGuid():N}"[..24], "Obligation Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"obl-{Guid.NewGuid():N}"[..32], isDefault: defaultOffice);
        var tenant = municipality.Id;
        var user = Guid.NewGuid();
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var today = new HistoricalClock().PhilippineToday;
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenant);
        var stall = Stall.Create(facility.Id, "FISH-01", 0m, ApplicableFees.None, MarketSection.FishSection,
            createdBy: "test", municipalityId: tenant);
        var unlinkedStall = Stall.Create(facility.Id, "FISH-02", 0m, ApplicableFees.None, MarketSection.FishSection,
            createdBy: "test", municipalityId: tenant);
        var payor = Payor.Create(tenant, "Pedro Vendor", BusinessPayorKind.Person, "test");
        var contract = Contract.Create(stall.Id, "Pedro Vendor", "Pedro Vendor",
            new DateOnly(today.Year, today.Month, 1).AddYears(-1), 20, 0m, createdBy: "test");
        contract.AssociatePayor(payor.Id, "test");
        var unlinkedContract = Contract.Create(unlinkedStall.Id, "Unlinked Vendor", "Unlinked Vendor",
            new DateOnly(today.Year, today.Month, 1).AddYears(-1), 20, 0m, createdBy: "test");

        var entities = new List<object> { facility, stall, unlinkedStall, payor, contract, unlinkedContract };
        foreach (var code in new[]
                 {
                     RevenueClassificationCodes.FishMeatVendorFee, RevenueClassificationCodes.KanmanggaySpaceRental,
                     RevenueClassificationCodes.FiestaArawLotRental
                 })
        {
            var classification = RevenueClassification.Create(code, tenant);
            entities.Add(classification);
            entities.Add(RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1), code,
                RevenueInstrumentType.OfficialReceipt, tenant));
        }
        var book = AccountableFormBook.Receive(tenant, RevenueInstrumentType.OfficialReceipt,
            "OBL TEST OR", "OR-", 1, 99, 4, DateTime.UtcNow.AddMinutes(-5), user.ToString("N"), "test");
        var documents = Enumerable.Range(1, 3).Select(i => AccountableDocument.Register(book, i, "test")).ToArray();
        entities.Add(book);
        entities.AddRange(documents);

        await using (var context = db.CreateContext(tenant))
        {
            context.AddRange(entities);
            await context.SaveChangesAsync();
        }
        return new Seed(tenant, user, stall.Id, payor.Id, unlinkedStall.Id, documents.Select(x => x.Id).ToArray(), today);
    }

    // ---- The three-year signed-contract duration is Cantilan's standard, not every office's ------------------------------------

    private static CreateObligationAccountRequest KanmanggayContract(Seed seed, DateOnly start, int? years) =>
        new(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null, start, 1200m,
            OccupancyArrangement.SignedContract, "KM-2026", "Actual occupant", "Signed lessee", start.AddDays(3), years, 4.80m, "Extension");

    [SkippableFact]
    public async Task AnOfficeWithNoConfirmedDurationPolicy_CannotRecordCantilansThreeYears_ButEverythingElseStillWorks()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(defaultOffice: false);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Setup(context, seed);

        // Neither Cantilan's number nor any other number is accepted as an approved duration.
        foreach (var years in new int?[] { 3, 5 })
        {
            var refused = await workflow.CreateAccountAsync(KanmanggayContract(seed, start, years));
            Assert.False(refused.IsSuccess);
            Assert.Equal(ResultStatus.Conflict, refused.Status);
            Assert.Contains("no duration policy has been confirmed for this municipality", refused.Error);
        }
        Assert.Equal(0, await context.ObligationAccounts.CountAsync(a => a.MunicipalityId == seed.TenantId && a.Kind == ObligationKind.KanmanggaySpaceRental));

        // The same contract WITHOUT a duration is a normal, legitimate record; so is a space-only occupant.
        var signed = await workflow.CreateAccountAsync(KanmanggayContract(seed, start, null));
        Assert.True(signed.IsSuccess, signed.Error);
        Assert.Null(signed.Value!.ContractYears);
        Assert.Equal("KM-2026", signed.Value.ContractReference);
        var spaceOnly = await workflow.CreateAccountAsync(new CreateObligationAccountRequest(
            ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null, start, 1200m,
            OccupancyArrangement.SpaceOnly, null, "Space only occupant"));
        Assert.True(spaceOnly.IsSuccess, spaceOnly.Error);

        // Other space operations have no duration rule and are untouched.
        var vegetable = await workflow.CreateAccountAsync(new CreateObligationAccountRequest(
            ObligationKind.VegetableFruitSpaceRental, seed.PayorId, null, "", null, null, start, 900m,
            OccupancyArrangement.SignedContract, "VF-1", "Vegetable seller", "Signed", start.AddDays(2), null, null, null));
        Assert.True(vegetable.IsSuccess, vegetable.Error);

        // Correcting the holder cannot introduce a duration either.
        var correction = await workflow.CorrectHolderAsync(signed.Value.Id, new CorrectObligationHolderRequest(
            "Actual occupant", OccupancyArrangement.SignedContract, "KM-2026", "Signed lessee", start.AddDays(3), 3, 4.80m, "Extension"));
        Assert.False(correction.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, correction.Status);
    }

    [SkippableFact]
    public async Task AnExistingRecordedDuration_IsPreservedAndStaysEditable_WhereItIsNotChanged()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(defaultOffice: false);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);

        // A record that already carries three years (made before this rule existed) - written directly, as history is.
        Guid accountId;
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            var account = ObligationAccount.Create(seed.TenantId, ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "KM-9",
                null, null, start, "test", actualOccupant: "Existing holder");
            account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "KM-OLD");
            account.SetLeaseDetails("Existing lessee", start.AddDays(2), 3, null, null);
            setup.ObligationAccounts.Add(account);
            setup.ObligationRates.Add(ObligationRate.Create(seed.TenantId, account.Id, start, 1200m, "test"));
            await setup.SaveChangesAsync();
            accountId = account.Id;
        }

        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Setup(context, seed);
        var sameYears = await workflow.CorrectHolderAsync(accountId, new CorrectObligationHolderRequest(
            "Existing holder", OccupancyArrangement.SignedContract, "KM-OLD", "Existing lessee", start.AddDays(2), 3, 9.5m, "Corner"));
        Assert.True(sameYears.IsSuccess, sameYears.Error);          // a descriptive correction; the duration is not touched

        var changed = await workflow.CorrectHolderAsync(accountId, new CorrectObligationHolderRequest(
            "Existing holder", OccupancyArrangement.SignedContract, "KM-OLD", "Existing lessee", start.AddDays(2), 5, 9.5m, "Corner"));
        Assert.False(changed.IsSuccess);                              // a different duration would be an unconfirmed policy value

        Assert.Equal(3, (await context.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId)).ContractYears);
    }

    [SkippableFact]
    public async Task TheDefaultOffice_KeepsItsThreeYearStandard_Exactly()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(defaultOffice: true);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Setup(context, seed);

        var three = await workflow.CreateAccountAsync(KanmanggayContract(seed, start, 3));
        Assert.True(three.IsSuccess, three.Error);
        Assert.Equal(3, three.Value!.ContractYears);
        var five = await workflow.CreateAccountAsync(KanmanggayContract(seed, start, 5));
        Assert.False(five.IsSuccess);
        Assert.Contains("three-year standard", five.Error);           // the domain rule, unchanged
    }

    [SkippableFact]
    public async Task ContractMonthlyRate_IsEvidenceOnly_NeverPricesTheAccount_AndIsValidated()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        await using var context = db.CreateContext(seed.TenantId);
        var head = Setup(context, seed);
        var signed = new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null, start, 900m,
            OccupancyArrangement.SignedContract, "KM-1", "Actual Occupant", "Contract Lessee", start.AddDays(2), 3, 9.5m, "Row A", ContractMonthlyRate: 1000m);

        // The signed contract states 1,000; the approved (assessed) amount is 900. Both are kept and neither prices the other.
        var created = await head.CreateAccountAsync(signed);
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal((900m, 1000m), (created.Value!.CurrentAmount, created.Value.ContractMonthlyRate));
        Assert.Equal(900m, Assert.Single((await head.GetRegisterAsync(created.Value.Id)).Value!).AssessedAmount);
        Assert.Equal(900m, Assert.Single(await context.ObligationRates.ToListAsync()).Amount);

        // A contract rate is only meaningful behind a signed contract, and must be a sane amount.
        Assert.False((await head.CreateAccountAsync(signed with
            { SubjectLabel = "", Arrangement = OccupancyArrangement.SpaceOnly, ContractReference = null, ContractName = null, ContractEffectiveDate = null, ContractYears = null })).IsSuccess);
        Assert.False((await head.CreateAccountAsync(signed with { ContractMonthlyRate = -1m })).IsSuccess);
        Assert.False((await head.CreateAccountAsync(signed with { ContractMonthlyRate = 1000.555m })).IsSuccess);

        // Correcting the stated rate is audited descriptive evidence: the approved amount and the assessed period do not move.
        var corrected = await head.CorrectHolderAsync(created.Value.Id, new("Actual Occupant", OccupancyArrangement.SignedContract, "KM-1", "Contract Lessee",
            start.AddDays(2), 3, 9.5m, "Row A", 1200m));
        Assert.True(corrected.IsSuccess, corrected.Error);
        var reloaded = Assert.Single((await head.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!);
        Assert.Equal((900m, 1200m), (reloaded.CurrentAmount, reloaded.ContractMonthlyRate));
        Assert.Equal(900m, Assert.Single((await head.GetRegisterAsync(created.Value.Id)).Value!).AssessedAmount);
    }

    [SkippableFact]
    public async Task PaymentHistory_IsOneAccountsCanonicalAllocations_NotItsOwnersOtherAccounts_AndIsTenantScoped()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        await using var context = db.CreateContext(seed.TenantId);
        var head = Setup(context, seed);
        // The SAME person (one Business Payor) holds two spaces.
        async Task<Guid> Open(string actual) => (await head.CreateAccountAsync(new CreateObligationAccountRequest(
            ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, "", null, null, start, 400m, OccupancyArrangement.SpaceOnly, null, actual))).Value!.Id;
        var first = await Open("Same Person"); var second = await Open("Same Person");
        var composer = Composer(context, seed);

        Assert.Empty((await head.GetPaymentsAsync(first)).Value!);                                           // an honest empty history
        var draft = await composer.AddObligationAllocationAsync(new AddObligationDraftAllocationRequest(first, seed.Today.Year, seed.Today.Month, 250m, null));
        Assert.True(draft.IsSuccess, draft.Error);
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);

        var payment = Assert.Single((await head.GetPaymentsAsync(first)).Value!);
        Assert.Equal((250m, 0m, 250m, "Posted", start), (payment.Amount, payment.CorrectionEffect, payment.NetAmount, payment.Disposition, payment.PeriodStart));
        Assert.StartsWith("SRC-", payment.ReferenceCode);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, payment.Instrument);
        Assert.Equal("obligation-test", payment.RecordedBy);
        Assert.Empty((await head.GetPaymentsAsync(second)).Value!);                                          // the same person's other space shows nothing

        // Authority and tenancy: a Head or Admin of this tenant may read; a Collector may not; another tenant cannot see the account at all.
        Assert.True((await Setup(context, seed, "Admin").GetPaymentsAsync(first)).IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, (await Setup(context, seed, "Collector").GetPaymentsAsync(first)).Status);
        var foreignSeed = seed with { TenantId = Guid.NewGuid() };
        await using var foreign = db.CreateContext(foreignSeed.TenantId);
        Assert.Equal(ResultStatus.NotFound, (await Setup(foreign, foreignSeed).GetPaymentsAsync(first)).Status);
    }

    [SkippableFact]
    public async Task ImportRuns_AreRecordedOnlyForSavedImports_AndSeparatePreviewsFailuresAndReplays()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        await using var context = db.CreateContext(seed.TenantId);
        var head = Setup(context, seed);
        ImportSpaceHolderRow Row(string subject, decimal rate = 100m) => new(new(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null, subject, null, null, start, rate,
            OccupancyArrangement.SignedContract, null, "Occupant " + subject, "Contract " + subject, start.AddDays(1), 3, 5m, null, ContractMonthlyRate: 120m));

        // A preview and a refused list leave nothing behind.
        Assert.True((await head.PreviewSpaceHoldersAsync(new([Row("K-1")]))).IsSuccess);
        Assert.Empty((await head.GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental)).Value!);
        var refused = await head.ImportSpaceHoldersAsync(new([Row("K-1"), Row("K-2") with { Account = Row("K-2").Account with { PayorId = Guid.NewGuid() } }]));
        Assert.Single(refused.Value!.NeedsReview);
        Assert.Empty((await head.GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental)).Value!);

        // A saved import records one run with the real counts; the contract rate arrived with the account.
        var saved = await head.ImportSpaceHoldersAsync(new([Row("K-1"), Row("K-2")]));
        Assert.Equal(2, saved.Value!.Imported);
        var run = Assert.Single((await head.GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental)).Value!);
        Assert.Equal((2, 2, 0, "obligation-test"), (run.RowCount, run.ImportedCount, run.SkippedCount, run.ImportedBy));
        Assert.All((await head.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!, a => Assert.Equal(120m, a.ContractMonthlyRate));

        // Replaying the same list adds nothing (existing spaces are skipped, never renumbered or overwritten), so it records no new run.
        var replay = await head.ImportSpaceHoldersAsync(new([Row("K-1")]));
        Assert.Equal((0, 1), (replay.Value!.Imported, replay.Value.Skipped));
        Assert.Single((await head.GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental)).Value!);
        Assert.Equal(2, await context.ObligationAccounts.CountAsync());

        // Head only; another tenant sees none; the run is for the line asked about.
        Assert.Equal(ResultStatus.Forbidden, (await Setup(context, seed, "Admin").GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental)).Status);
        Assert.Empty((await head.GetImportRunsAsync(ObligationKind.FiestaArawLotRental)).Value!);
        var foreignSeed = seed with { TenantId = Guid.NewGuid() };
        await using var foreign = db.CreateContext(foreignSeed.TenantId);
        Assert.Empty((await Setup(foreign, foreignSeed).GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental)).Value!);
    }
}
