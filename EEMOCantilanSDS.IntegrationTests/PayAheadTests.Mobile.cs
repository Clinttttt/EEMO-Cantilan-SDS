using EEMOCantilanSDS.Application.Command.Payments.RecordPayAhead;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Rent taken ahead by a collector (IA-071, 2026-11-25): only in a facility they are assigned to, the office's own recording, and never twice on a resend.</summary>
public partial class PayAheadTests
{
    private sealed class FieldCollector(Guid tenant, Guid id) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => id;
        public Guid? MunicipalityId => tenant;
        public string? Username => "field-collector";
        public string? Role => "Collector";
        public Guid? CollectorId => id;
        public string? MunicipalityCode => "TEST";
        public AdminUserDto? GetCurrentUser() => null;
    }

    private sealed class OfficeSender(RecordPayAheadCommandHandler office) : MediatR.ISender
    {
        public async Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken ct = default) =>
            request is RecordPayAheadCommand command ? (TResponse)(object)await office.Handle(command, ct) : throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : MediatR.IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private async Task<Guid> CollectorAsync(Guid tenant, Guid stall, bool assigned)
    {
        await using var write = db.CreateContext(tenant);
        var facility = await write.Stalls.Where(s => s.Id == stall).Select(s => s.FacilityId).SingleAsync();
        var collector = CollectorUser.Create("Field Collector", "FC-" + Guid.NewGuid().ToString("N")[..4], "fc-" + Guid.NewGuid().ToString("N")[..8], null, null, new HashedPassword("test-hash"), tenant);
        write.Add(collector);
        if (assigned) write.Add(CollectorFacilityAssignment.Create(collector.Id, facility, FacilityCode.TCC));
        await write.SaveChangesAsync();
        return collector.Id;
    }

    private async Task<Result<bool>> CollectAheadAsync(Guid tenant, Guid collector, Guid stall, int months, decimal total, string or = "OR-2001")
    {
        await using var context = db.CreateContext(tenant);
        var caller = new FieldCollector(tenant, collector);
        var office = new RecordPayAheadCommandHandler(new PaymentRepository(context), new StallRepository(context), Rules(context, tenant), caller,
            new UnitOfWork(context), new QuietCache(), new Code());
        return await new RecordMobilePayAheadCommandHandler(new StallRepository(context), new CollectorRepository(context), caller, new PaymentRepository(context), new OfficeSender(office))
            .Handle(new RecordMobilePayAheadCommand(stall, ThisMonth.Year, ThisMonth.Month, months, total, or), CancellationToken.None);
    }

    [SkippableFact]
    public async Task ACollector_PaysAhead_OnlyWhereAssigned_AndAResendPaysNothingMore()
    {
        var seeded = await SeedAsync("AHD-P");
        var outsider = await CollectorAsync(seeded.Tenant, seeded.Stall, assigned: false);
        var assigned = await CollectorAsync(seeded.Tenant, seeded.Stall, assigned: true);

        Assert.Equal(ResultStatus.Forbidden, (await CollectAheadAsync(seeded.Tenant, outsider, seeded.Stall, 2, 2 * Rate)).Status);
        Assert.Empty(await RowsAsync(seeded.Tenant, seeded.Stall));

        var paid = await CollectAheadAsync(seeded.Tenant, assigned, seeded.Stall, 2, 2 * Rate);
        Assert.True(paid.IsSuccess, paid.Error);
        // The phone lost the answer and sent it again: the same months under the same receipt are the payment already recorded.
        var resend = await CollectAheadAsync(seeded.Tenant, assigned, seeded.Stall, 2, 2 * Rate);
        Assert.True(resend.IsSuccess, resend.Error);

        var rows = await RowsAsync(seeded.Tenant, seeded.Stall);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal((PaymentStatus.Paid, "OR-2001", (Guid?)assigned), (r.Status, r.ORNumber, r.CollectorId)));
        // Another receipt for months already paid is refused by the office, never recorded as a second payment.
        Assert.Equal(ResultStatus.Conflict, (await CollectAheadAsync(seeded.Tenant, assigned, seeded.Stall, 2, 2 * Rate, "OR-2002")).Status);
        Assert.Equal(2, (await RowsAsync(seeded.Tenant, seeded.Stall)).Count);
    }
}
