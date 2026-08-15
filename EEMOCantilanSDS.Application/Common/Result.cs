using System.Collections.Generic;
using System.Linq;

namespace EEMOCantilanSDS.Application.Common
{
    /// <summary>
    /// The outcome of a handler: a value, or a stated failure.
    ///
    /// <para>
    /// Lives in Application, not Domain. Domain never referenced it, so moving it changed no rule; what changed is that the
    /// domain no longer carries a type named after HTTP outcomes.
    /// </para>
    ///
    /// <para>
    /// The KIND of outcome is <see cref="Status"/>, a <see cref="ResultStatus"/> — stated without reference to HTTP, because a
    /// handler deciding what a stall owes has no use for the number 409. The API translates a status to a response at its own
    /// boundary (<c>ApiBaseController.HandleResponse</c>).
    /// </para>
    ///
    /// <para>
    /// <see cref="StatusCode"/> is DERIVED from the status and kept because the portal, the mobile app and the sync path all
    /// read it — they reconstruct a <c>Result</c> from a real HTTP response, so they legitimately speak in numbers. It is not a
    /// second source of truth: nothing stores it, and it cannot disagree with the status.
    /// </para>
    /// </summary>
    public class Result<T>
    {
        public bool IsSuccess { get; }
        public T? Value { get; }
        public string? Error { get; set; }
        public Dictionary<string, string[]>? ValidationErrors { get; set; }

        /// <summary>What kind of outcome this is, in the office's terms rather than the web's.</summary>
        public ResultStatus Status { get; init; }

        /// <summary>
        /// The HTTP status this outcome translates to. Derived from <see cref="Status"/>, never stored, and settable only for
        /// the callers that build a Result FROM an HTTP response (the portal's API clients) — where the number is the input.
        /// </summary>
        public int? StatusCode
        {
            get => _statusCode ?? HttpStatusFor(Status);
            set => _statusCode = value;
        }

        private int? _statusCode;

        public Result(bool isSuccess, T? value, int? statusCode = 200, string? error = null)
        {
            Value = value;
            IsSuccess = isSuccess;
            Error = isSuccess ? null : error;
            Status = StatusFor(statusCode, isSuccess);
        }

        private Result(bool isSuccess, T? value, ResultStatus status, string? error)
        {
            Value = value;
            IsSuccess = isSuccess;
            Error = isSuccess ? null : error;
            Status = status;
        }

        public static Result<T> Success(T value) => new(true, value, ResultStatus.Ok, null);

        /// <summary>A stated failure. Prefer the <see cref="ResultStatus"/> overload; the numeric one exists for callers that
        /// genuinely hold an HTTP status, such as the portal's API clients translating a response.</summary>
        public static Result<T> Failure(string error, int statusCode = 400) =>
            new(false, default, StatusFor(statusCode, false), error) { StatusCode = statusCode };

        /// <summary>A stated failure of a given kind.</summary>
        public static Result<T> Failure(string error, ResultStatus status) => new(false, default, status, error);

        public static Result<T> ValidationFailure(Dictionary<string, string[]> errors) =>
            new(false, default, ResultStatus.Invalid, string.Join("; ", errors.Values.SelectMany(v => v)))
            {
                ValidationErrors = errors
            };

        public static Result<T> NotFound() => new(false, default, ResultStatus.NotFound, null);
        public static Result<T> Unauthorized() => new(false, default, ResultStatus.Unauthorized, null);
        public static Result<T> Conflict() => new(false, default, ResultStatus.Conflict, null);
        public static Result<T> Forbidden() => new(false, default, ResultStatus.Forbidden, null);
        public static Result<T> NoContent() => new(true, default, ResultStatus.NoContent, null);
        public static Result<T> InternalServerError() => new(false, default, ResultStatus.Failed, null);

        /// <summary>
        /// The single mapping between an outcome and an HTTP status, in both directions. Kept here rather than in the API so
        /// the numeric <c>Failure</c> overload and the derived <see cref="StatusCode"/> agree with it by construction; the API
        /// switches on the STATUS and owns the response shape.
        /// </summary>
        internal static int HttpStatusFor(ResultStatus status) => status switch
        {
            ResultStatus.Ok => 200,
            ResultStatus.NoContent => 204,
            ResultStatus.Invalid => 400,
            ResultStatus.Unauthorized => 401,
            ResultStatus.Forbidden => 403,
            ResultStatus.NotFound => 404,
            ResultStatus.Conflict => 409,
            ResultStatus.Locked => 423,
            ResultStatus.Failed => 500,
            ResultStatus.UpstreamFailed => 502,
            _ => 400,
        };

        private static ResultStatus StatusFor(int? statusCode, bool isSuccess) => statusCode switch
        {
            200 or null => isSuccess ? ResultStatus.Ok : ResultStatus.Invalid,
            204 => ResultStatus.NoContent,
            400 => ResultStatus.Invalid,
            401 => ResultStatus.Unauthorized,
            403 => ResultStatus.Forbidden,
            404 => ResultStatus.NotFound,
            409 => ResultStatus.Conflict,
            423 => ResultStatus.Locked,
            500 => ResultStatus.Failed,
            502 => ResultStatus.UpstreamFailed,
            // Anything else keeps its number through StatusCode and lands on the API's fallback, exactly as before.
            _ => isSuccess ? ResultStatus.Ok : ResultStatus.Invalid,
        };
    }
}
