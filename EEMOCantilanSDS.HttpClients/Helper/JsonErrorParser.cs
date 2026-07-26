using EEMOCantilanSDS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.HttpClients.Helper
{
    public static class JsonErrorParser
    {
        public static string? ExtractMessages(JsonElement element) =>
          element.EnumerateObject()
              .SelectMany(f => f.Value.EnumerateArray().Select(v => v.GetString()))
              .Where(m => !string.IsNullOrWhiteSpace(m))
              .ToList() is { Count: > 0 } messages
                  ? string.Join("; ", messages)
                  : null;

        /// <summary>
        /// Finds a property regardless of casing. The API serialises with camelCase, but responses are also
        /// read from logs/tests in PascalCase, so both are accepted rather than silently missing the payload.
        /// </summary>
        public static bool TryGetPropertyInsensitive(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Extracts a single human-readable message from an API failure body.
        /// <para>
        /// The API returns <c>{ isSuccess:false, error:"..." }</c> for a plain failure and
        /// <c>{ isSuccess:false, errors:{ field:[".."] } }</c> for validation failures. Returns null when the
        /// body carries no usable message, so callers can fall back to their own copy.
        /// </para>
        /// </summary>
        public static string? ExtractFailureMessage(JsonElement root)
        {
            // Plain failure: a string message.
            if (TryGetPropertyInsensitive(root, "error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    var text = error.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
                if (error.ValueKind == JsonValueKind.Object)
                {
                    var joined = ExtractMessages(error);
                    if (!string.IsNullOrWhiteSpace(joined)) return joined;
                }
            }

            // Validation failure: field → messages.
            if (TryGetPropertyInsensitive(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var joined = ExtractMessages(errors);
                if (!string.IsNullOrWhiteSpace(joined)) return joined;
            }

            // ASP.NET ProblemDetails / generic shapes.
            foreach (var name in new[] { "message", "title", "detail" })
            {
                if (TryGetPropertyInsensitive(root, name, out var alt)
                    && alt.ValueKind == JsonValueKind.String)
                {
                    var text = alt.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the per-field validation dictionary from a 400 body. Accepts the API's <c>errors</c> shape
        /// as well as the legacy <c>error</c> object. Falls back to the sentinel <c>BadRequest</c> key when the
        /// body is not field-keyed, which tells the caller to look for a plain message instead.
        /// </summary>
        public static Dictionary<string, string[]> ValidationErrorHandler(JsonElement error)
        {
            foreach (var propertyName in new[] { "errors", "error" })
            {
                if (TryGetPropertyInsensitive(error, propertyName, out var errorProp)
                    && errorProp.ValueKind == JsonValueKind.Object)
                {
                    var errors = errorProp.EnumerateObject()
                         .Where(f => f.Value.ValueKind == JsonValueKind.Array)
                         .Select(f => new
                         {
                             Key = f.Name,
                             Messages = f.Value.EnumerateArray()
                                 .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
                                 .Where(m => !string.IsNullOrEmpty(m))
                                 .OfType<string>()
                                 .ToArray()
                         })
                         .Where(f => f.Messages.Any())
                         .ToDictionary(f => f.Key, f => f.Messages!);

                    if (errors.Any())
                        return errors;
                }
            }

            return new Dictionary<string, string[]> { { "BadRequest", ["400"] } };
        }
    }
}
