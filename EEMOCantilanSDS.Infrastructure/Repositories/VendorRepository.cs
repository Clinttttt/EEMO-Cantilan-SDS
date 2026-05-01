using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public sealed class VendorRepository(AppDbContext context) : IVendorRepository
{
    public async Task<VendorRegistryDto> GetVendorRegistryAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var stalls = await context.Stalls
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Include(s => s.PaymentRecords.Where(p => p.BillingYear == year && p.BillingMonth == month))
            .ToListAsync(cancellationToken);

        var activeStalls = stalls.Where(s => s.Status == StallStatus.Active).ToList();
        var closedStalls = stalls.Where(s => s.Status == StallStatus.Closed).ToList();

        var paidCount = stalls.Count(s =>
        {
            var payment = s.PaymentRecords.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
            return payment?.Status == PaymentStatus.Paid;
        });

        var unpaidCount = activeStalls.Count(s =>
        {
            var payment = s.PaymentRecords.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
            return payment == null || payment.Status == PaymentStatus.Unpaid;
        });

        var totalOutstanding = activeStalls.Sum(s =>
        {
            var payment = s.PaymentRecords.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
            if (payment == null)
                return s.MonthlyRate;

            var totalBill = payment.BaseRentalAmount + (payment.ElecAmount ?? 0) + (payment.WaterAmount ?? 0) + (payment.FishKilos.HasValue ? payment.FishKilos.Value * 1.0m : 0);
            var amountPaid = payment.Status == PaymentStatus.Paid ? totalBill : payment.Status == PaymentStatus.Partial ? payment.PartialAmount : 0;
            return totalBill - amountPaid;
        });

        var monthlyTarget = activeStalls.Sum(s => s.MonthlyRate);

        var vendors = stalls.Select(s =>
        {
            var activeContract = s.Contracts.FirstOrDefault(c => c.IsActive);
            var payment = s.PaymentRecords.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);

            return new VendorListItemDto(
                s.Id,
                s.StallNo,
                activeContract?.ActualOccupant ?? "No Contract",
                activeContract?.NameOnContract,
                payment?.ORNumber,
                s.Facility!.Code,
                s.Facility.Name,
                s.Section,
                s.Section.HasValue ? s.Section.Value.ToString() : null,
                s.AreaLocation,
                s.AreaLocation.HasValue ? s.AreaLocation.Value.ToString() : null,
                s.MonthlyRate,
                s.Status,
                payment?.Status ?? PaymentStatus.Unpaid
            );
        }).ToList();

        return new VendorRegistryDto(
            stalls.Count,
            activeStalls.Count,
            closedStalls.Count,
            paidCount,
            unpaidCount,
            totalOutstanding,
            monthlyTarget,
            vendors
        );
    }
}
