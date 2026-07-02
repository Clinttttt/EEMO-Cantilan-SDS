using System;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Stalls;

public static class StallContractStatus
{
    /// <summary>
    /// Whether a stall should be listed as a current vendor on its facility page.
    /// An ACTIVE stall whose contract term has lapsed (today is past effectivity + duration years)
    /// is hidden — its contract is out of date. Closed stalls and stalls without a dated contract
    /// are unaffected. Mirrors the domain rule <c>Contract.IsExpired</c> (expired when today &gt; expiry).
    /// </summary>
    public static bool IsCurrentVendor(StallDto dto) => IsCurrentVendor(dto, PhilippineTime.Today);

    public static bool IsCurrentVendor(StallDto dto, DateOnly today)
    {
        if (dto.Status != StallStatus.Active) return true;       // closed/other → leave as-is
        if (!dto.ContractDate.HasValue) return true;             // no dated contract → can't be "expired"

        var expiry = DateOnly.FromDateTime(dto.ContractDate.Value.AddYears(dto.ContractYears));
        return expiry >= today;
    }
}
