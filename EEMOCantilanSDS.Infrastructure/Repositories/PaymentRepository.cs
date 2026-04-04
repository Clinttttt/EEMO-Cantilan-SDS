using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Payments;
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
            .FirstOrDefaultAsync(p => p.StallId == stallId && p.BillingYear == year && p.BillingMonth == month, ct);

        if (payment == null)
            return null;

        var totalBill = payment.BaseRentalAmount + (payment.ElecAmount ?? 0) + (payment.WaterAmount ?? 0) + (payment.FishKilos.HasValue ? payment.FishKilos.Value * 1.0m : 0);
        var amountPaid = payment.Status == Domain.Enums.PaymentStatus.Paid ? totalBill : payment.Status == Domain.Enums.PaymentStatus.Partial ? payment.PartialAmount : 0;

        return new PaymentRecordDto(
            payment.Id,
            payment.Status,
            payment.ORNumber,
            payment.BaseRentalAmount,
            payment.ElecAmount,
            payment.WaterAmount,
            payment.FishKilos.HasValue ? payment.FishKilos.Value * 1.0m : null,
            amountPaid,
            totalBill - amountPaid
        );
    }

    public async Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var startDate = now.AddMonths(-11);

        var payments = await context.PaymentRecords
            .Where(p => p.StallId == stallId)
            .Where(p => (p.BillingYear > startDate.Year) || (p.BillingYear == startDate.Year && p.BillingMonth >= startDate.Month))
            .OrderByDescending(p => p.BillingYear)
            .ThenByDescending(p => p.BillingMonth)
            .ToListAsync(ct);

        return payments.Select(p => new PaymentHistoryDto(
            $"{p.BillingYear:0000}-{p.BillingMonth:00}",
            p.Status,
            p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos.HasValue ? p.FishKilos.Value * 1.0m : 0),
            p.Status == Domain.Enums.PaymentStatus.Paid 
                ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos.HasValue ? p.FishKilos.Value * 1.0m : 0)
                : p.Status == Domain.Enums.PaymentStatus.Partial ? p.PartialAmount : 0,
            p.Status == Domain.Enums.PaymentStatus.Unpaid 
                ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos.HasValue ? p.FishKilos.Value * 1.0m : 0)
                : p.Status == Domain.Enums.PaymentStatus.Partial 
                    ? (p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos.HasValue ? p.FishKilos.Value * 1.0m : 0)) - p.PartialAmount 
                    : 0,
            p.ORNumber,
            p.PaidAt
        )).ToList();
    }

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct)
    {
        var existsInPayments = await context.PaymentRecords.AnyAsync(p => p.ORNumber == orNumber, ct);
        var existsInDaily = await context.DailyCollections.AnyAsync(d => d.ORNumber == orNumber, ct);
        return !existsInPayments && !existsInDaily;
    }

    public async Task UpdateAsync(PaymentRecord payment, CancellationToken ct)
    {
        context.PaymentRecords.Update(payment);
        await Task.CompletedTask;
    }
}
