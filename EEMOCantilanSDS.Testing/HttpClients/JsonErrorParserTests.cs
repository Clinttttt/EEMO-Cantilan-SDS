using System.Text.Json;
using EEMOCantilanSDS.HttpClients.Helper;
using Xunit;

namespace EEMOCantilanSDS.Testing.HttpClients;

/// <summary>
/// Regression tests for API failure-message parsing.
/// <para>
/// The API returns <c>{ isSuccess:false, error:"..." }</c> for a plain failure and
/// <c>{ isSuccess:false, errors:{ field:[".."] } }</c> for validation failures. The parser previously only
/// looked for an <c>error</c> OBJECT, so BOTH shapes fell through to the sentinel and every 400 in the app
/// surfaced as the literal "Bad Request" — hiding the real reason (e.g. "That code is not valid") from the
/// user on every screen.
/// </para>
/// </summary>
public class JsonErrorParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ── Plain failure message ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFailureMessage_ReadsPlainErrorString_CamelCase()
    {
        var root = Parse("""{"isSuccess":false,"error":"That code is not valid."}""");

        Assert.Equal("That code is not valid.", JsonErrorParser.ExtractFailureMessage(root));
    }

    [Fact]
    public void ExtractFailureMessage_ReadsPlainErrorString_PascalCase()
    {
        var root = Parse("""{"IsSuccess":false,"Error":"Your password is incorrect."}""");

        Assert.Equal("Your password is incorrect.", JsonErrorParser.ExtractFailureMessage(root));
    }

    [Fact]
    public void ExtractFailureMessage_ReadsValidationErrorsObject()
    {
        var root = Parse("""{"isSuccess":false,"errors":{"NewPassword":["Password must contain a digit."]}}""");

        Assert.Equal("Password must contain a digit.", JsonErrorParser.ExtractFailureMessage(root));
    }

    [Fact]
    public void ExtractFailureMessage_FallsBackToProblemDetails()
    {
        var root = Parse("""{"title":"Too many attempts. Please wait a minute."}""");

        Assert.Equal("Too many attempts. Please wait a minute.", JsonErrorParser.ExtractFailureMessage(root));
    }

    [Fact]
    public void ExtractFailureMessage_ReturnsNullWhenNoMessage()
    {
        Assert.Null(JsonErrorParser.ExtractFailureMessage(Parse("""{"isSuccess":false}""")));
        Assert.Null(JsonErrorParser.ExtractFailureMessage(Parse("""{"error":""}""")));
    }

    // ── Field-keyed validation dictionary ───────────────────────────────────────────────────────

    [Fact]
    public void ValidationErrorHandler_ReadsErrorsProperty_TheShapeTheApiActuallySends()
    {
        var root = Parse("""{"isSuccess":false,"errors":{"Code":["Required."],"NewPassword":["Too short.","Needs a digit."]}}""");

        var result = JsonErrorParser.ValidationErrorHandler(root);

        Assert.False(result.ContainsKey("BadRequest"));
        Assert.Equal(new[] { "Required." }, result["Code"]);
        Assert.Equal(new[] { "Too short.", "Needs a digit." }, result["NewPassword"]);
    }

    /// <summary>The older <c>error</c>-object shape must keep working (other callers still rely on it).</summary>
    [Fact]
    public void ValidationErrorHandler_StillReadsLegacyErrorObject()
    {
        var root = Parse("""{"error":{"Username":["Already taken."]}}""");

        var result = JsonErrorParser.ValidationErrorHandler(root);

        Assert.Equal(new[] { "Already taken." }, result["Username"]);
    }

    [Fact]
    public void ValidationErrorHandler_SignalsSentinel_WhenBodyIsAPlainMessage()
    {
        // A plain message is not field-keyed, so the caller is told to look for a message instead.
        var root = Parse("""{"isSuccess":false,"error":"That code is not valid."}""");

        var result = JsonErrorParser.ValidationErrorHandler(root);

        Assert.True(result.ContainsKey("BadRequest"));
    }
}
