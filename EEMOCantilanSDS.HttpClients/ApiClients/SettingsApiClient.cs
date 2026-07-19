using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
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

    public async Task<Result<bool>> SavePaymentCredentialsAsync(string? secretKey, string? publicKey, string? webhookSecret) =>
        await PutAsync<EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials.SetMunicipalityPaymentCredentialsCommand, bool>(
            "api/municipality-profile/payment",
            new EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials.SetMunicipalityPaymentCredentialsCommand(secretKey, publicKey, webhookSecret));

    public async Task<Result<bool>> VerifyMyPasswordAsync(string password) =>
        await PostAsync<object, bool>("api/municipality-profile/verify-password", new { Password = password });

    public async Task<Result<OfficeProfileEditDto>> GetOfficeProfileAsync() =>
        await GetAsync<OfficeProfileEditDto>("api/municipality-profile/office");

    public async Task<Result<bool>> UpdateOfficeProfileAsync(string officeName, string? officeAcronym, string? address, string? sealPath) =>
        await PutAsync<EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile.UpdateOfficeProfileCommand, bool>(
            "api/municipality-profile",
            new EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile.UpdateOfficeProfileCommand(
                officeName, address, sealPath, officeAcronym));
}
