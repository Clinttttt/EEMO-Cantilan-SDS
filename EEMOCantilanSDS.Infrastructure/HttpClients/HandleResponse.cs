using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure.HttpClients
{
    public class HandleResponse
    {
        private readonly HttpClient _http;
        public HandleResponse(HttpClient http)
        {
            _http = http;   
        }


    }
}
