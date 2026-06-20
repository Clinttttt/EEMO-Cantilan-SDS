using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.HttpClients.Models
{
    public class ValidationErrorResponse
    {
        public bool IsSuccess { get; set; }
 
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

