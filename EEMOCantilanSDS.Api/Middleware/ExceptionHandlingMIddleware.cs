

using FluentValidation;
using System.Text.Json;

namespace EEMOCantilanSDS.Api.Middleware
{
    public class ExceptionHandlingMIddleware
    {
        private readonly RequestDelegate _next;
        public ExceptionHandlingMIddleware(RequestDelegate next)
        {
            _next = next;
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
            return context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        public static Task HandlingException(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var response = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                message = "An unexpected error occurred.",
                title = "Internal Server Error",
                status = 500,
                error = exception.Message
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }

}