using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public async Task<PaymentRecord?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await context.PaymentRecords.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<PaymentRecordDto?> GetPaymentRecordAsync(Guid stallId, int year, int month, CancellationToken ct)
    {
        var payment = await context.PaymentRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.StallId == stallId && p.BillingYear == year && p.BillingMonth == month && !p.IsDeleted, ct);

        if (payment == null)
            return null;

        return new PaymentRecordDto(
            payment.Id,
            payment.Status,
            payment.ORNumber,
            payment.BaseRentalAmount,
            payment.ElecAmount,
            payment.WaterAmount,
            payment.FishFeeAmount,
            payment.AmountPaid,
            payment.BalanceDue
        );
    }

    public async Task<IReadOnlyList<FacilityPaymentRecordDto>> GetFacilityPaymentRecordsAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var payments = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.Stall!.Facility!.Code == facilityCode && p.BillingYear == year && p.BillingMonth == month && !p.IsDeleted)
            .ToListAsync(ct);

        // AmountPaid is a C# computed property — map in memory, not in SQL
        return payments
            .Select(p => new FacilityPaymentRecordDto(p.StallId, p.Status, p.ORNumber, p.AmountPaid))
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct)
    {
        var now = PhilippineTime.Now;
        var startDate = now.AddMonths(-11);

        var payments = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.StallId == stallId && !p.IsDeleted)
            .Where(p => (p.BillingYear > startDate.Year) || (p.BillingYear == startDate.Year && p.BillingMonth >= startDate.Month))
            .OrderByDescending(p => p.BillingYear)
            .ThenByDescending(p => p.BillingMonth)
            .ToListAsync(ct);

        return payments.Select(p => new PaymentHistoryDto(
            $"{p.BillingYear:0000}-{p.BillingMonth:00}",
            p.Status,
            p.TotalBill,
            p.AmountPaid,
            p.BalanceDue,
            p.ORNumber,
            p.PaidAt,
            null
        )).ToList();
    }

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct)
    {
        if (await context.PaymentRecords.AnyAsync(p => p.ORNumber == orNumber, ct)) return false;
        if (await context.DailyCollections.AnyAsync(d => d.ORNumber == orNumber, ct)) return false;
        if (await context.SlaughterTransactions.AnyAsync(s => s.ORNumber == orNumber, ct)) return false;
        if (await context.TpmAttendances.AnyAsync(a => a.ORNumber == orNumber, ct)) return false;
        if (await context.TrmTrips.AnyAsync(t => t.ORNumber == orNumber, ct)) return false;
        return true;
    }

    public async Task AddAsync(PaymentRecord payment, CancellationToken ct)
    {
        await context.PaymentRecords.AddAsync(payment, ct);
    }

    public async Task UpdateAsync(PaymentRecord payment, CancellationToken ct)
    {
        context.PaymentRecords.Update(payment);
        await Task.CompletedTask;
    }
}
