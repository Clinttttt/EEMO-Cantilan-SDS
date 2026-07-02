using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Vendors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
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
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Include(s => s.PaymentRecords.Where(p => p.BillingYear == year && p.BillingMonth == month))
            .ToListAsync(cancellationToken);

        // Only current vendors belong in the registry: an ACTIVE stall whose contract term has lapsed
        // (today past effectivity + duration) is excluded from BOTH the counts and the list. Closed
        // stalls and stalls without a dated contract are unaffected. Mirrors Contract.IsExpired.
        var today = PhilippineTime.Today;
        var visibleStalls = stalls.Where(s =>
        {
            if (s.Status != StallStatus.Active) return true;
            var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
            if (contract is null) return true;
            return today <= contract.EffectivityDate.AddYears(contract.DurationYears);
        }).ToList();

        var activeStalls = visibleStalls.Where(s => s.Status == StallStatus.Active).ToList();
        var closedStalls = visibleStalls.Where(s => s.Status == StallStatus.Closed).ToList();

        // Monthly rent collection metrics apply only to monthly-billed facilities.
        // NPM is collected daily (DailyCollection), so it is excluded from the monthly
        // paid/unpaid/outstanding/target figures to avoid falsely counting NPM vendors as unpaid.
        var monthlyStalls = activeStalls.Where(s => s.Facility!.Code != FacilityCode.NPM).ToList();

        var paidCount = monthlyStalls.Count(s =>
        {
            var payment = s.PaymentRecords.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
            return payment?.Status == PaymentStatus.Paid;
        });

        var unpaidCount = monthlyStalls.Count(s =>
        {
            var payment = s.PaymentRecords.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
            return payment == null || payment.Status == PaymentStatus.Unpaid;
        });

        var totalOutstanding = monthlyStalls.Sum(s =>
        {
            var payment = s.PaymentRecords.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
            if (payment == null)
                return s.MonthlyRate;

            var totalBill = payment.BaseRentalAmount + (payment.ElecAmount ?? 0) + (payment.WaterAmount ?? 0) + (payment.FishKilos.HasValue ? payment.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0);
            var amountPaid = payment.Status == PaymentStatus.Paid ? totalBill : payment.Status == PaymentStatus.Partial ? payment.PartialAmount : 0;
            return totalBill - amountPaid;
        });

        var monthlyTarget = monthlyStalls.Sum(s => s.MonthlyRate);

        var vendors = visibleStalls.Select(s =>
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
                payment?.Status ?? PaymentStatus.Unpaid,
                activeContract?.EffectivityDate.ToDateTime(TimeOnly.MinValue),
                activeContract?.DurationYears ?? 0,
                s.AreaSqm,
                s.AreaNote
            );
        }).ToList();

        return new VendorRegistryDto(
            visibleStalls.Count,
            activeStalls.Count,
            closedStalls.Count,
            monthlyStalls.Count,
            paidCount,
            unpaidCount,
            totalOutstanding,
            monthlyTarget,
            vendors
        );
    }
}
