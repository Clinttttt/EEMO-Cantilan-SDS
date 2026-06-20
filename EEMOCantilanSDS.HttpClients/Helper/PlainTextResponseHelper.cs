using EEMOCantilanSDS.Domain.Common;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.HttpClients.Helper;

public static class PlainTextResponseHelper
{
    public static async Task<Result<string>> GetPlainStringAsync(this HttpClient http, string url)
    {
        try
        {
            var response = await http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                content = content.Trim('"'); 
                return Result<string>.Success(content);
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            return Result<string>.Failure(errorContent);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(ex.Message);
        }
    }
}
