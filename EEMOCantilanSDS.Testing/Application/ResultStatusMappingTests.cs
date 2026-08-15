namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The two directions a <see cref="Result{T}"/> travels, and that they agree.
///
/// <para>
/// Handlers state a <see cref="ResultStatus"/> and the API turns it into a status code. The portal goes the other way: its API
/// clients build a Result FROM a real HTTP response, so the number is the input there. Both meet in the same mapping, and the
/// portal now branches on the CATEGORY — <c>Status == ResultStatus.Conflict</c> rather than <c>StatusCode == 409</c> — so if the
/// two directions ever disagreed, the office would be shown the wrong message about its own data.
/// </para>
/// </summary>
public class ResultStatusMappingTests
{
    [Theory]
    [InlineData(400, ResultStatus.Invalid)]
    [InlineData(401, ResultStatus.Unauthorized)]
    [InlineData(403, ResultStatus.Forbidden)]
    [InlineData(404, ResultStatus.NotFound)]
    [InlineData(409, ResultStatus.Conflict)]
    [InlineData(423, ResultStatus.Locked)]
    [InlineData(500, ResultStatus.Failed)]
    [InlineData(502, ResultStatus.UpstreamFailed)]
    public void AResultBuiltFromAnHttpResponseCarriesTheMatchingCategory(int httpStatus, ResultStatus expected)
    {
        // This is exactly what HttpClients/HandleResponse.cs does with a failed response.
        var result = Result<string>.Failure("as returned by the API", httpStatus);

        Assert.Equal(expected, result.Status);
        Assert.Equal(httpStatus, result.StatusCode);
    }

    [Theory]
    [InlineData(ResultStatus.Invalid, 400)]
    [InlineData(ResultStatus.Unauthorized, 401)]
    [InlineData(ResultStatus.Forbidden, 403)]
    [InlineData(ResultStatus.NotFound, 404)]
    [InlineData(ResultStatus.Conflict, 409)]
    [InlineData(ResultStatus.Locked, 423)]
    [InlineData(ResultStatus.Failed, 500)]
    [InlineData(ResultStatus.UpstreamFailed, 502)]
    public void ACategoryStatedByAHandlerReportsTheMatchingStatusCode(ResultStatus status, int expected)
    {
        var result = Result<string>.Failure("stated by a handler", status);

        Assert.Equal(status, result.Status);
        Assert.Equal(expected, result.StatusCode);
    }

    [Fact]
    public void AStatusCodeNobodyMapsKeepsItsNumberAndIsNotMistakenForAKnownKind()
    {
        // 429 is real here: the sign-in and password-reset endpoints are rate limited, so the portal does receive it. It has no
        // category of its own, and the important part is what it must NOT be taken for. A future reader tempted to rewrite a
        // "StatusCode == 400" check as "Status == Invalid" would silently start treating a throttled request as a bad one.
        var throttled = Result<string>.Failure("too many attempts", 429);

        Assert.Equal(429, throttled.StatusCode);
        Assert.NotEqual(ResultStatus.Unauthorized, throttled.Status);
        Assert.NotEqual(ResultStatus.Forbidden, throttled.Status);
        Assert.NotEqual(ResultStatus.NotFound, throttled.Status);
        Assert.NotEqual(ResultStatus.Conflict, throttled.Status);
        Assert.NotEqual(ResultStatus.Locked, throttled.Status);
    }

    [Fact]
    public void ASuccessIsOkAndReportsTwoHundred()
    {
        var result = Result<string>.Success("ninety pesos");

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(ResultStatus.Ok, result.Status);
    }

    [Fact]
    public void TheNamedFailuresCarryTheirOwnCategory()
    {
        // The factories the handlers actually use, so a rename or a reordering of the enum cannot quietly repoint one of them.
        Assert.Equal(ResultStatus.NotFound, Result<string>.NotFound().Status);
        Assert.Equal(ResultStatus.Unauthorized, Result<string>.Unauthorized().Status);
        Assert.Equal(ResultStatus.Forbidden, Result<string>.Forbidden().Status);
        Assert.Equal(ResultStatus.Conflict, Result<string>.Conflict().Status);
        Assert.Equal(ResultStatus.Failed, Result<string>.InternalServerError().Status);
        Assert.Equal(ResultStatus.NoContent, Result<string>.NoContent().Status);
    }

    [Fact]
    public void AValidationFailureIsInvalidAndKeepsItsFields()
    {
        var result = Result<string>.ValidationFailure(new()
        {
            ["Amount"] = ["Amount must be greater than zero."],
        });

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains("Amount", result.ValidationErrors!.Keys);
    }
}
