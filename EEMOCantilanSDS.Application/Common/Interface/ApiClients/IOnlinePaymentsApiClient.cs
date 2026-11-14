using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Staff (admin/head) reconciliation of online payments: list those awaiting an OR and encode it.</summary>
public interface IOnlinePaymentsApiClient
{
    Task<Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>> GetAwaitingOrAsync();
    Task<Result<bool>> IssueOrNumberAsync(Guid transactionId, string orNumber);

    /// <summary>Treasury overview + recent settled online-payment history for the admin page.</summary>
    Task<Result<OnlinePaymentDashboardDto>> GetDashboardAsync();

    Task<Result<IReadOnlyList<OnlinePaymentExceptionDto>>> GetExceptionsAsync();

    /// <summary>Asks the gateway what really happened to this payment and settles it if the money is there. Never edits state blindly.</summary>
    Task<Result<ConfirmOnlinePaymentResultDto>> VerifyAsync(string reference);

    /// <summary>Head only: records what became of a held capture.</summary>
    Task<Result<bool>> ResolveExceptionAsync(Guid transactionId, OnlinePaymentDisposition disposition, string reference, string? note);

    /// <summary>Head only: the months a held capture would pay if applied to the space's rent.</summary>
    Task<Result<HeldPaymentPlanDto>> GetHeldPaymentPlanAsync(Guid transactionId);

    /// <summary>Head only: applies a held capture to those months under the OR the office issues.</summary>
    Task<Result<bool>> ApplyHeldPaymentAsync(Guid transactionId, string orNumber, string writtenRequest, IReadOnlyList<HeldPaymentMonthDto> months);
}
