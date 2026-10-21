using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmRentMonthFinancialTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Optional_OR_is_consumed_by_the_exact_month_collection_and_replay_creates_no_second_receipt(bool outstanding)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync();
        var world = await SeedOpening(new(2026, 10, 1));
        await EnableOutstanding(world);
        await using var db = fixture.Create(world.World.Tenant);
        var month = OpeningWorkflow(db, world);
        await PrepareAndActivateOpening(db, world, month);
        var book = AccountableFormBook.Receive(world.World.Tenant, RevenueInstrumentType.OfficialReceipt,
            "Synthetic OR book", "TEST-", 1, 2, 5, DateTime.UtcNow, world.World.Actor.ToString(), "Fixture");
        var document = AccountableDocument.Register(book, 1, "Fixture");
        db.AddRange(book, document); await db.SaveChangesAsync();
        var operation = Guid.NewGuid();
        Guid collectionId;
        if (outstanding)
        {
            var flow = Outstanding(db, world, prospective: true);
            var quote = await flow.QuoteAsync(world.World.Stall, world.Contract, 50);
            Assert.True(quote.IsSuccess, quote.Error); var q = quote.Value!;
            var request = new NpmOutstandingRentPayment(operation, q.StallId, q.ContractId, q.BusinessDate, 50, q.QuoteToken, document.DocumentNumber);
            var result = await flow.PostAsync(request); Assert.True(result.IsSuccess, result.Error);
            collectionId = result.Value!.CollectionId;
            Assert.True((await flow.PostAsync(request)).Value!.Existing);
            Assert.False((await flow.PostAsync(request with { OfficialReceiptNumber = "TEST-00002" })).IsSuccess);
        }
        else
        {
            var q = (await month.QuoteAsync(world.World.Stall, new(2026, 10, 1), new(2026, 10, 20), NpmRentPaymentScope.DailyInstallment)).Value!;
            var request = new NpmRentMonthPayment(operation, world.World.Stall, q.PeriodStart, q.BusinessDate,
                NpmRentPaymentScope.DailyInstallment, q.EligibleAmount, q.Token, q.InstallmentDate, document.DocumentNumber);
            var result = await month.PostAsync(request); Assert.True(result.IsSuccess, result.Error);
            collectionId = result.Value!.CollectionId;
            Assert.True((await month.PostAsync(request)).Value!.Existing);
            Assert.False((await month.PostAsync(request with { OfficialReceiptNumber = "TEST-00002" })).IsSuccess);
        }
        await using var verify = fixture.Create(world.World.Tenant);
        var saved = await verify.AccountableDocuments.SingleAsync();
        Assert.Equal(AccountableDocumentState.Consumed, saved.State);
        Assert.Equal(collectionId, saved.CollectionId); Assert.Equal(operation, saved.ClientOperationId);
        Assert.Single(await verify.Collections.ToListAsync());
        Assert.StartsWith("SRC-", (await verify.Collections.SingleAsync()).ReferenceCode);
        var activity = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(verify)
            .GetAsync(world.World.Tenant, new(2026, 10, 1), new(2026, 10, 31));
        var shown = Assert.Single(activity.Where(x => x.CollectionId == collectionId));
        Assert.Equal("TEST-00001", shown.DocumentNumber);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, shown.InstrumentType);
    }

    [SkippableTheory]
    [InlineData("unknown")]
    [InlineData("other-custodian")]
    [InlineData("cash-ticket")]
    public async Task Invalid_OR_refuses_outstanding_payment_without_cash_or_source_mutation(string scenario)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedOpening(new(2026, 10, 20)); await EnableOutstanding(world);
        await using var db = fixture.Create(world.World.Tenant);
        await PrepareAndActivateOpening(db, world, OpeningWorkflow(db, world));
        if (scenario != "unknown")
        {
            var book = AccountableFormBook.Receive(world.World.Tenant,
                scenario == "cash-ticket" ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt,
                "Synthetic receipt", "TEST-", 1, 1, 5, DateTime.UtcNow, "Fixture", "Fixture");
            var document = AccountableDocument.Register(book, 1, "Fixture");
            if (scenario == "other-custodian") document.AssignTo(Guid.NewGuid(), "Fixture");
            db.AddRange(book, document); await db.SaveChangesAsync();
        }
        var flow = Outstanding(db, world, prospective: true);
        var q = (await flow.QuoteAsync(world.World.Stall, world.Contract, 30)).Value!;
        var result = await flow.PostAsync(new(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, 30, q.QuoteToken, "TEST-00001"));
        Assert.False(result.IsSuccess);
        await using var verify = fixture.Create(world.World.Tenant);
        Assert.Empty(await verify.Collections.ToListAsync()); Assert.Empty(await verify.CollectionAllocations.ToListAsync());
        Assert.Equal(900, (await Outstanding(verify, world, prospective: true).QuoteAsync(q.StallId, q.ContractId)).Value!.TotalOutstanding);
    }
}
