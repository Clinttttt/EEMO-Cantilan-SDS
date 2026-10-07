using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public enum SourceIdentityKind { Occupancy = 1, SpaceAccount = 2, FishMeatVendorRegistration = 3 }
public sealed record CollectionSourceIdentity(SourceIdentityKind Kind, Guid Id);
public sealed record CollectionSourceSearchResult(CollectionSourceIdentity Identity, string DisplayName, string Context,
    string OperationCode, int? TaxYear = null, FishMeatVendorType? VendorType = null);
public enum CollectionFamily { Market = 1, Rent = 2, Space = 3, Terminal = 4, Slaughterhouse = 5 }
public sealed record RegisterFishMeatVendorRequest(Guid ClientOperationId, int TaxYear, FishMeatVendorType VendorType,
    VendorRegistrationKind RegistrationKind, string DisplayName, string? BusinessName = null, string? Address = null, string? Reference = null);
public sealed record FishMeatVendorRegistrationDto(Guid Id, int TaxYear, FishMeatVendorType VendorType,
    VendorRegistrationKind RegistrationKind, string DisplayName, string? BusinessName, string? Address, string? Reference);
public sealed record SourceNativeChargeIntent(string OperationCode, Guid? VendorRegistrationId = null,
    TerminalSection? Section = null, Guid? VehicleClassId = null, int? CashTicketCount = null, decimal? Kilograms = null);
public sealed record SourceNativeCollectionRequest(Guid ClientOperationId, DateOnly BusinessDate, decimal AmountReceived,
    SourceNativeChargeIntent Charge, string? PayerSnapshot = null, string? ExpectedSourceVersion = null);
public sealed record SourceNativeChargeQuote(string OperationCode, string DisplayName, string Context,
    RevenueInstrumentType Instrument, decimal Amount, string SourceVersion, SourceNativeChargeIntent Charge,
    Guid? RateId = null, DateOnly? RateEffectiveDate = null, decimal? Rate = null, FishMeatVendorType? VendorType = null);
public sealed record TerminalVehicleChoice(Guid VehicleClassId, string Code, string DisplayName, TerminalSection Section,
    decimal Rate, Guid RateId, DateOnly EffectiveDate);
public sealed record TerminalVehicleMappingRequest(Guid VehicleClassId, TerminalSection Section);
public sealed record SourceNativeActivityDto(Guid CollectionId, string SRC, DateOnly BusinessDate, DateTime RecordedAtUtc,
    string OperationCode, TerminalSection? Section, Guid? VendorRegistrationId, string? PayerSnapshot,
    Guid? CollectorId, string? CollectorName, decimal Amount, decimal NetAmount, string State,
    int? CashTicketCount, Guid? VehicleClassId, Guid? RateId, decimal? Rate, decimal? Kilograms, string? VehicleClassName = null);
