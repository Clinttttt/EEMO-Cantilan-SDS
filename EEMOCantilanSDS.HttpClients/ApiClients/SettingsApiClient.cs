using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Command.Municipalities.TestPaymentConnection;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class SettingsApiClient : HandleResponse, ISettingsApiClient
{
    public SettingsApiClient(HttpClient http) : base(http)
    {
    }

    public async Task<Result<SystemSettingsDto>> GetSystemSettingsAsync() =>
        await GetAsync<SystemSettingsDto>("api/Settings");

    public async Task<Result<PaymentSettingsDto>> GetPaymentSettingsAsync() =>
        await GetAsync<PaymentSettingsDto>("api/municipality-profile/payment");

    public async Task<Result<PaymentSetupResultDto>> SavePaymentCredentialsAsync(string? secretKey, string? publicKey, string? webhookSecret) =>
        await PutAsync<EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials.SetMunicipalityPaymentCredentialsCommand, PaymentSetupResultDto>(
            "api/municipality-profile/payment",
            new EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials.SetMunicipalityPaymentCredentialsCommand(secretKey, publicKey, webhookSecret));

    public async Task<Result<PaymentConnectionTestDto>> TestPaymentConnectionAsync(string? secretKey) =>
        await PostAsync<TestPaymentConnectionCommand, PaymentConnectionTestDto>(
            "api/municipality-profile/payment/test",
            new TestPaymentConnectionCommand(secretKey));

    public async Task<Result<bool>> VerifyMyPasswordAsync(string password) =>
        await PostAsync<object, bool>("api/municipality-profile/verify-password", new { Password = password });

    public async Task<Result<OfficeProfileEditDto>> GetOfficeProfileAsync() =>
        await GetAsync<OfficeProfileEditDto>("api/municipality-profile/office");

    public async Task<Result<bool>> UpdateOfficeProfileAsync(string officeName, string? officeAcronym, string? address, string? sealPath) =>
        await PutAsync<EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile.UpdateOfficeProfileCommand, bool>(
            "api/municipality-profile",
            new EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile.UpdateOfficeProfileCommand(
                officeName, address, sealPath, officeAcronym));

    /// <summary>Replaces this LGU's signatory lines; an empty list restores the office's default trio.</summary>
    public async Task<Result<bool>> SaveReportSignatoriesAsync(
        IReadOnlyList<EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories.ReportSignatoryDto>? signatories,
        string? align = null) =>
        await PutAsync<EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories.SetReportSignatoriesCommand, bool>(
            "api/municipality-profile/signatories",
            new EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories.SetReportSignatoriesCommand(signatories, align));
}
