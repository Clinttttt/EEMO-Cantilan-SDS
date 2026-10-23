using EEMOCantilanSDS.Api.Controllers;
using EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Auth.VerifyMyPassword;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EEMOCantilanSDS.Testing.Api;

public class PaymentCredentialEndpointReauthenticationTests
{
    private static MunicipalityProfileController Controller(Mock<ISender> mediator)
        => new(mediator.Object, new ConfigurationBuilder().Build(), Mock.Of<IQrCodeGenerator>());

    [Fact]
    public async Task MissingPasswordCannotSaveOrClearMerchantAccount()
    {
        var mediator = new Mock<ISender>();
        var controller = Controller(mediator);
        var save = await controller.UpdatePaymentAsync(
            new SetMunicipalityPaymentCredentialsCommand("sk_live_fake", null, null));
        var clear = await controller.UpdatePaymentAsync(
            new SetMunicipalityPaymentCredentialsCommand(null, null, null));

        Assert.IsType<BadRequestObjectResult>(save.Result);
        Assert.IsType<BadRequestObjectResult>(clear.Result);
        mediator.Verify(s => s.Send(It.IsAny<SetMunicipalityPaymentCredentialsCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WrongPasswordCannotChangeMerchantAccount()
    {
        var mediator = new Mock<ISender>();
        mediator.Setup(s => s.Send(It.IsAny<VerifyMyPasswordQuery>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Result<bool>.Success(false));

        var response = await Controller(mediator).UpdatePaymentAsync(
            new SetMunicipalityPaymentCredentialsCommand("sk_live_fake", null, null, "bad-password"));
        Assert.IsType<ForbidResult>(response.Result);
        mediator.Verify(s => s.Send(It.IsAny<SetMunicipalityPaymentCredentialsCommand>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifiedPasswordAllowsAuthorizedMerchantUpdate()
    {
        var mediator = new Mock<ISender>();
        mediator.Setup(s => s.Send(It.IsAny<VerifyMyPasswordQuery>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Result<bool>.Success(true));
        mediator.Setup(s => s.Send(It.IsAny<SetMunicipalityPaymentCredentialsCommand>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Result<PaymentSetupResultDto>.Success(
                new PaymentSetupResultDto(true, false, "Saved")));

        await Controller(mediator).UpdatePaymentAsync(
            new SetMunicipalityPaymentCredentialsCommand("sk_test_fake", null, null, "verified-password"));
        mediator.Verify(s => s.Send(It.Is<SetMunicipalityPaymentCredentialsCommand>(c =>
            c.SecretKey == "sk_test_fake"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
