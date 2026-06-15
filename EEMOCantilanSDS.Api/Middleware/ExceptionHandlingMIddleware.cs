

using FluentValidation;
using System.Text.Json;

namespace EEMOCantilanSDS.Api.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (ValidationException e)
            {
                await HandlingValidationException(context, e);
            }
            catch (Exception e)
            {
                await HandlingException(context, e);
            }
        }
        public static Task HandlingValidationException(HttpContext context, ValidationException exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            var error = exception.Errors.GroupBy(g => g.PropertyName)
                .ToDictionary(k => k.Key, v => v.Select(s => s.ErrorMessage).ToArray());

            var response = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                message = "Validation failed",
                title = "One or more validation errors occurred.",
                status = 400,
                error
            };
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
        }
        public Task HandlingException(HttpContext context, Exception exception)
        {   
            var traceId = context.TraceIdentifier;
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}. TraceId: {TraceId}",
                context.Request?.Method, context.Request?.Path.Value, traceId);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var response = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                message = "An unexpected error occurred.",
                title = "Internal Server Error",
                status = 500,
                error = "An unexpected error occurred. Please try again, and contact support with the reference below if it persists.",
                traceId
            };
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
        }
    }

}