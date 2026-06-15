using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Api.Middleware;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EEMOCantilanSDS.Testing.Api.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private static ExceptionHandlingMiddleware CreateSut() =>
        new(_ => Task.CompletedTask, NullLogger<ExceptionHandlingMiddleware>.Instance);

    private static async Task<(int status, string body)> InvokeHandlerAsync(Exception ex, string traceId = "trace-123")
    {
        var context = new DefaultHttpContext { TraceIdentifier = traceId };
        context.Response.Body = new MemoryStream();

        await CreateSut().HandlingException(context, ex);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task HandlingException_DoesNotLeakInternalExceptionDetails()
    {
        const string sensitive = "Npgsql connection failed: password=SuperSecret123; host=10.0.0.5";

        var (status, body) = await InvokeHandlerAsync(new InvalidOperationException(sensitive));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("SuperSecret123", body);
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain(sensitive, body);
    }

    [Fact]
    public async Task HandlingException_ReturnsGenericPayloadWithTraceId()
    {
        var (status, body) = await InvokeHandlerAsync(new Exception("boom: secret stack detail"));

        Assert.Equal(500, status);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("message").GetString());
        Assert.Equal("trace-123", root.GetProperty("traceId").GetString());

        // The client surfaces "error"; it must be generic and never echo the exception text.
        var error = root.GetProperty("error").GetString();
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain("boom", error);
        Assert.DoesNotContain("secret stack detail", body);
    }

    [Fact]
    public async Task HandlingValidationException_StillReturns400WithFieldErrors()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var failures = new[] { new ValidationFailure("Name", "Name is required") };
        await ExceptionHandlingMiddleware.HandlingValidationException(context, new ValidationException(failures));

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("Name is required", body);
    }
}
