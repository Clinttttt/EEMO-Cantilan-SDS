using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace EEMOCantilanSDS.Domain.Common
{
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
