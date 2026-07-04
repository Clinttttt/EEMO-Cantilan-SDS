using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Requests.Mobile;

/// <summary>A collector's payment against an NPM utility bill (electricity + water settled independently).</summary>
public record RecordMobileUtilityPaymentRequest(
    Guid BillId,
    PaymentStatus ElecStatus,
    decimal? ElecPartialAmount,
    PaymentStatus WaterStatus,
    decimal? WaterPartialAmount,
    string? ElecORNumber,
    string? WaterORNumber,
    Guid? ClientOperationId);
