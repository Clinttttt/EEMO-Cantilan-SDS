using System;
using System.Collections.Generic;

namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>Read-only system configuration overview surfaced on the Settings page.</summary>
public record SystemSettingsDto(
    OfficeProfileDto Office,
    SecurityPolicyDto Security,
    CollectionRulesDto Collection,
    SystemInfoDto System,
    IReadOnlyList<FacilityRuleDto> Facilities);

public record OfficeProfileDto(
    string Office,
    string Municipality,
    string Province,
    string SystemName,
    string ReceiptsIssuedBy);

public record SecurityPolicyDto(
    int AccessTokenMinutes,
    int RefreshTokenDays,
    int MaxFailedLoginAttempts,
    int LockoutMinutes,
    IReadOnlyList<string> Roles);

public record CollectionRulesDto(
    int DelinquentThresholdMonths,
    int ArrearsMinMonths,
    int ArrearsMaxMonths,
    int DelinquencyWindowMonths,
    int ContractExpiryWarningMonths,
    string TimeZone);

public record SystemInfoDto(
    string ApplicationName,
    string Version,
    string Environment,
    string TimeZone,
    DateTime ServerDate);

public record FacilityRuleDto(
    string Code,
    string Name,
    string Model,
    string Rate,
    string Cadence);
