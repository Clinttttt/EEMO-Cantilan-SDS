using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.HttpClients.Models;
using MediatR;
using Microsoft.EntityFrameworkCore.Query.Internal;
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

namespace EEMOCantilanSDS.Infrastructure.HttpClients
{
    public abstract class HandleResponse
    {
        private readonly HttpClient _http;
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
            await _http.PostAsync(url,null);
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
        public async Task<Result<TResponse>> DeleteAsync<TResponse>(string url)
        {
            var response = await _http.DeleteAsync(url);
            return await MapStatusCodeAsync<TResponse>(response);
        }
        public async Task<Result<TResponse>> MapStatusCodeAsync<TResponse>(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var content = await response.Content.ReadAsStringAsync();
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    
                    if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
                    {
                        var errors = new Dictionary<string, string[]>();
                        foreach (var field in errorProp.EnumerateObject())
                        {
                            var messages = field.Value.EnumerateArray()
                                .Select(x => x.GetString())
                                .Where(x => !string.IsNullOrEmpty(x))
                                .ToArray();
                            if (messages.Any())
                            {
                                errors[field.Name] = messages!;
                            }
                        }
                        if (errors.Any())
                        {
                            return Result<TResponse>.ValidationFailure(errors);
                        }
                    }
                }
                catch { }
                
                return Result<TResponse>.Failure("Bad Request", 400);
            }
            return response.StatusCode switch
            {
                HttpStatusCode.OK => await HandleOkAsync<TResponse>(response),
                HttpStatusCode.NoContent => Result<TResponse>.NoContent(),
                HttpStatusCode.NotFound => Result<TResponse>.NotFound(),
                HttpStatusCode.Unauthorized => Result<TResponse>.Unauthorized(),
                HttpStatusCode.Conflict => Result<TResponse>.Conflict(),
                HttpStatusCode.Forbidden => Result<TResponse>.Forbidden(),
                HttpStatusCode.InternalServerError => await HandleErrorResponseAsync<TResponse>(response, 500),
                _ => await HandleErrorResponseAsync<TResponse>(response, (int)response.StatusCode)
            };
        }
        public async Task<Result<TResponse>> HandleOkAsync<TResponse>(HttpResponseMessage response)
        {
            var value = await response.Content.ReadFromJsonAsync<TResponse>();
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
        private string TryExtractErrorMessage(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                return jsonContent;
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                
                // Try to get single error property first (non-validation errors)
                if (root.TryGetProperty("error", out var errorProp))
                {
                    if (errorProp.ValueKind == JsonValueKind.String)
                    {
                        return errorProp.GetString() ?? jsonContent;
                    }
                    // Handle validation errors (error is an object with field names)
                    if (errorProp.ValueKind == JsonValueKind.Object)
                    {
                        var message = errorProp.EnumerateObject()
                            .SelectMany(s => s.Value.EnumerateArray().Select(s => s.GetString()))
                            .Where(m => !string.IsNullOrWhiteSpace(m))
                            .ToList();
                        if (message.Any())
                        {
                            return string.Join("; ", message);
                        }
                    }
                }
                
                // Fallback: Try uppercase Errors property
                if (root.TryGetProperty("Errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var message = errors.EnumerateObject()
                        .SelectMany(s => s.Value.EnumerateArray().Select(s => s.GetString()))
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToList();
                    if (message.Any())
                    {
                        return string.Join("; ", message);
                    }
                }
            }
            catch
            {

            }
            return jsonContent;
        }

    }
}
