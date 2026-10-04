using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Accountable Form No. 51 accountability on PostgreSQL: the exact printed serial and its normalized key, range registration
/// without a hard-coded booklet size, collector custody, sequence-aware next receipt, cancellation and loss (whole set or by
/// copy) that never return to stock, external follow-up references, and the position and history that read the form ledger.
/// Custody is a separate ledger from money (IA-052): nothing here posts a Collection.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Af51AccountabilityTests(PostgresFixture db)
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Head(Guid userId, Guid tenantId, string role = "Admin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "collector" : "office";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "pg-af51";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record World(Guid TenantId, Guid HeadId, CollectorUser Ana, CollectorUser Ben);

    private async Task<World> SeedAsync(string label = "A")
    {
        var tenant = Municipality.Create($"af5-{Guid.NewGuid():N}"[..12], $"AF51 {label}", "Province", MunicipalityStatus.Active,
            tenantCode: $"af51-{Guid.NewGuid():N}"[..28]);
        CollectorUser Collector(string name, string id) =>
            CollectorUser.Create(name, id, $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        var (ana, ben) = (Collector("Ana Reyes", "C-01"), Collector("Ben Cruz", "C-02"));
        await using var setup = db.CreateContext(Guid.Empty);
        setup.AddRange(tenant, ana, ben);
        await setup.SaveChangesAsync();
        return new World(tenant.Id, Guid.NewGuid(), ana, ben);
    }

    private static AccountableFormCustodyWorkflow Custody(AppDbContext ctx, World w, string role = "Admin") =>
        new(ctx, new Head(w.HeadId, w.TenantId, role), new FixedTenant(w.TenantId));

    private static RegisterAccountableFormsRequest Register(string first, string last, int? quantity = null, string? variant = null) =>
        new(RevenueInstrumentType.OfficialReceipt, first, last, quantity, variant, null, "Municipal Treasurer", null);

    private async Task<AccountableFormBookDto> RegisterOkAsync(World w, string first, string last, int? quantity = null, string? variant = null)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var result = await Custody(ctx, w).RegisterAsync(Register(first, last, quantity, variant));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [SkippableFact]
    public async Task ARangeIsRegisteredFromItsPrintedSerials_KeepingTheExactValue_WithNoBookletSizeAssumed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        var seven = await RegisterOkAsync(w, "2315601 A", "2315607 A");
        var big = await RegisterOkAsync(w, "5000001 C", "5000120 C", quantity: 120);

        Assert.Equal(7, seven.Quantity);
        Assert.Equal(120, big.Quantity);
        Assert.Equal("2315601 A", seven.Documents.First().DocumentNumber);
        Assert.Equal("2315607 A", seven.Documents.Last().DocumentNumber);
        Assert.Equal(" A", seven.NumberSuffix);
        Assert.Equal(string.Empty, seven.FormVariant);
        Assert.Equal("AF No. 51", seven.SeriesName);
        Assert.Equal("Municipal Treasurer", seven.SourceAuthority);
        await using var read = db.CreateContext(w.TenantId);
        var stored = await read.AccountableDocuments.AsNoTracking().OrderBy(x => x.SerialNumber).Where(x => x.FormBookId == seven.BookId).ToListAsync();
        Assert.Equal(["2315601A", "2315602A", "2315603A", "2315604A", "2315605A", "2315606A", "2315607A"], stored.Select(x => x.NormalizedNumber));
        Assert.All(stored, x => Assert.Equal(AccountableDocumentState.InOffice, x.State));
        Assert.Equal(0, await read.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task TheSuffixIsNotTheFormVariant_AndAVariantIsRecordedSeparately()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        var withSuffix = await RegisterOkAsync(w, "2315601 A", "2315603 A");
        var withVariant = await RegisterOkAsync(w, "7000001", "7000003", variant: "51-A");

        Assert.Equal(("", " A"), (withSuffix.FormVariant, withSuffix.NumberSuffix));
        Assert.Equal(("51-A", ""), (withVariant.FormVariant, withVariant.NumberSuffix));
        Assert.Equal("AF No. 51-A", withVariant.SeriesName);
    }

    [SkippableTheory]
    [InlineData("2315601A", "2315603A")]      // spacing differs
    [InlineData("2315601 a", "2315603 a")]    // case differs
    [InlineData("2315603 A", "2315610 A")]    // overlaps the end of the registered range
    [InlineData("2315599 A", "2315602 A")]    // overlaps the start
    public async Task TheSamePhysicalSerial_CannotBeRegisteredTwice_WhateverItsSpacingOrCase_AndNothingIsRegistered(string first, string last)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await RegisterOkAsync(w, "2315601 A", "2315605 A");

        await using var ctx = db.CreateContext(w.TenantId);
        var result = await Custody(ctx, w).RegisterAsync(Register(first, last));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("already registered", result.Error);
        await using var read = db.CreateContext(w.TenantId);
        Assert.Equal(5, await read.AccountableDocuments.CountAsync());
        Assert.Equal(1, await read.AccountableFormBooks.CountAsync());
    }

    [SkippableFact]
    public async Task AWrongQuantity_AndARangeThatCannotBeCounted_AreRefused_AndAnotherTenantMayRegisterTheSameSerial()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var other = await SeedAsync("B");
        await using var ctx = db.CreateContext(w.TenantId);
        var custody = Custody(ctx, w);

        Assert.Equal(ResultStatus.Invalid, (await custody.RegisterAsync(Register("2315601 A", "2315650 A", quantity: 40))).Status);
        Assert.Equal(ResultStatus.Invalid, (await custody.RegisterAsync(Register("2315601 A", "2315650 B"))).Status);
        Assert.Equal(ResultStatus.Invalid, (await custody.RegisterAsync(Register("2315650 A", "2315601 A"))).Status);
        Assert.Equal(0, await ctx.AccountableDocuments.CountAsync());

        // Serial identity is per tenant: the same printed range is a different physical form in another tenant.
        await RegisterOkAsync(w, "2315601 A", "2315603 A");
        await RegisterOkAsync(other, "2315601 A", "2315603 A");
    }

    [SkippableFact]
    public async Task ATenant_CannotReadOrChangeAnotherTenantsForms()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var other = await SeedAsync("B");
        var book = await RegisterOkAsync(w, "2315601 A", "2315605 A");

        await using var ctx = db.CreateContext(other.TenantId);
        var custody = Custody(ctx, other);
        Assert.Empty((await custody.ListAsync()).Value!);
        var loss = await custody.ReportLossAsync(new(book.BookId, 2315601, 2315605, AccountableFormCopies.WholeSet, PhilippineTime.Today, "x", "x"));
        Assert.False(loss.IsSuccess);
        Assert.Empty((await custody.GetExceptionsAsync()).Value!);
        Assert.Equal(0, (await custody.GetPositionAsync(RevenueInstrumentType.OfficialReceipt)).Value!.Totals.Registered);
        Assert.Equal(ResultStatus.Forbidden, (await Custody(ctx, other, "Collector").RegisterAsync(Register("1", "2"))).Status);

        await using var own = db.CreateContext(w.TenantId);
        Assert.All(await own.AccountableDocuments.ToListAsync(), x => Assert.Equal(AccountableDocumentState.InOffice, x.State));
    }

    [SkippableFact]
    public async Task ACollectorHoldsEachSerialOnce_TwoConcurrentAssignmentsOfTheSameRangeHaveOneWinner_AndUnusedFormsReturnOrTransfer()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var book = await RegisterOkAsync(w, "2315601 A", "2315625 A");

        await using var first = db.CreateContext(w.TenantId);
        await using var second = db.CreateContext(w.TenantId);
        var results = await Task.WhenAll(
            Custody(first, w).AssignRangeAsync(new(book.BookId, w.Ana.Id, 2315601, 2315610), RevenueInstrumentType.OfficialReceipt),
            Custody(second, w).AssignRangeAsync(new(book.BookId, w.Ben.Id, 2315601, 2315610), RevenueInstrumentType.OfficialReceipt));
        Assert.Equal(1, results.Count(x => x.IsSuccess));

        await using var check = db.CreateContext(w.TenantId);
        var open = await check.AccountableFormAssignments.Where(x => x.ReturnedAtUtc == null).ToListAsync();
        Assert.Equal(10, open.Count);
        Assert.Single(open.Select(x => x.AssignedUserId).Distinct());
        var holder = open[0].AssignedUserId;
        var other = holder == w.Ana.Id ? w.Ben : w.Ana;

        // Unused forms transfer between collectors, then return to the office; none of it is money.
        var transfer = await Custody(check, w).TransferAsync(new(book.BookId, 2315601, 2315605, other.Id, "Route change"));
        Assert.True(transfer.IsSuccess, transfer.Error);
        var back = await Custody(check, w).ReturnUnusedAsync(new(book.BookId, 2315601, 2315605));
        Assert.True(back.IsSuccess, back.Error);
        await using var after = db.CreateContext(w.TenantId);
        Assert.Equal(5, await after.AccountableDocuments.CountAsync(x => x.State == AccountableDocumentState.InOffice && x.SerialNumber <= 2315605));
        Assert.Equal(0, await after.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task TheNextExpectedReceiptFollowsTheRegisteredSequence_AndACancelledOrLostSerialIsSkippedNeverReused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var book = await RegisterOkAsync(w, "2315601 A", "2315606 A");

        async Task<IReadOnlyList<EcfAvailableDocumentDto>> Available()
        {
            await using var ctx = db.CreateContext(w.TenantId);
            var composer = new CollectionComposerWorkflow(ctx, new Head(w.HeadId, w.TenantId), new FixedTenant(w.TenantId));
            var result = await composer.GetAvailableReceiptsAsync();
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }

        var all = await Available();
        Assert.Equal("2315601 A", all[0].DocumentNumber);
        Assert.True(all[0].IsNextExpected);
        Assert.Single(all, x => x.IsNextExpected);

        // 2315601 A is spoiled on the desk; 2315602 A is reported lost. The sequence moves past both and neither returns.
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var id = (await ctx.AccountableDocuments.SingleAsync(x => x.DocumentNumber == "2315601 A")).Id;
            Assert.True((await Custody(ctx, w).SpoilAsync(new(id, "Wrong payor written", null))).IsSuccess);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Custody(ctx, w).ReportLossAsync(new(book.BookId, 2315602, 2315602, AccountableFormCopies.WholeSet,
                PhilippineTime.Today, "Market", "Booklet slipped from the bag"))).IsSuccess);

        var next = await Available();
        Assert.Equal("2315603 A", next[0].DocumentNumber);
        Assert.DoesNotContain(next, x => x.DocumentNumber is "2315601 A" or "2315602 A");

        // They can never be assigned, returned, re-spoiled or registered again.
        await using var verify = db.CreateContext(w.TenantId);
        var custody = Custody(verify, w);
        Assert.False((await custody.AssignRangeAsync(new(book.BookId, w.Ana.Id, 2315601, 2315602), RevenueInstrumentType.OfficialReceipt)).IsSuccess);
        Assert.False((await custody.ReturnUnusedAsync(new(book.BookId, 2315601, 2315602))).IsSuccess);
        Assert.Equal(ResultStatus.Conflict, (await custody.RegisterAsync(Register("2315601 A", "2315602 A"))).Status);
        var lostId = (await verify.AccountableDocuments.SingleAsync(x => x.DocumentNumber == "2315602 A")).Id;
        Assert.Equal(ResultStatus.Conflict, (await custody.SpoilAsync(new(lostId, "again", null))).Status);
    }

    [SkippableFact]
    public async Task AnIssuedFormNeverReturnsToStock_AndItsLostCopyIsRecordedAsAnExceptionWithoutChangingItsFinancialState()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var book = await RegisterOkAsync(w, "2315601 A", "2315605 A");
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Custody(ctx, w).AssignRangeAsync(new(book.BookId, w.Ana.Id, 2315601, 2315605), RevenueInstrumentType.OfficialReceipt)).IsSuccess);
        Guid issuedId;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var unit = await ctx.AccountableDocuments.SingleAsync(x => x.DocumentNumber == "2315601 A");
            unit.Consume(null, Guid.NewGuid(), DateTime.UtcNow, "office");
            issuedId = unit.Id;
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext(w.TenantId);
        var custody = Custody(verify, w);
        Assert.False((await custody.ReturnUnusedAsync(new(book.BookId, 2315601, 2315601))).IsSuccess);
        Assert.False((await custody.TransferAsync(new(book.BookId, 2315601, 2315601, w.Ben.Id, "x"))).IsSuccess);

        // Only the Duplicate of the issued receipt is missing: an exception on the issued unit, not a state change.
        var loss = await custody.ReportLossAsync(new(book.BookId, 2315601, 2315601, AccountableFormCopies.Duplicate, PhilippineTime.Today,
            "Office", "Duplicate not in the booklet at turnover"));
        Assert.True(loss.IsSuccess, loss.Error);
        Assert.Equal((1, 0), (loss.Value!.Reported, loss.Value.BlockedFromIssue));
        await using var after = db.CreateContext(w.TenantId);
        Assert.Equal(AccountableDocumentState.Consumed, (await after.AccountableDocuments.SingleAsync(x => x.Id == issuedId)).State);
        var report = await after.AccountableFormLossReports.SingleAsync();
        Assert.Equal(AccountableFormCopies.Duplicate, report.CopiesLost);
        Assert.False(report.BlockedFromIssue);
        var exceptions = (await Custody(after, w).GetExceptionsAsync()).Value!;
        Assert.Equal("Duplicate missing", Assert.Single(exceptions).Kind);
    }

    [SkippableFact]
    public async Task ALostWholeSetOrACopyOfABlankForm_BlocksItAtOnce_ClosesCustody_AndNeedsFollowUpUntilTheNoticeIsAdded()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var book = await RegisterOkAsync(w, "2315601 A", "2315610 A");
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Custody(ctx, w).AssignRangeAsync(new(book.BookId, w.Ana.Id, 2315601, 2315610), RevenueInstrumentType.OfficialReceipt)).IsSuccess);

        await using var ctx2 = db.CreateContext(w.TenantId);
        var custody = Custody(ctx2, w);
        var whole = await custody.ReportLossAsync(new(book.BookId, 2315601, 2315603, AccountableFormCopies.WholeSet, PhilippineTime.Today, "Market", "Bag taken"));
        var copy = await custody.ReportLossAsync(new(book.BookId, 2315604, 2315604, AccountableFormCopies.Duplicate | AccountableFormCopies.Triplicate,
            PhilippineTime.Today, null, "Two copies torn out"));
        Assert.Equal((3, 3), (whole.Value!.Reported, whole.Value.BlockedFromIssue));
        Assert.Equal((1, 1), (copy.Value!.Reported, copy.Value.BlockedFromIssue));

        await using var read = db.CreateContext(w.TenantId);
        var units = await read.AccountableDocuments.OrderBy(x => x.SerialNumber).ToListAsync();
        Assert.All(units.Take(4), x => { Assert.Equal(AccountableDocumentState.Lost, x.State); Assert.Null(x.AssignedUserId); });
        Assert.All(units.Skip(4), x => Assert.Equal(AccountableDocumentState.Assigned, x.State));
        Assert.Equal(6, await read.AccountableFormAssignments.CountAsync(x => x.ReturnedAtUtc == null));
        Assert.Equal(["Lost — entire set", "Lost — entire set", "Lost — entire set", "Duplicate and Triplicate missing"],
            (await Custody(read, w).GetExceptionsAsync()).Value!.OrderBy(x => x.DocumentNumber).Select(x => x.Kind));

        var needing = (await Custody(read, w).GetExceptionsAsync()).Value!;
        Assert.All(needing, x => Assert.True(x.NeedsFollowUp));
        Assert.Equal(4, (await Custody(read, w).GetPositionAsync(RevenueInstrumentType.OfficialReceipt)).Value!.Totals.NeedsFollowUp);

        // The external notice is added afterwards; it changes no state and clears the flag.
        await using var ctx3 = db.CreateContext(w.TenantId);
        var reference = await Custody(ctx3, w).AddReferenceAsync(new(book.BookId, 2315601, 2315604, AccountableFormReferenceKind.Loss, "Notice of Loss 2026-10-04", null));
        Assert.True(reference.IsSuccess, reference.Error);
        await using var done = db.CreateContext(w.TenantId);
        Assert.All((await Custody(done, w).GetExceptionsAsync()).Value!, x => { Assert.False(x.NeedsFollowUp); Assert.Contains("Notice of Loss 2026-10-04", x.References); });
        Assert.Equal(0, (await Custody(done, w).GetPositionAsync(RevenueInstrumentType.OfficialReceipt)).Value!.Totals.NeedsFollowUp);
        Assert.Equal(4, await done.AccountableDocuments.CountAsync(x => x.State == AccountableDocumentState.Lost));

        // A cancellation reference cannot be attached to a form that was not cancelled.
        Assert.Equal(ResultStatus.Conflict, (await Custody(done, w).AddReferenceAsync(new(book.BookId, 2315605, 2315605,
            AccountableFormReferenceKind.Cancellation, "RCD 10-2026", null))).Status);
    }

    [SkippableFact]
    public async Task ACancellationIsRecordedAtOnce_NeedsFollowUpUntilItsRcdReferenceIsAdded_AndIsNeverRevenue()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var book = await RegisterOkAsync(w, "2315601 A", "2315603 A");
        Guid id;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            id = (await ctx.AccountableDocuments.SingleAsync(x => x.DocumentNumber == "2315602 A")).Id;
            var spoiled = await Custody(ctx, w).SpoilAsync(new(id, "Misprinted", "Torn while writing"));
            Assert.True(spoiled.IsSuccess, spoiled.Error);
        }

        await using var read = db.CreateContext(w.TenantId);
        var row = Assert.Single((await Custody(read, w).GetExceptionsAsync()).Value!);
        Assert.Equal(("2315602 A", "Cancelled", true), (row.DocumentNumber, row.Kind, row.NeedsFollowUp));
        Assert.Equal(AccountableDocumentState.Voided, (await read.AccountableDocuments.SingleAsync(x => x.Id == id)).State);

        await using var ctx2 = db.CreateContext(w.TenantId);
        Assert.True((await Custody(ctx2, w).AddReferenceAsync(new(book.BookId, 2315602, 2315602, AccountableFormReferenceKind.Cancellation, "RCD 10-2026", "Original and duplicate attached"))).IsSuccess);
        await using var done = db.CreateContext(w.TenantId);
        var after = Assert.Single((await Custody(done, w).GetExceptionsAsync()).Value!);
        Assert.False(after.NeedsFollowUp);
        Assert.Equal(AccountableDocumentState.Voided, (await done.AccountableDocuments.SingleAsync(x => x.Id == id)).State);
        Assert.Equal(0, await done.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task ALossAndAnAssignmentRacingForTheSameBlankForm_LeaveItInExactlyOneConsistentState()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var book = await RegisterOkAsync(w, "2315601 A", "2315605 A");
        await using var first = db.CreateContext(w.TenantId);
        await using var second = db.CreateContext(w.TenantId);

        var assign = Custody(first, w).AssignRangeAsync(new(book.BookId, w.Ana.Id, 2315601, 2315605), RevenueInstrumentType.OfficialReceipt);
        var loss = Custody(second, w).ReportLossAsync(new(book.BookId, 2315601, 2315605, AccountableFormCopies.WholeSet, PhilippineTime.Today, "x", "Lost on the way"));
        await Task.WhenAll(assign, loss);
        var results = new[] { assign.Result.IsSuccess, loss.Result.IsSuccess };

        await using var check = db.CreateContext(w.TenantId);
        var units = await check.AccountableDocuments.ToListAsync();
        var open = await check.AccountableFormAssignments.CountAsync(x => x.ReturnedAtUtc == null);
        // Either the assignment won (units Assigned, then a later loss would have blocked them) or the loss won (Lost, no custody).
        Assert.Equal(5, units.Count);
        Assert.All(units, x => Assert.Contains(x.State, new[] { AccountableDocumentState.Assigned, AccountableDocumentState.Lost }));
        Assert.Equal(units.Count(x => x.State == AccountableDocumentState.Assigned), open);
        Assert.Contains(true, results);
    }

    [SkippableFact]
    public async Task ThePositionAndHistoryReadTheFormLedger_AsCountsAndSerials_NeverPesos()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var book = await RegisterOkAsync(w, "2315601 A", "2315610 A");
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Custody(ctx, w).AssignRangeAsync(new(book.BookId, w.Ana.Id, 2315601, 2315606), RevenueInstrumentType.OfficialReceipt)).IsSuccess);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var unit = await ctx.AccountableDocuments.SingleAsync(x => x.DocumentNumber == "2315603 A");
            unit.Consume(null, Guid.NewGuid(), DateTime.UtcNow, "office");
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var id = (await ctx.AccountableDocuments.SingleAsync(x => x.DocumentNumber == "2315605 A")).Id;
            Assert.True((await Custody(ctx, w).SpoilAsync(new(id, "Misprinted", null))).IsSuccess);
        }
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Custody(ctx, w).ReportLossAsync(new(book.BookId, 2315610, 2315610, AccountableFormCopies.WholeSet, PhilippineTime.Today, "Office", "Missing at count"))).IsSuccess);

        await using var read = db.CreateContext(w.TenantId);
        var position = (await Custody(read, w).GetPositionAsync(RevenueInstrumentType.OfficialReceipt)).Value!;
        Assert.Equal(10, position.Totals.Registered);
        Assert.Equal(3, position.Totals.InOffice);       // 2315607 – 2315609
        Assert.Equal(4, position.Totals.Assigned);       // 01, 02, 04, 06
        Assert.Equal(1, position.Totals.Issued);         // 03
        Assert.Equal(1, position.Totals.Cancelled);      // 05
        Assert.Equal(1, position.Totals.Lost);           // 10
        Assert.Equal(2, position.Totals.Skipped);        // 01 and 02 are unused but lower than the issued 03
        var ana = Assert.Single(position.Custodians);
        Assert.Equal(("Ana Reyes", 4, 1), (ana.Name, ana.Assigned, ana.Issued));
        Assert.Equal([("2315601 A", "2315602 A"), ("2315604 A", "2315604 A"), ("2315606 A", "2315606 A")],
            ana.OnHand.Select(r => (r.FirstDocumentNumber, r.LastDocumentNumber)));

        var history = (await Custody(read, w).GetHistoryAsync(RevenueInstrumentType.OfficialReceipt)).Value!;
        Assert.Contains(history, x => x.Event == "Registered" && x.Quantity == 10 && x.Serials == "2315601 A – 2315610 A" && x.From == "Municipal Treasurer");
        Assert.Contains(history, x => x.Event == "Assigned" && x.Quantity == 6 && x.To == "Ana Reyes" && x.Serials == "2315601 A – 2315606 A");
        Assert.Contains(history, x => x.Event == "Cancelled" && x.Serials == "2315605 A" && x.Detail == "Misprinted");
        Assert.Contains(history, x => x.Event == "Lost — entire set" && x.Serials == "2315610 A");
        Assert.Contains(history, x => x.Event == "Issued" && x.Serials == "2315603 A");
        Assert.All(history, x => Assert.DoesNotMatch("[A-Z]+_[A-Z]+", x.Event));

        // The month's accountability support: beginning, receipts, issued, cancelled, lost, ending - with inclusive serials.
        var raaf = (await Custody(read, w).GetRaafSupportAsync(RevenueInstrumentType.OfficialReceipt, PhilippineTime.Today.Year, PhilippineTime.Today.Month)).Value!;
        var row = Assert.Single(raaf.Rows);
        Assert.Equal((0, 10, 1, 1, 1, 7), (row.Beginning, row.Receipts, row.Issued, row.Cancelled, row.Lost, row.Ending));
        Assert.Equal(row.Beginning + row.Receipts - row.Issued - row.Cancelled - row.Lost, row.Ending);
    }
}
