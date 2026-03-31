using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.Models
{
    public class ValidationErrorResponse
    {
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
