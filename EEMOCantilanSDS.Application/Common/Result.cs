using System.Collections.Generic;
using System.Linq;

namespace EEMOCantilanSDS.Application.Common
{
    /// <summary>
    /// The outcome of a handler: a value, or a stated failure.
    ///
    /// <para>
    /// Lives in Application, not Domain. It carries HTTP status codes and names like <see cref="Unauthorized"/> and
    /// <see cref="NoContent"/>, which are facts about a web API rather than about a market, a stall or a receipt — a domain
    /// that knows what 404 means has been told something it has no use for. Domain never referenced this type at all, so
    /// moving it changed no rule.
    /// </para>
    ///
    /// <para>
    /// The status codes themselves are still here, and that is the remaining half of the job: the review asks for error
    /// CATEGORIES with the API translating them to HTTP. That is a behavioural change across every handler and every
    /// controller, so it is recorded in OUTSTANDING_WORK rather than smuggled into a file move.
    /// </para>
    /// </summary>
    public class Result<T>
    {
        public bool IsSuccess { get; }
        public T? Value { get; }
        public string? Error { get; set; }
        public int? StatusCode { get; set; }
        public Dictionary<string, string[]>? ValidationErrors { get; set; }

        public Result(bool isSuccess, T? value, int? statusCode = 200, string? error = null)
        {
            Value = value;
            IsSuccess = isSuccess;
            Error = isSuccess ? null : error;
            StatusCode = statusCode;
        }

        public static Result<T> Success(T value) => new(true, value);
        public static Result<T> Failure(string error, int statusCode = 400) => new(false, default, statusCode, error);

        public static Result<T> ValidationFailure(Dictionary<string, string[]> errors)
        {
            return new Result<T>(
                isSuccess: false,
                value: default,
                statusCode: 400,
                error: string.Join("; ", errors.Values.SelectMany(v => v))
            )
            {
                ValidationErrors = errors
            };
        }

        public static Result<T> NotFound() => new(false, default, 404);
        public static Result<T> Unauthorized() => new(false, default, 401);
        public static Result<T> Conflict() => new(false, default, 409);
        public static Result<T> Forbidden() => new(false, default, 403);
        public static Result<T> NoContent() => new(true, default, 204);
        public static Result<T> InternalServerError() => new(false, default, 500);
    }
}
