using System;
using System.Collections.Generic;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality
{
    /// <summary>
    /// Platform-operator action that turns a staged onboarding configuration into a live, isolated LGU
    /// (Phase 6 activation). Atomically: applies the LGU's branding + flips it to Active, creates its
    /// facilities and fixed rates under its own <c>MunicipalityId</c>, and provisions its Head account in a
    /// must-set-password state. Cantilan (the default LGU) can never be a target. Everything commits in one
    /// transaction, so a failure leaves the municipality Upcoming with no partial data.
    /// </summary>
    public record ActivateMunicipalityCommand(
        string MunicipalityCode,
        ActivationBranding Branding,
        ActivationAdministrator Administrator,
        IReadOnlyList<ActivationFacility> Facilities,
        IReadOnlyList<ActivationRate> Rates) : IRequest<Result<ActivationResultDto>>;

    /// <summary>Official identity captured at onboarding, stamped onto the LGU's registry record.</summary>
    public record ActivationBranding(string OfficeName, string? Address, string? SealPath);

    /// <summary>The single LGU owner (Head/SuperAdmin) provisioned at activation.</summary>
    public record ActivationAdministrator(string FullName, string Username, string Email);

    /// <summary>A facility to create for the LGU, mapped to a code + billing archetype during onboarding.
    /// <paramref name="StallGroups"/> optionally provisions the facility's stalls/units (spaces) at
    /// activation; leave null/empty for transaction-only facilities (SLH/TRM/TPM).</summary>
    public record ActivationFacility(
        FacilityCode Code,
        string Name,
        string ShortName,
        BillingArchetype Archetype,
        IReadOnlyList<ActivationStallGroup>? StallGroups = null);

    /// <summary>
    /// A block of like stalls to provision for a facility — e.g. a market section (Fish · 40 stalls) or a
    /// monthly-rental space count. Creates <see cref="Count"/> stalls, each with the given rate/fees/section.
    /// Stalls are the empty spaces; occupants/contracts are added later in the live portal.
    /// </summary>
    public record ActivationStallGroup(
        int Count,
        decimal MonthlyRate,
        decimal? DailyRate,
        ApplicableFees Fees,
        MarketSection? Section = null);

    /// <summary>A fixed ordinance rate to seed for the LGU (range/monthly-rental facilities carry no fixed rate).</summary>
    public record ActivationRate(FacilityCode FacilityCode, FeeRateKey Key, decimal Amount);

    /// <summary>
    /// Result of a successful activation. <see cref="ActivationToken"/> is a one-time secret generated
    /// server-side (stored only as a hash) — the platform builds the Head's set-password link from it
    /// (e.g. <c>https://{lgu}.stalltrack.site/activate/{token}</c>). The Head is provisioned inactive and
    /// becomes active only after setting their password through that link. Returned exactly once.
    /// </summary>
    public record ActivationResultDto(
        Guid MunicipalityId,
        string MunicipalityCode,
        string AdminUsername,
        string ActivationToken,
        int FacilitiesCreated,
        int RatesCreated,
        int StallsCreated);
}
