using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.Models
{
    public class ValidationErrorResponse
    {
        public bool IsSuccess { get; set; }
        
        [JsonPropertyName("error")]
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

