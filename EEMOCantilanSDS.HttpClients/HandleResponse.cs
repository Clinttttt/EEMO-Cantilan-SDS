using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.HttpClients.Helper;
using EEMOCantilanSDS.HttpClients.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.HttpClients
{
    public abstract class HandleResponse
    {
        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public HandleResponse(HttpClient http)
        {
            _http = http;
        }

        public async Task PostAsync<TRequest>(string url, TRequest request)
        {
            await _http.PostAsJsonAsync(url, request);
        }
        public async Task PostAsync(string url)
        {
            await _http.PostAsync(url, null);
        }
        public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest request)
        {
            var reponse = await _http.PostAsJsonAsync(url, request);
            return await MapStatusCodeAsync<TResponse>(reponse);
        }
        public async Task<Result<TResponse>> PostAsync<TResponse>(string url)
        {
            var reponse = await _http.PostAsync(url, null);
            return await MapStatusCodeAsync<TResponse>(reponse);
        }
        public async Task<Result<TResponse>> GetAsync<TResponse>(string url)
        {
            var reponse = await _http.GetAsync(url);
            return await MapStatusCodeAsync<TResponse>(reponse);
        }
        public async Task<Result<TResponse>> UpdateAsync<TRequest, TResponse>(string url, TRequest request)
        {
            var response = await _http.PatchAsJsonAsync(url, request);
            return await MapStatusCodeAsync<TResponse>(response);
        }
        public async Task<Result<TResponse>> UpdateAsync<TResponse>(string url)
        {
            var response = await _http.PatchAsync(url, null);
            return await MapStatusCodeAsync<TResponse>(response);
        }
        public async Task<Result<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest request)
        {
            var response = await _http.PutAsJsonAsync(url, request);
            return await MapStatusCodeAsync<TResponse>(response);
        }
        public async Task<Result<TResponse>> DeleteAsync<TResponse>(string url)
        {
            var response = await _http.DeleteAsync(url);
            return await MapStatusCodeAsync<TResponse>(response);
        }
        public async Task<Result<TResponse>> MapStatusCodeAsync<TResponse>(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                return await BadRequestHandler<TResponse>(response);
            }
                return response.StatusCode switch
            {
                HttpStatusCode.OK => await HandleOkAsync<TResponse>(response),
                // 201 Created carries the created resource in its body (the API returns Created("", value)),
                // so deserialize it exactly like 200. Without this, a successful create (e.g. a mobile
                // record/trip/vendor POST) was mis-read as a failure, prompting a retry and a possible
                // DUPLICATE record.
                HttpStatusCode.Created => await HandleOkAsync<TResponse>(response),
                HttpStatusCode.NoContent => Result<TResponse>.NoContent(),
                HttpStatusCode.NotFound => Result<TResponse>.NotFound(),
                HttpStatusCode.Unauthorized => Result<TResponse>.Unauthorized(),
                HttpStatusCode.Conflict => await HandleConflictAsync<TResponse>(response),
                HttpStatusCode.Forbidden => Result<TResponse>.Forbidden(),
                HttpStatusCode.InternalServerError => await HandleErrorResponseAsync<TResponse>(response, 500),
                _ => await HandleErrorResponseAsync<TResponse>(response, (int)response.StatusCode)
            };
        }
        public async Task<Result<TResponse>> HandleOkAsync<TResponse>(HttpResponseMessage response)
        {
            var value = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
            if (value is null)
                return Result<TResponse>.Failure("Failed to deserialize response content.", 500);
            return Result<TResponse>.Success(value);
        }
        private async Task<Result<T>> HandleErrorResponseAsync<T>(HttpResponseMessage response, int statusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            var message = TryExtractErrorMessage(errorContent);
            return Result<T>.Failure(message, statusCode);
        }

        // 409s may carry a meaningful server message (e.g. "This mobile number is already registered").
        // Surface it when present; otherwise keep the generic Conflict so callers can show their own copy.
        private async Task<Result<T>> HandleConflictAsync<T>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            var message = TryExtractErrorMessage(content);
            return string.IsNullOrWhiteSpace(message) || message == content
                ? Result<T>.Conflict()
                : Result<T>.Failure(message, 409);
        }
        private string TryExtractErrorMessage(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                return jsonContent;
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errorProp))
                {
                    if (errorProp.ValueKind == JsonValueKind.String)
                        return errorProp.GetString() ?? jsonContent;

                    if (errorProp.ValueKind == JsonValueKind.Object)
                        return JsonErrorParser.ExtractMessages(errorProp) ?? jsonContent;
                }
            }
            catch { }

            return jsonContent;
        }
        public  async Task<Result<TResponse>> BadRequestHandler<TResponse>(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(content);
                var errors = JsonErrorParser.ValidationErrorHandler(doc.RootElement);
                return !errors.ContainsKey("BadRequest") ? Result<TResponse>.ValidationFailure(errors)
                        : Result<TResponse>.Failure("Bad Request", 400);
            }
            catch { }
            return Result<TResponse>.Failure("Bad Request", 400);
        }

    }
}
