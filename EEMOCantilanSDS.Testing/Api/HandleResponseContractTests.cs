using EEMOCantilanSDS.Api.Controllers;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EEMOCantilanSDS.Testing.Api;

/// <summary>
/// What the API actually returns for each kind of handler failure.
///
/// <para>
/// A characterisation test, written BEFORE moving the translation from HTTP numbers to error categories. Every one of these
/// status codes is read by something: the portal branches on 409 to say "that username is taken", on 404 to say the account is
/// gone, and treats 401/403 as "your session ended" rather than as an error to display. The mobile app and the sync path read
/// them too. So the point of the refactor is that Application stops NAMING HTTP numbers — not that any response changes, and
/// this file is what holds the API to that.
/// </para>
/// </summary>
public class HandleResponseContractTests
{
    /// <summary>Exposes the protected translation. Nothing else about the controller is exercised.</summary>
    private sealed class TestController(ISender sender) : ApiBaseController(sender)
    {
        public ActionResult<T> Translate<T>(Result<T> result) => HandleResponse(result);
    }

    private static TestController Controller() => new(Mock.Of<ISender>());

    private static int StatusOf<T>(ActionResult<T> action) => action.Result switch
    {
        ObjectResult o => o.StatusCode ?? 0,
        StatusCodeResult s => s.StatusCode,
        _ => 0,
    };

    [Fact]
    public void ASuccessIsTwoHundredAndCarriesTheValue()
    {
        var action = Controller().Translate(Result<string>.Success("ninety pesos"));

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(200, ok.StatusCode);
        Assert.Equal("ninety pesos", ok.Value);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(423)]
    [InlineData(500)]
    [InlineData(502)]
    public void EachFailureKeepsItsOwnStatus(int code)
    {
        // The whole set the switch handles, so a refactor cannot quietly collapse two of them into one.
        var action = Controller().Translate(Result<string>.Failure("stated reason", code));

        Assert.Equal(code, StatusOf(action));
    }

    [Fact]
    public void AnUnrecognisedStatusFallsBackToBadRequest()
    {
        var action = Controller().Translate(Result<string>.Failure("odd", 418));

        Assert.Equal(400, StatusOf(action));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(409)]
    [InlineData(423)]
    [InlineData(500)]
    [InlineData(502)]
    public void TheReasonIsCarriedInTheBodyForTheKindsThatExplainThemselves(int code)
    {
        // These are the statuses whose body the portal shows to the office, so the message has to survive the translation.
        var action = Controller().Translate(Result<string>.Failure("that OR number is already used", code));

        var body = Assert.IsAssignableFrom<ObjectResult>(action.Result).Value;
        Assert.Contains("that OR number is already used", System.Text.Json.JsonSerializer.Serialize(body));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    public void TheseSayNothingBeyondTheStatus(int code)
    {
        // Deliberately bodiless: 401 must not hint whether an account exists, and 404 has nothing to add.
        var action = Controller().Translate(Result<string>.Failure("internal detail", code));

        Assert.IsNotType<ObjectResult>(action.Result);
        Assert.Equal(code, StatusOf(action));
    }

    [Fact]
    public void ValidationErrorsAreReturnedPerField()
    {
        // The portal renders these against the individual inputs, so the shape matters as much as the status.
        var action = Controller().Translate(Result<string>.ValidationFailure(new()
        {
            ["Username"] = ["Username is required."],
            ["Amount"] = ["Amount must be greater than zero."],
        }));

        var bad = Assert.IsType<BadRequestObjectResult>(action.Result);
        var json = System.Text.Json.JsonSerializer.Serialize(bad.Value);
        Assert.Contains("Username is required.", json);
        Assert.Contains("Amount must be greater than zero.", json);
        Assert.Contains("Errors", json);
    }

    [Fact]
    public void AFailureNeverCarriesAValue()
    {
        var result = Result<string>.Failure("no", 409);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
