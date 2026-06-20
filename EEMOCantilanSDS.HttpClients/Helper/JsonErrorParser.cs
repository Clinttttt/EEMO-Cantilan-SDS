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
        public static Dictionary<string, string[]> ValidationErrorHandler(JsonElement error)
        {

            if (error.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
            {
                var errors = errorProp.EnumerateObject()
                     .Select(f => new
                     {
                         Key = f.Name,
                         Messages = f.Value.EnumerateArray()
                             .Select(v => v.GetString())
                             .Where(m => !string.IsNullOrEmpty(m))
                             .OfType<string>()
                             .ToArray()
                     })
                     .Where(f => f.Messages.Any())
                     .ToDictionary(f => f.Key, f => f.Messages!);

                if (errors.Any())
                    return errors;
            }

            return new Dictionary<string, string[]> { { "BadRequest", ["400"] } };
        }

    
        
    }
}
